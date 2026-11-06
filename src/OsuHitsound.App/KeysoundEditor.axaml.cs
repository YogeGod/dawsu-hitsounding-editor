using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OsuHitsound.Audio;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

/// <summary>
/// Keysound mode: the key editor over the main window's editing area for one <see cref="KeysoundDocument"/>. Hitsound
/// mode's view, session and commands are untouched while it shows; <see cref="MainWindow"/> routes keys and saving here.
/// </summary>
public partial class KeysoundEditor : UserControl
{
    private static readonly (string Label, int Divisions)[] Snaps = [("1/1", 1), ("1/2", 2), ("1/3", 3), ("1/4", 4), ("1/6", 6), ("1/8", 8), ("1/12", 12), ("1/16", 16)];

    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private KeysoundDocument? doc;
    private AudioEngine? audio;
    private KeysoundPlayer? player;

    // One piano for the whole session: decoding its samples takes a moment and about 110 MB, so projects share it.
    private static Task<SfzInstrument>? piano;
    private CancellationTokenSource? waveCancel;
    private string status = "";
    // Copied notes, shared between projects in a session (not the system clipboard).
    private static IReadOnlyList<KeyNote> clipboard = [];
    private string trackListKey = "";
    private static readonly Cursor Hand = new(StandardCursorType.Hand);

    public KeysoundEditor()
    {
        InitializeComponent();
        SnapBox.ItemsSource = Snaps.Select(s => s.Label).ToList();
        SnapBox.SelectedIndex = 3;
        SnapBox.SelectionChanged += (_, _) => { Roll.SnapDivisions = Snaps[Math.Max(0, SnapBox.SelectedIndex)].Divisions; Roll.InvalidateVisual(); };
        SnapToggle.IsCheckedChanged += (_, _) => Roll.SnapOn = SnapToggle.IsChecked == true;
        GhostToggle.IsCheckedChanged += (_, _) => { Roll.ShowGhosts = GhostToggle.IsChecked == true; Roll.InvalidateVisual(); };
        MarkerToggle.IsCheckedChanged += (_, _) => { Roll.ShowMarkers = MarkerToggle.IsChecked == true; Roll.InvalidateVisual(); };
        SelectTool.Click += (_, _) => SetTool(KeyTool.Select);
        DrawTool.Click += (_, _) => SetTool(KeyTool.Draw);
        EraseTool.Click += (_, _) => SetTool(KeyTool.Erase);
        SplitTool.Click += (_, _) => SetTool(KeyTool.Split);
        GlueTool.Click += (_, _) => SetTool(KeyTool.Glue);
        InsertVelocityBox.ValueChanged += (_, _) => Roll.InsertVelocity = (int)Math.Clamp(InsertVelocityBox.Value ?? 100, 1, 127);
        UndoButton.Click += (_, _) => doc?.Session.Undo();
        RedoButton.Click += (_, _) => doc?.Session.Redo();
        ZoomInButton.Click += (_, _) => Roll.ZoomTime(1.25);
        ZoomOutButton.Click += (_, _) => Roll.ZoomTime(0.8);
        RowsInButton.Click += (_, _) => Roll.ZoomRows(1.2);
        RowsOutButton.Click += (_, _) => Roll.ZoomRows(1 / 1.2);
        AddTrackButton.Click += (_, _) => AddTrack();
        RenameTrackButton.Click += async (_, _) => await RenameTrack();
        DeleteTrackButton.Click += (_, _) => DeleteTrack();
        CloseButton.Click += (_, _) => CloseRequested?.Invoke();
        ImportMidiButton.Click += async (_, _) => await ImportMidi();
        StartButton.Click += (_, _) => Seek(0);
        PlayButton.Click += (_, _) => TogglePlay();
        Roll.SeekRequested += Seek;
        Roll.AuditionRequested += (pitch, velocity) => { if (HearToggle.IsChecked == true) player?.Audition(pitch, velocity); };
        Roll.ViewChanged += UpdateTexts;
        clock.Tick += (_, _) => Tick();
        SetupRecording();
    }

    /// <summary>The ◀ Close button: the window asks about unsaved edits, then closes the project.</summary>
    public event Action? CloseRequested;
    /// <summary>The project's edit state or name changed (for the title and the Save button).</summary>
    public event Action? Changed;

    public KeysoundDocument? Document => doc;

    public void Open(KeysoundDocument document, AudioEngine? engine)
    {
        Close();
        doc = document;
        audio = engine;
        doc.Session.Changed += OnSessionChanged;
        Roll.Attach(doc);
        Roll.Playhead = audio?.Position ?? 0;
        Roll.CentrePitch();
        LoadWaveform();
        _ = LoadPiano(doc);
        RefreshMidiInputs();
        status = doc.Warnings.Count > 0 ? string.Join(" ", doc.Warnings) : $"Opened {doc.Name}. Save project (Ctrl+S) keeps your tracks and notes.";
        RefreshTracks();
        UpdateTexts();
        Roll.Focus();
    }

    public void Close()
    {
        if (doc is null) return;
        StopRecording();
        keyboard.Stop();
        Stop();
        waveCancel?.Cancel();
        audio?.SetLiveParts([]);
        player = null;
        doc.Session.Changed -= OnSessionChanged;
        doc = null;
        Roll.Attach(null);
        Roll.Wave = null;
        TrackList.Children.Clear();
    }

    public void SetStatus(string text)
    {
        status = text;
        UpdateTexts();
    }

    private void OnSessionChanged()
    {
        if (doc is { } d && (d.SelectedTrack is not { } id || d.Session.State.Track(id) is null))
        {
            d.SelectedTrack = d.Session.State.Tracks.FirstOrDefault()?.Id;
            Roll.TrackChanged();
        }

        RefreshTracks();
        SchedulePlayback();
        UpdateTexts();
        Changed?.Invoke();
    }

    /// <summary>The window restarted the audio output (new device or buffer): play through the new engine.</summary>
    public void UseAudio(AudioEngine? engine)
    {
        Stop();
        audio = engine;
        if (doc is null) return;
        if (player is not null) ApplyParts();
        else _ = LoadPiano(doc);
        RefreshPlayback();
    }

    /// <summary>Loads the bundled piano (once, off the UI thread) and plays this project through it.</summary>
    private async Task LoadPiano(KeysoundDocument opened)
    {
        if (audio is null) return;
        SfzInstrument instrument;
        try
        {
            instrument = await (piano ??= Task.Run(() => SfzInstrument.Load(SfzInstrument.BundledPianoPath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            piano = null; // try again next time
            if (ReferenceEquals(doc, opened)) SetStatus($"The bundled piano couldn't load ({ex.Message}); notes stay silent.");
            return;
        }

        if (!ReferenceEquals(doc, opened)) return; // closed or replaced meanwhile
        player = new KeysoundPlayer(instrument);
        ApplyParts();
        RefreshPlayback();
    }

    private bool playbackPending;

    // A drag changes the notes on every mouse move: the list is rebuilt once those changes have settled.
    private void SchedulePlayback()
    {
        if (playbackPending) return;
        playbackPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            playbackPending = false;
            RefreshPlayback();
        }, DispatcherPriority.Background);
    }

    /// <summary>What the mixer plays alongside the song: the bundled piano (VST3 tracks join it in their own parts).</summary>
    private void ApplyParts() => audio?.SetLiveParts(player is null ? [] : [player]);

    /// <summary>The notes playback plays: every audible track (mute and solo), in timeline ms.</summary>
    private void RefreshPlayback()
    {
        if (player is null || doc is null) return;
        var session = doc.Session;
        var tempo = doc.Tempo;
        var played = new List<PlayedNote>();
        var bends = new List<PlayedBend[]>();
        double last = 0;
        foreach (var (t, part) in session.State.Tracks.Where(t => session.IsAudible(t.Id)).Select((t, i) => (t, i)))
        {
            // Controllers are kept sorted by tick. The bundled piano can't read pedal events, so the sustain pedal holds
            // its notes until the pedal is released, as the dampers would.
            var sustain = t.Controllers.Where(c => c.Kind == ControllerKind.Sustain).ToList();
            foreach (var n in t.Notes)
            {
                var note = new PlayedNote(tempo.TimeAt(n.Start), tempo.TimeAt(sustain.Count > 0 ? KeyControllers.SustainedEnd(sustain, n.End) : n.End), n.Pitch, n.Velocity, part);
                played.Add(note);
                last = Math.Max(last, note.End);
            }

            bends.Add([.. t.Controllers.Where(c => c.Kind == ControllerKind.PitchBend)
                .Select(c => new PlayedBend(tempo.TimeAt(c.Tick), c.Value / 8192.0 * t.Instrument.PitchBendRange))]);
        }

        player.SetNotes(played, bends);
        audio?.SetMinimumLength(played.Count > 0 ? last + 2000 : 0); // notes after the song, or no song at all
    }

    private void LoadWaveform()
    {
        if (doc?.SongPath is not { } path) return;
        var cancel = waveCancel = new CancellationTokenSource();
        var loaded = audio?.SongBytes; // already read for playback
        _ = Task.Run(() =>
        {
            try
            {
                var wave = Waveform.Decode(loaded ?? File.ReadAllBytes(path), cancel: cancel.Token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (cancel.IsCancellationRequested) return;
                    Roll.Wave = wave;
                    Roll.InvalidateVisual();
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { }
        });
    }

    private void SetTool(KeyTool tool)
    {
        Roll.Tool = tool;
        SelectTool.IsChecked = tool == KeyTool.Select;
        DrawTool.IsChecked = tool == KeyTool.Draw;
        EraseTool.IsChecked = tool == KeyTool.Erase;
        SplitTool.IsChecked = tool == KeyTool.Split;
        GlueTool.IsChecked = tool == KeyTool.Glue;
    }

    // Tracks.

    private void RefreshTracks()
    {
        // Rebuilt only when something it shows changed: a note drag changes the state on every mouse move.
        var key = doc is null ? "" : string.Join("|", doc.Session.State.Tracks.Select(t =>
            $"{t.Id}/{t.Name}/{t.Instrument.DisplayName}/{t.Notes.Count}/{t.IncludeInGenerate}/{doc.Session.Muted.Contains(t.Id)}/{doc.Session.Soloed.Contains(t.Id)}")) + "|" + doc?.SelectedTrack;
        if (key == trackListKey && TrackList.Children.Count > 0) return;
        trackListKey = key;
        TrackList.Children.Clear();
        if (doc is null) return;
        var session = doc.Session;
        foreach (var track in session.State.Tracks)
        {
            var id = track.Id;
            bool selected = id == doc.SelectedTrack;
            var name = new TextBlock { Text = track.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var detail = new TextBlock
            {
                Text = $"{track.Instrument.DisplayName} · {track.Notes.Count} note{(track.Notes.Count == 1 ? "" : "s")}"
                    + (track.Percussion ? " · percussion" : "") + (track.IncludeInGenerate ? "" : " · excluded from Generate"),
                FontSize = 11,
                Opacity = 0.75,
                Foreground = track.IncludeInGenerate ? null : Paint.Brush("#ff8a5c"),
            };
            var mute = Toggle("M", session.Muted.Contains(id), "Mute while editing (playback only)", on => Flag(session.Muted, id, on));
            var solo = Toggle("S", session.Soloed.Contains(id), "Solo while editing (playback only)", on => Flag(session.Soloed, id, on));
            var include = Toggle("G", track.IncludeInGenerate, "Include in Generate: this track's synced notes become keysounds. Off excludes it (undoable).", on => session.SetIncludeInGenerate(id, on));
            var row = new Border
            {
                Padding = new Avalonia.Thickness(8, 5),
                CornerRadius = new Avalonia.CornerRadius(4),
                BorderThickness = new Avalonia.Thickness(1),
                BorderBrush = selected ? (IBrush?)this.FindResource("ButtonActive") : Brushes.Transparent,
                Background = selected ? (IBrush?)this.FindResource("PanelActive") : (IBrush?)this.FindResource("ButtonBg"),
                Cursor = Hand,
                Child = new DockPanel
                {
                    Children =
                    {
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, [DockPanel.DockProperty] = Dock.Right, VerticalAlignment = VerticalAlignment.Center, Children = { mute, solo, include } },
                        new StackPanel { Children = { name, detail } },
                    },
                },
            };
            Avalonia.Automation.AutomationProperties.SetName(row, $"Track {track.Name}");
            row.PointerPressed += (_, e) =>
            {
                if (e.Source is ToggleButton) return;
                SelectTrack(id);
                if (e.ClickCount == 2) _ = RenameTrack();
            };
            TrackList.Children.Add(row);
        }

        var current = doc.SelectedTrack is { } sel ? session.State.Track(sel) : null;
        InstrumentText.Text = current is null ? "No track. Add one to draw notes." : $"Instrument: {current.Instrument.DisplayName}";
        RenameTrackButton.IsEnabled = DeleteTrackButton.IsEnabled = current is not null;
    }

    private ToggleButton Toggle(string text, bool on, string tip, Action<bool> changed)
    {
        var button = new ToggleButton { Content = text, IsChecked = on, Width = 24, Height = 22, MinHeight = 22, Padding = new Avalonia.Thickness(0), FontSize = 11, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => changed(button.IsChecked == true);
        return button;
    }

    private void Flag(HashSet<Guid> set, Guid id, bool on)
    {
        if (on) set.Add(id); else set.Remove(id);
        RefreshTracks();
        SchedulePlayback();
        Changed?.Invoke();
    }

    private void SelectTrack(Guid id)
    {
        if (doc is null || doc.SelectedTrack == id) return;
        doc.SelectedTrack = id;
        Roll.TrackChanged();
        RefreshTracks();
        Roll.Focus();
    }

    /// <summary>Import MIDI…: pick a file, preview its tracks and placement, then add them as one undo step.</summary>
    private async Task ImportMidi()
    {
        if (doc is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var picked = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a MIDI file",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("MIDI file") { Patterns = ["*.mid", "*.midi"] }],
        });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path || doc is not { } project) return;

        MidiFile file;
        try { file = await Task.Run(() => MidiFile.Read(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            SetStatus($"Couldn't read {Path.GetFileName(path)}: {ex.Message}");
            return;
        }

        var parts = MidiImport.Parts(file);
        if (parts.Count == 0)
        {
            SetStatus($"{Path.GetFileName(path)} has no notes to import.");
            return;
        }

        double mapEnd = project.Objects.Count > 0 ? project.Objects[^1].End : 0;
        if (await Dialogs.ImportMidi(owner, Path.GetFileName(path), file, parts, project.Tempo, mapEnd) is not var (chosen, origin) || !ReferenceEquals(doc, project)) return;
        var tracks = MidiImport.Tracks(chosen, file.TicksPerBeat, origin);
        project.Session.AddTracks(tracks, "Import MIDI");
        SelectTrack(tracks[0].Id);
        Roll.CentrePitch();
        int notes = tracks.Sum(t => t.Notes.Count);
        SetStatus($"Imported {tracks.Count} track{(tracks.Count == 1 ? "" : "s")} with {notes} notes from {Path.GetFileName(path)}. Undo removes them together.");
    }

    private void AddTrack()
    {
        if (doc is null) return;
        var names = doc.Session.State.Tracks.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = Enumerable.Range(1, int.MaxValue).Select(n => $"Track {n}").First(n => !names.Contains(n));
        var track = doc.Session.AddTrack(name);
        SelectTrack(track.Id);
    }

    private async Task RenameTrack()
    {
        if (doc?.SelectedTrack is not { } id || doc.Session.State.Track(id) is not { } track || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (await Dialogs.Prompt(owner, "Rename track", "Track name", track.Name) is not { } name) return;
        name = name.Trim();
        if (name.Length is 0 or > 256) return;
        doc.Session.RenameTrack(id, name);
    }

    private void DeleteTrack()
    {
        if (doc?.SelectedTrack is not { } id || doc.Session.State.Track(id) is not { } track) return;
        doc.Session.DeleteTrack(id);
        SetStatus($"Deleted {track.Name}. Undo brings it back with its notes.");
    }

    // Transport.

    private void Seek(double time)
    {
        if (IsRecording) return; // a take records one stretch of the timeline
        audio?.Seek(time);
        Roll.Playhead = time;
        Roll.InvalidateVisual();
        UpdateTransport();
    }

    private void TogglePlay()
    {
        if (audio is null) { SetStatus("No audio output is available."); return; }
        if (IsRecording) { StopRecording(); return; }
        if (audio.IsPlaying) { Stop(); return; }
        if (!audio.HasSong) SetStatus("The song audio is missing from this project.");
        audio.Seek(Roll.Playhead);
        audio.Play();
        PlayGlyph.Glyph = "Pause";
        clock.Start();
    }

    private void Stop()
    {
        if (audio?.IsPlaying == true) audio.Pause();
        clock.Stop();
        PlayGlyph.Glyph = "Play";
    }

    private void Tick()
    {
        if (audio is null || !audio.IsPlaying)
        {
            if (IsRecording) StopRecording(); // the song ended; this stops playback too
            else Stop();
            return;
        }

        double now = audio.Position;
        Roll.Playhead = now;
        ShowRecording(now);
        // Follow playback a page at a time.
        var axis = Roll.Axis;
        if (now > axis.Left + axis.VisibleDuration * 0.92 || now < axis.Left) axis.Left = now - axis.VisibleDuration * 0.08;
        Roll.InvalidateVisual();
        UpdateTransport();
    }

    /// <summary>The clock: time, bar.beat and tempo at the playhead (every playback tick).</summary>
    private void UpdateTransport()
    {
        if (doc is null) return;
        double t = Roll.Playhead;
        TimeText.Text = TimeSpan.FromMilliseconds(Math.Max(0, t)).ToString(@"m\:ss\.fff");
        var (bar, beat) = doc.Tempo.BarBeatAt(t);
        BarText.Text = $"{bar}.{beat}";
        BpmText.Text = $"{60000 / doc.Tempo.SegmentAtTime(t).BeatLength:0.##}";
    }

    /// <summary>Everything else that changes with edits, the selection or a status message.</summary>
    private void UpdateTexts()
    {
        if (doc is null) return;
        UpdateTransport();
        SelectionText.Text = Roll.SelectionText();
        StatusText.Text = status;
        ToolTip.SetTip(StatusText, status);
        UndoButton.IsEnabled = doc.Session.CanUndo;
        RedoButton.IsEnabled = doc.Session.CanRedo;
        ToolTip.SetTip(UndoButton, doc.Session.UndoLabel is { } u ? $"Undo {u} (Ctrl+Z)" : "Undo (Ctrl+Z)");
        ToolTip.SetTip(RedoButton, doc.Session.RedoLabel is { } r ? $"Redo {r} (Ctrl+Y)" : "Redo (Ctrl+Y)");
    }

    /// <summary>Keys while the key editor shows. Returns false for keys the window should handle (saving, help…).</summary>
    public bool HandleKey(KeyEventArgs e)
    {
        if (doc is null) return false;
        var mods = e.KeyModifiers;
        bool ctrl = mods.HasFlag(KeyModifiers.Control), shift = mods.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl: doc.Session.Redo(); return true;
            case Key.Z when ctrl: doc.Session.Undo(); return true;
            case Key.A when ctrl && shift: Roll.Deselect(); return true;
            case Key.A when ctrl: Roll.SelectAll(); return true;
            case Key.Escape: if (!Roll.CancelDrag()) Roll.Deselect(); return true;
            case Key.Delete or Key.Back when mods == KeyModifiers.None: Roll.DeleteSelection(); return true;
            case Key.Up when !ctrl: Roll.Transpose(shift ? 12 : 1); return true;
            case Key.Down when !ctrl: Roll.Transpose(shift ? -12 : -1); return true;
            case Key.Left when ctrl: Roll.Nudge(-1); return true; // Cubase's Nudge Left/Right: one grid step
            case Key.Right when ctrl: Roll.Nudge(1); return true;
            case Key.Left: Roll.SelectNeighbour(-1, extend: shift); return true; // previous / next note; Shift adds it
            case Key.Right: Roll.SelectNeighbour(1, extend: shift); return true;
            case Key.D when ctrl && !shift: Roll.Duplicate(); return true;
            case Key.C when ctrl && !shift: clipboard = Roll.CopySelection(); return true;
            case Key.X when ctrl && !shift: clipboard = Roll.CopySelection(); Roll.DeleteSelection(); return true;
            case Key.V when ctrl && !shift: Roll.Paste(clipboard, Roll.Playhead); return true;
            case Key.Q when mods == KeyModifiers.None: Roll.QuantizeSelection(); return true;
            case Key.Space when mods == KeyModifiers.None: TogglePlay(); return true;
            case Key.Multiply when mods == KeyModifiers.None: ToggleRecord(); return true; // Cubase's Record
            case Key.Home when mods == KeyModifiers.None: Seek(0); return true;
            // Cubase's tool numbers.
            case Key.D1 or Key.NumPad1 when mods == KeyModifiers.None: SetTool(KeyTool.Select); return true;
            case Key.D3 or Key.NumPad3 when mods == KeyModifiers.None: SetTool(KeyTool.Split); return true;
            case Key.D4 or Key.NumPad4 when mods == KeyModifiers.None: SetTool(KeyTool.Glue); return true;
            case Key.D5 or Key.NumPad5 when mods == KeyModifiers.None: SetTool(KeyTool.Erase); return true;
            case Key.D8 or Key.NumPad8 when mods == KeyModifiers.None: SetTool(KeyTool.Draw); return true;
            case Key.J when mods == KeyModifiers.None: SnapToggle.IsChecked = SnapToggle.IsChecked != true; return true;
            case Key.H when mods == KeyModifiers.None: Roll.ZoomTime(1.25); return true;
            case Key.G when mods == KeyModifiers.None: Roll.ZoomTime(0.8); return true;
            default: return false;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// Live hitsounding, Hitsound mode's counterpart of Keysound's musical typing and MIDI recording. The computer keyboard
/// plays one lane per key (A S D F G H J K L ; ' then Q W E R T Y U I O P [ ] then Z X C V B N M , . /, in lane order;
/// Up and Down shift which lane A plays). A MIDI keyboard or pad plays lanes too: the drum notes (the Drums VST's
/// notes and the drum map's) first, then the other lanes from middle C up. Record (Numpad *) takes the taps from the
/// playhead: they snap to the grid, and the hits of the tapped lanes in the recorded range are replaced, as one undo
/// step. While it's on, every other key is ignored except Space (play / stop), Numpad * (record) and Esc (close), so
/// editor shortcuts can't fire by accident. Keys are read by their position, so other keyboard layouts get the same shape.
/// </summary>
public partial class MainWindow
{
    private static readonly PhysicalKey[] LiveKeys =
    [
        PhysicalKey.A, PhysicalKey.S, PhysicalKey.D, PhysicalKey.F, PhysicalKey.G, PhysicalKey.H, PhysicalKey.J,
        PhysicalKey.K, PhysicalKey.L, PhysicalKey.Semicolon, PhysicalKey.Quote,
        PhysicalKey.Q, PhysicalKey.W, PhysicalKey.E, PhysicalKey.R, PhysicalKey.T, PhysicalKey.Y, PhysicalKey.U,
        PhysicalKey.I, PhysicalKey.O, PhysicalKey.P, PhysicalKey.BracketLeft, PhysicalKey.BracketRight,
        PhysicalKey.Z, PhysicalKey.X, PhysicalKey.C, PhysicalKey.V, PhysicalKey.B, PhysicalKey.N, PhysicalKey.M,
        PhysicalKey.Comma, PhysicalKey.Period, PhysicalKey.Slash,
    ];

    private static readonly string[] LiveKeyNames =
        ["A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]",
         "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/"];

    private const string LiveAllInputs = "All MIDI inputs", LiveNoInput = "No MIDI input";
    private const int LiveFirstMidiNote = 60; // middle C: lanes without a drum note start here

    private readonly MidiKeyboard liveKeyboard = new();
    private readonly HashSet<PhysicalKey> liveHeld = []; // a held key repeats its press: only the first one plays
    private int liveFirstLane; // the lane A plays, as an index into the lane list
    private HitRecorder? hitRecorder;

    /// <summary>Whether live hitsounding is on: then the keyboard plays lanes instead of running shortcuts.</summary>
    private bool LiveOn => LiveToggle.IsChecked == true;

    private bool IsRecordingHits => hitRecorder is not null;

    private void SetupLiveHits()
    {
        LiveToggle.IsCheckedChanged += (_, _) => ShowLive();
        LiveCloseButton.Click += (_, _) => LiveToggle.IsChecked = false;
        LiveMidiRescanButton.Click += (_, _) => RefreshLiveMidiInputs();
        LiveMidiBox.SelectionChanged += (_, _) => { if (LiveOn) OpenLiveMidiInputs(); };
        HsRecordButton.Click += (_, _) => ToggleHitRecording();
        LiveRecordButton.Click += (_, _) => ToggleHitRecording();
        liveKeyboard.Received += OnLiveMidi;
        LiveHelp.Text = "Up / Down  shift the lanes    Space  play / stop    Numpad *  record    Esc  close. "
            + "Other shortcuts are off while this is open. Recorded taps snap to the grid (as played with Snap off).";
        Closed += (_, _) => liveKeyboard.Dispose();
    }

    /// <summary>Turns live hitsounding on or off (Alt+K, or the ribbon's Live button).</summary>
    private void ToggleLive() => LiveToggle.IsChecked = !LiveOn;

    private void ShowLive()
    {
        liveHeld.Clear();
        LivePanel.IsVisible = LiveOn;
        if (LiveOn) RefreshLiveMidiInputs();
        else
        {
            if (IsRecordingHits) StopHitRecording();
            liveKeyboard.Stop(); // the MIDI input is free again for Keysound mode or another program
        }
        RefreshLivePanel();
    }

    /// <summary>The lanes in the order the lane list shows them: what the keys play.</summary>
    private IReadOnlyList<Lane> LiveLanes() => model?.LaneRows.Select(r => r.Lane).ToList() ?? [];

    private void RefreshLivePanel()
    {
        if (!LiveOn) return;
        var lanes = LiveLanes();
        liveFirstLane = Math.Clamp(liveFirstLane, 0, Math.Max(0, lanes.Count - 1));
        LiveTitle.Text = lanes.Count == 0 ? "Live hitsounding · open a map to play its lanes"
            : $"Live hitsounding · lanes {liveFirstLane + 1}–{Math.Min(lanes.Count, liveFirstLane + LiveKeys.Length)} of {lanes.Count}"
              + (IsRecordingHits ? " · recording" : "");
        // Each key as a key chip beside the lane it plays, as the key hints elsewhere show keys.
        LiveKeysPanel.Children.Clear();
        foreach (var (lane, i) in lanes.Skip(liveFirstLane).Take(LiveKeys.Length).Select((lane, i) => (lane, i)))
        {
            var chip = new Border { Child = new TextBlock { Text = LiveKeyNames[i] } };
            chip.Classes.Add("keyChip");
            var pair = new StackPanel { Margin = new Avalonia.Thickness(0, 2, 12, 2) };
            pair.Classes.Add("keyHint");
            pair.Children.Add(chip);
            pair.Children.Add(new TextBlock { Text = lane.Name });
            LiveKeysPanel.Children.Add(pair);
        }
        var notes = LiveMidiLanes().OrderBy(kv => kv.Key).Select(kv => $"{NoteEditor.PitchName(kv.Key)} ({kv.Key}) {kv.Value.Name}");
        ToolTip.SetTip(LiveKeysPanel, "MIDI notes:\n" + string.Join("\n", notes));
    }

    /// <summary>
    /// Which lane each MIDI note plays: the drum notes first (the Drums VST's defaults and the drum map's, so pads hit
    /// what they hit there), then each other lane, in lane order, on the next free note from middle C up.
    /// </summary>
    private Dictionary<int, Lane> LiveMidiLanes()
    {
        var byNote = new Dictionary<int, Lane>();
        if (model is null) return byNote;
        foreach (var (lane, note) in DrumVstNotes(model)) byNote.TryAdd(note, lane);
        var mapped = byNote.Values.ToHashSet();
        int next = LiveFirstMidiNote;
        foreach (var lane in LiveLanes().Where(l => !mapped.Contains(l)))
        {
            while (byNote.ContainsKey(next)) next++;
            if (next > 127) break;
            byNote[next++] = lane;
        }
        return byNote;
    }

    private void RefreshLiveMidiInputs()
    {
        var chosen = LiveMidiBox.SelectedItem as string;
        var names = MidiKeyboard.DeviceNames();
        LiveMidiBox.ItemsSource = names.Count == 0 ? [LiveNoInput] : (string[])[LiveAllInputs, .. names];
        LiveMidiBox.SelectedItem = chosen is not null && names.Contains(chosen) ? chosen : names.Count == 0 ? LiveNoInput : LiveAllInputs;
        OpenLiveMidiInputs();
    }

    private void OpenLiveMidiInputs()
    {
        if (LiveMidiBox.SelectedItem is not string choice || choice == LiveNoInput)
        {
            liveKeyboard.Stop();
            ToolTip.SetTip(LiveMidiBox, "No MIDI keyboard found. Connect one and press ⟳. The computer keys still play the lanes.");
            return;
        }

        var open = liveKeyboard.Start(choice == LiveAllInputs ? null : choice);
        ToolTip.SetTip(LiveMidiBox, open.Count == 0
            ? "The MIDI input couldn't be opened (another program may be using it)."
            : $"Listening on: {string.Join(", ", open)} (all channels). Hover the key list for which note plays which lane.");
    }

    // On the driver's thread: the time it arrived is what was heard then; the rest goes to the UI thread.
    private void OnLiveMidi(KeyboardEvent e)
    {
        if (e.Kind != KeyboardEventKind.NoteOn || e.Value <= 0) return;
        double? at = audio is { IsPlaying: true } engine ? engine.Position : null;
        Dispatcher.UIThread.Post(() =>
        {
            if (LiveMidiLanes().TryGetValue(e.Pitch, out var lane)) LiveTap(lane, e.Value / 127f, at);
        });
    }

    /// <summary>A key press while live hitsounding is on. Returns true when it was used (every key but Space is).</summary>
    private bool LiveKeyDown(KeyEventArgs e)
    {
        if (!LiveOn || Start.IsVisible || Keys.Document is not null || FocusManager?.GetFocusedElement() is TextBox) return false;
        if (e.Key == Key.F4 && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return false; // closing the window still works
        if (e.Key == Key.Escape) { LiveToggle.IsChecked = false; return true; }
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None) { RunKey("play"); return true; }
        if (EditorCommands.Match(e)?.Id is "ks.typing") { ToggleLive(); return true; }
        if (EditorCommands.Match(e)?.Id is "ks.record") { ToggleHitRecording(); return true; }
        if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return true; // no Ctrl/Alt shortcuts either
        switch (e.Key)
        {
            case Key.Up: ShiftLiveLanes(-1); return true;
            case Key.Down: ShiftLiveLanes(1); return true;
        }

        int index = Array.IndexOf(LiveKeys, e.PhysicalKey);
        var lanes = LiveLanes();
        if (index >= 0 && liveHeld.Add(e.PhysicalKey) && liveFirstLane + index < lanes.Count)
            LiveTap(lanes[liveFirstLane + index], 1, audio is { IsPlaying: true } engine ? engine.Position : null);
        return true;
    }

    private bool LiveKeyUp(KeyEventArgs e)
    {
        if (!LiveOn || Start.IsVisible || Keys.Document is not null || FocusManager?.GetFocusedElement() is TextBox) return false;
        liveHeld.Remove(e.PhysicalKey);
        return true;
    }

    private void ShiftLiveLanes(int by)
    {
        liveFirstLane = Math.Max(0, liveFirstLane + by);
        RefreshLivePanel();
    }

    /// <summary>Plays a lane now, as its name in the lane list does, and records the tap while recording.</summary>
    private void LiveTap(Lane lane, float volume, double? at)
    {
        if (audio is null || model is null) return;
        try
        {
            if (drumVstEnabled && (drumKit is not null || drumVst is not null && drumVstReady) || doc is null) audio.Preview(lane, volume);
            else audio.Preview(doc.LanePreviewAt(lane, at ?? Lanes.Playhead), doc.Folder, volume);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            flash = $"Couldn't play {lane.Name}: {ex.Message}";
            UpdateStatus();
        }

        if (hitRecorder is { } r && at is { } time) r.Tap(time, lane);
    }

    private void ToggleHitRecording()
    {
        if (IsRecordingHits) StopHitRecording();
        else StartHitRecording();
        ShowRecordingHits();
    }

    private void ShowRecordingHits() => HsRecordButton.IsChecked = LiveRecordButton.IsChecked = IsRecordingHits;

    /// <summary>
    /// Records taps from the playhead. With Count-in, playback starts one bar earlier (less near the song's start, which
    /// playback can't go before), so the song leads into the take.
    /// </summary>
    private void StartHitRecording()
    {
        if (audio is null || model is null || session is null) { flash = "Recording needs an open map and an audio output."; UpdateStatus(); return; }
        if (session.IsGestureActive) return;
        if (!LiveOn) LiveToggle.IsChecked = true; // the keys play lanes while recording

        double start = Math.Max(0, audio.IsPlaying ? audio.Position : Lanes.Playhead);
        double bar = model.RedLineAt(start) is { } red ? red.BeatLength * red.Meter : 0;
        double preroll = HsCountInToggle.IsChecked == true ? Math.Max(0, start - bar) : start;

        if (audio.IsPlaying) audio.Pause();
        hitRecorder = new HitRecorder(start);
        audio.Loop = null; // a take is one pass: no cycling while recording
        audio.Seek(preroll);
        playStartedAt = start;
        audio.Play();
        ShowPlaying(true);
        ShowRecordingHits();
        RefreshLivePanel();
        flash = "Recording… tap the lanes; Space or Record stops. Hits of the lanes you tap are replaced from here."
            + (liveKeyboard.Open.Count == 0 ? " (No MIDI keyboard is open: the computer keys play the lanes.)" : "");
        UpdateStatus();
    }

    /// <summary>Ends the take where playback is, and puts it into the lanes as one undo step.</summary>
    private void StopHitRecording()
    {
        if (hitRecorder is not { } r) return;
        double stop = audio?.Position ?? Lanes.Playhead;
        hitRecorder = null;
        ShowRecordingHits();
        if (audio?.IsPlaying == true) audio.Pause();
        UpdateLoop();
        ShowPlaying(false);
        RefreshLivePanel();
        if (session is null || model is null) return;

        var take = r.Finish(stop);
        var edits = Lanes.Edits;
        var hits = HitRecording.Apply(session, take, t => edits.SnapOn ? model.Snap(t, Lanes.Spacing) : t);
        int lanes = hits.Select(h => h.Lane).Distinct().Count();
        flash = hits.Count == 0 ? "Nothing was tapped; the lanes are unchanged."
            : $"Recorded {hits.Count} hit{(hits.Count == 1 ? "" : "s")} in {lanes} lane{(lanes == 1 ? "" : "s")}. Undo removes the whole take.";
        UpdateStatus();
    }
}

using Avalonia.Controls;
using Avalonia.Threading;
using OsuHitsound.Audio;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

/// <summary>
/// VST3 instruments on tracks: each runs in its own host process (<see cref="IsolatedVst3"/>), so a plug-in that
/// crashes only silences its track. A track's plug-in settings are kept in the project and captured on Save.
/// </summary>
public partial class KeysoundEditor
{
    private enum PartStatus { Loading, Ready, Missing, Crashed, Failed }

    /// <summary>A track's loaded plug-in. Kept until the project closes, so Undo of an instrument change or a deleted
    /// track gets the same instance back (with its unsaved tweaks) instead of reloading it.</summary>
    private sealed class VstPart(Guid track, KeyInstrument instrument)
    {
        public Guid Track { get; } = track;
        public KeyInstrument Instrument { get; } = instrument;
        public IsolatedVst3? Vst { get; set; }
        public PartStatus Status { get; set; } = PartStatus.Loading;
        public string? Error { get; set; }

        // The same plug-in: its saved state changes on every Save without reloading it.
        public bool Matches(KeyInstrument i) => Instrument.SamePlugin(i);
    }

    private readonly List<VstPart> vstParts = [];
    // What the MIDI keyboard plays (read on the driver's thread): the selected track's plug-in, or the piano.
    private volatile bool liveToVst;
    private volatile IsolatedVst3? liveVst;

    private void SetupInstruments()
    {
        InstrumentButton.Click += async (_, _) => await ChooseInstrument();
        EditorButton.Click += async (_, _) => await OpenEditor();
        FixButton.Click += async (_, _) =>
        {
            if (SelectedTrackPart() is { Status: PartStatus.Missing }) await LocateInstrument();
            else ReloadInstrument();
        };
    }

    private VstPart? PartFor(KeyTrack track) => track.Instrument.IsVst3 ? vstParts.FirstOrDefault(p => p.Track == track.Id && p.Matches(track.Instrument)) : null;

    private KeyTrack? SelectedKeyTrack() => doc?.SelectedTrack is { } id ? doc.Session.State.Track(id) : null;

    private VstPart? SelectedTrackPart() => SelectedKeyTrack() is { } t ? PartFor(t) : null;

    // The instruments and selection SyncInstruments last saw: a note drag changes the state on every mouse move.
    private KeyInstrument[] syncedInstruments = [];
    private Guid? syncedSelection;

    /// <summary>Starts a plug-in for each VST3 track that has none (an open, a new instrument, an Undo).</summary>
    private void SyncInstruments(bool force = false)
    {
        if (doc is null) return;
        var tracks = doc.Session.State.Tracks;
        if (!force && syncedSelection == doc.SelectedTrack && tracks.Count == syncedInstruments.Length
            && tracks.Select((t, i) => ReferenceEquals(t.Instrument, syncedInstruments[i])).All(same => same)) return;
        syncedInstruments = [.. tracks.Select(t => t.Instrument)];
        syncedSelection = doc.SelectedTrack;
        foreach (var track in doc.Session.State.Tracks)
        {
            if (!track.Instrument.IsVst3 || PartFor(track) is not null) continue;
            var part = new VstPart(track.Id, track.Instrument);
            vstParts.Add(part);
            _ = StartPart(part);
        }

        ApplyParts();
        UpdateLiveTarget();
    }

    private async Task StartPart(VstPart part)
    {
        var opened = doc;
        string path = part.Instrument.Path ?? "";
        if (!IsolatedVst3.IsLoadablePath(path))
        {
            part.Status = PartStatus.Failed;
            part.Error = "only a .vst3 on this computer is loaded; choose the instrument again";
            InstrumentsChanged();
            return;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            part.Status = PartStatus.Missing;
            InstrumentsChanged();
            return;
        }

        try
        {
            var vst = await IsolatedVst3.StartAsync(path, part.Instrument.ClassId, part.Instrument.State);
            if (!ReferenceEquals(doc, opened) || !vstParts.Contains(part))
            {
                Release(vst); // closed or reloaded meanwhile
                return;
            }

            part.Vst = vst;
            part.Status = PartStatus.Ready;
            vst.Crashed += () => Dispatcher.UIThread.Post(() => OnCrashed(part, vst));
            if (vst.IsCrashed) OnCrashed(part, vst);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (!ReferenceEquals(doc, opened)) return;
            part.Status = PartStatus.Failed;
            part.Error = ex.Message;
            SetStatus($"{part.Instrument.DisplayName} couldn't load: {ex.Message}");
        }

        InstrumentsChanged();
    }

    /// <summary>Takes a part's plug-in out of the mixer, then closes its host off the UI thread.</summary>
    private void Release(VstPart part)
    {
        if (part.Vst is not { } vst) return;
        part.Vst = null;
        ApplyParts(); // the mixer lets go of it before it is disposed
        Release(vst);
    }

    private static void Release(IsolatedVst3 vst) => _ = Task.Run(vst.Dispose);

    private void OnCrashed(VstPart part, IsolatedVst3 vst)
    {
        if (part.Vst != vst) return;
        Release(part);
        part.Status = PartStatus.Crashed;
        SetStatus($"{part.Instrument.DisplayName} stopped working, so its track is silent. Reload instrument tries again with the settings last saved in the project.");
        InstrumentsChanged();
    }

    private void InstrumentsChanged()
    {
        if (doc is null) return;
        ApplyParts();
        SchedulePlayback(); // several plug-ins finishing together rebuild playback once
        RefreshTracks();
        UpdateLiveTarget();
    }

    /// <summary>Closes every plug-in (the project closed). Their hosts quit off the UI thread.</summary>
    private void DisposeInstruments()
    {
        audio?.SetLiveParts([]);
        applied = (null, []);
        syncedInstruments = [];
        liveVst = null;
        liveToVst = false;
        foreach (var vst in vstParts.Select(p => p.Vst).OfType<IsolatedVst3>()) Release(vst);
        vstParts.Clear();
    }

    private (AudioEngine? Engine, ILivePart[] Parts) applied = (null, []);

    /// <summary>What the mixer plays alongside the song: the bundled piano and each VST3 track's loaded plug-in.</summary>
    private void ApplyParts()
    {
        if (audio is null) return;
        var parts = new List<ILivePart>();
        if (player is not null) parts.Add(player);
        if (doc is not null)
            parts.AddRange(doc.Session.State.Tracks.Select(PartFor).Select(p => p?.Vst).OfType<IsolatedVst3>());
        if (applied.Engine == audio && applied.Parts.SequenceEqual(parts)) return; // every edit lands here
        applied = (audio, [.. parts]);
        audio.SetLiveParts(parts);
    }

    private void UpdateLiveTarget()
    {
        var track = SelectedKeyTrack();
        var next = track is null ? null : PartFor(track)?.Vst;
        if (liveVst is { } previous && previous != next) previous.Live(KeysoundMidi.ControlChange, KeysoundMidi.AllNotesOff, 0); // no key stays stuck
        liveVst = next;
        liveToVst = track?.Instrument.IsVst3 == true; // a plug-in still loading or missing stays silent, not the piano
    }

    /// <summary>A note-name tap in the key editor: the selected track's instrument plays it briefly.</summary>
    private void Audition(int pitch, int velocity)
    {
        if (!liveToVst)
        {
            player?.Audition(pitch, velocity);
            return;
        }

        if (liveVst is not { } vst) return;
        vst.Live(KeysoundMidi.NoteOn, (byte)pitch, (byte)velocity);
        DispatcherTimer.RunOnce(() => vst.Live(KeysoundMidi.NoteOff, (byte)pitch, 0), TimeSpan.FromMilliseconds(400));
    }

    /// <summary>A keyboard message for the selected track's plug-in. True when it went there (not to the piano).</summary>
    private bool LiveToVst(KeyboardEvent e)
    {
        if (!liveToVst) return false;
        if (liveVst is not { } vst) return true;
        switch (e.Kind)
        {
            case KeyboardEventKind.NoteOn: vst.Live(KeysoundMidi.NoteOn, (byte)e.Pitch, (byte)e.Value); break;
            case KeyboardEventKind.NoteOff: vst.Live(KeysoundMidi.NoteOff, (byte)e.Pitch, 0); break;
            case KeyboardEventKind.Controller:
                var (status, data1, data2) = KeysoundMidi.Controller(e.Controller, e.Value);
                vst.Live(status, data1, data2);
                break;
        }

        return true;
    }

    // Track instrument commands.

    private async Task ChooseInstrument()
    {
        if (SelectedKeyTrack() is not { } track || TopLevel.GetTopLevel(this) is not Window owner || doc is not { } project) return;
        var id = track.Id;
        const string piano = "Bundled piano", browse = "Browse for another VST3 file…";
        var installed = await Task.Run(Vst3Picker.Installed);
        string? current = track.Instrument.IsVst3 ? installed.FirstOrDefault(p => string.Equals(p.Value, track.Instrument.Path, StringComparison.OrdinalIgnoreCase)).Key : piano;
        var selected = await Dialogs.Choose(owner, "Track instrument", $"Instrument for {track.Name} (its notes stay as they are):",
            [piano, .. installed.Keys, browse], current, accept: "Use");
        if (selected is null || !ReferenceEquals(doc, project)) return;

        KeyInstrument? chosen;
        if (selected == piano) chosen = KeyInstrument.Piano;
        else
        {
            string? path = selected == browse ? await Vst3Picker.Browse(owner) : installed.GetValueOrDefault(selected);
            if (path is null) return;
            chosen = await PickClass(owner, path, keep: null);
        }

        if (chosen is null || !ReferenceEquals(doc, project) || project.Session.State.Track(id) is not { } now) return;
        if (now.Instrument.SamePlugin(chosen)) return; // keeps its settings
        project.Session.ReplaceInstrument(id, chosen, "Change instrument");
        SetStatus($"{now.Name} now plays {chosen.DisplayName}. Undo puts back {now.Instrument.DisplayName}.");
    }

    /// <summary>A missing plug-in: point at where it is now. Its saved settings are kept.</summary>
    private async Task LocateInstrument()
    {
        if (SelectedKeyTrack() is not { Instrument.IsVst3: true } track || TopLevel.GetTopLevel(this) is not Window owner || doc is not { } project) return;
        var id = track.Id;
        if (await Vst3Picker.Browse(owner, $"Locate {track.Instrument.DisplayName}") is not { } path) return;
        if (await PickClass(owner, path, keep: track.Instrument) is not { } found || !ReferenceEquals(doc, project)) return;
        project.Session.ReplaceInstrument(id, found, "Locate instrument");
    }

    /// <summary>
    /// Reads the instrument classes in a .vst3 (in a short-lived host) and picks one: the only one, the one
    /// <paramref name="keep"/> used, or the user's choice. With <paramref name="keep"/>, the result keeps its settings.
    /// </summary>
    private async Task<KeyInstrument?> PickClass(Window owner, string path, KeyInstrument? keep)
    {
        string file = Path.GetFileName(path);
        SetStatus($"Reading {file}…");
        var classes = await IsolatedVst3.ScanAsync(path);
        if (classes.Count == 0)
        {
            SetStatus(File.Exists(IsolatedVst3.DefaultHostPath)
                ? $"{file} has no VST3 instrument dawsu! can load."
                : "The VST3 host (dawsu_vst3_host.exe) isn't installed next to dawsu!.");
            return null;
        }

        var pick = classes.FirstOrDefault(c => c.Id == keep?.ClassId);
        if (pick.Id is null && classes.Count == 1) pick = classes[0];
        if (pick.Id is null)
        {
            var labels = classes.Select(c => classes.Count(o => o.Name == c.Name) > 1 ? $"{c.Name} ({c.Vendor}, {c.Id[..Math.Min(8, c.Id.Length)]})" : c.Name).ToList();
            if (await Dialogs.Choose(owner, "Choose instrument", $"{file} has several instruments:", labels, accept: "Use") is not { } label) return null;
            pick = classes[labels.IndexOf(label)];
        }

        SetStatus("");
        var instrument = KeyInstrument.Vst3Plugin(path, pick.Id, pick.Name);
        return keep is null ? instrument : instrument with { State = keep.State };
    }

    private void ReloadInstrument()
    {
        if (SelectedTrackPart() is not { } part) return;
        vstParts.Remove(part);
        Release(part);
        SetStatus($"Reloading {part.Instrument.DisplayName}…");
        SyncInstruments(force: true); // a new host, with the track's last saved settings
        RefreshTracks();
    }

    private async Task OpenEditor()
    {
        if (SelectedTrackPart() is not { Vst: { } vst } part) return;
        try { await vst.ShowEditorAsync(); }
        catch (InvalidOperationException ex) { SetStatus($"{part.Instrument.DisplayName}'s window couldn't open: {ex.Message}"); }
    }

    /// <summary>
    /// Before Save: asks each loaded plug-in for its current settings and keeps them in the project. Returns the tracks
    /// whose plug-in didn't answer (their last saved settings stay).
    /// </summary>
    public async Task<IReadOnlyList<string>> CaptureInstrumentStates()
    {
        if (doc is not { } project) return [];
        // All at once: each plug-in answers from its own process.
        var asked = project.Session.State.Tracks.Select(t => (Track: t, PartFor(t)?.Vst)).Where(p => p.Vst is not null)
            .Select(async p =>
            {
                try { return (p.Track, State: await p.Vst!.GetStateAsync()); }
                catch (InvalidOperationException) { return (p.Track, State: (string?)null); }
            });
        var answers = await Task.WhenAll(asked);
        if (ReferenceEquals(doc, project))
            project.Session.StoreInstrumentStates(answers.Where(a => a.State is not null).ToDictionary(a => a.Track.Id, a => a.State!));
        return [.. answers.Where(a => a.State is null).Select(a => a.Track.Name)];
    }

    /// <summary>The track list's word for a plug-in that isn't playing; null when it plays (or for the piano).</summary>
    private string? InstrumentProblem(KeyTrack track) => PartFor(track)?.Status switch
    {
        PartStatus.Loading => "loading",
        PartStatus.Missing => "missing",
        PartStatus.Crashed => "crashed",
        PartStatus.Failed => "failed to load",
        _ => null,
    };

    private void UpdateInstrumentControls(KeyTrack? current)
    {
        var part = current is null ? null : PartFor(current);
        InstrumentText.Text = current is null ? "No track. Add one to draw notes." : $"Instrument: {current.Instrument.DisplayName}" + part?.Status switch
        {
            PartStatus.Loading => " (loading…)",
            PartStatus.Missing => $" — not found at {current.Instrument.Path}. Its settings are kept; locate it or choose another instrument.",
            PartStatus.Crashed => " — stopped working. Reload tries again with the settings last saved in the project.",
            PartStatus.Failed => $" — couldn't load: {part.Error}",
            _ => "",
        };
        InstrumentButton.IsEnabled = current is not null;
        showingVolume = true;
        VolumeBox.IsEnabled = current is not null;
        VolumeBox.Value = (decimal)(current?.VolumeDb ?? 0);
        showingVolume = false;
        EditorButton.IsVisible = current?.Instrument.IsVst3 == true;
        EditorButton.IsEnabled = part?.Vst is not null;
        FixButton.IsVisible = part?.Status is PartStatus.Missing or PartStatus.Crashed or PartStatus.Failed;
        FixButton.Content = part?.Status == PartStatus.Missing ? "Locate…" : "Reload instrument";
    }
}

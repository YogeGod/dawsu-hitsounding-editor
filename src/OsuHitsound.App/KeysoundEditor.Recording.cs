using Avalonia.Controls;
using Avalonia.Threading;
using OsuHitsound.Audio;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

/// <summary>
/// Keysound mode's MIDI keyboard: live playing through the piano, and replacement recording into the selected track
/// from the playhead, with a metronome and a one-bar count-in (Cubase's Record, Numpad *).
/// </summary>
public partial class KeysoundEditor
{
    private const string AllInputs = "All MIDI inputs", NoInput = "No MIDI input";
    private readonly MidiKeyboard keyboard = new();
    private KeyRecorder? recorder;
    private Guid recordTrack;

    public bool IsRecording => recorder is not null;

    private void SetupRecording()
    {
        keyboard.Received += OnKeyboard;
        RecordButton.Click += (_, _) => ToggleRecord();
        MidiRescanButton.Click += (_, _) => RefreshMidiInputs();
        MidiInputBox.SelectionChanged += (_, _) => { if (doc is not null) OpenMidiInputs(); }; // inputs open only while a project is
    }

    /// <summary>Lists the MIDI inputs again (a keyboard plugged in later) and opens the chosen one, or all.</summary>
    private void RefreshMidiInputs()
    {
        var chosen = MidiInputBox.SelectedItem as string;
        var names = MidiKeyboard.DeviceNames();
        MidiInputBox.ItemsSource = names.Count == 0 ? [NoInput] : (string[])[AllInputs, .. names];
        MidiInputBox.SelectedItem = chosen is not null && names.Contains(chosen) ? chosen : names.Count == 0 ? NoInput : AllInputs;
        OpenMidiInputs();
    }

    private void OpenMidiInputs()
    {
        if (MidiInputBox.SelectedItem is not string choice || choice == NoInput)
        {
            keyboard.Stop();
            ToolTip.SetTip(MidiInputBox, "No MIDI keyboard found. Connect one and press ⟳.");
            return;
        }

        var open = keyboard.Start(choice == AllInputs ? null : choice);
        ToolTip.SetTip(MidiInputBox, open.Count == 0
            ? "The MIDI input couldn't be opened (another program may be using it)."
            : $"Listening on: {string.Join(", ", open)} (all channels).");
    }

    // On the driver's thread: the piano hears it at once; recording bookkeeping goes to the UI thread with the time it
    // arrived, which already accounts for the output latency (what was heard then).
    private void OnKeyboard(KeyboardEvent e)
    {
        var p = player;
        switch (e.Kind)
        {
            case KeyboardEventKind.NoteOn: p?.LiveNoteOn(e.Pitch, e.Value); break;
            case KeyboardEventKind.NoteOff: p?.LiveNoteOff(e.Pitch); break;
            case KeyboardEventKind.Controller when e.Controller == ControllerKind.Sustain: p?.LiveSustain(e.Value >= KeyControllers.PedalDown); break;
            case KeyboardEventKind.Controller when e.Controller == ControllerKind.PitchBend: p?.LiveBend(e.Value); break;
        }

        if (recorder is null || audio is not { IsPlaying: true } engine) return;
        double at = engine.Position;
        Dispatcher.UIThread.Post(() => Record(e, at));
    }

    private void Record(KeyboardEvent e, double at)
    {
        if (recorder is not { } r) return;
        switch (e.Kind)
        {
            case KeyboardEventKind.NoteOn: r.NoteOn(at, e.Pitch, e.Value); break;
            case KeyboardEventKind.NoteOff: r.NoteOff(at, e.Pitch); break;
            default: r.Controller(at, e.Controller, e.Value); break;
        }
    }

    private void ToggleRecord()
    {
        if (IsRecording) StopRecording();
        else StartRecording();
        RecordButton.IsChecked = IsRecording;
    }

    /// <summary>
    /// Records into the selected track from the playhead. With Count-in, playback starts one bar earlier (less near the
    /// song's start, which playback can't go before) with clicks; the Metronome clicks on through the take.
    /// </summary>
    private void StartRecording()
    {
        if (doc is null || audio is null) { SetStatus("Recording needs an audio output."); return; }
        if (doc.SelectedTrack is not { } trackId || doc.Session.State.Track(trackId) is not { } track) { SetStatus("Select a track to record into."); return; }

        var tempo = doc.Tempo;
        bool countIn = CountInToggle.IsChecked == true, click = MetronomeToggle.IsChecked == true;
        double start = Math.Max(0, Roll.Playhead);
        var segment = tempo.SegmentAtTime(start);
        double preroll = countIn ? Math.Max(0, start - segment.BeatLength * segment.Meter) : start;
        double songEnd = Math.Max(audio.Length, start + 60_000);
        // Beats (level 1) and bars (level 0): clicks during the count-in and, with the metronome on, the take.
        player?.SetClicks(tempo.GridLines(preroll, songEnd, 1)
            .Where(g => g.Level <= 1 && (g.Time < start ? countIn : click))
            .Select(g => (g.Time, g.Level == 0)));

        Stop();
        recorder = new KeyRecorder(tempo, start);
        recordTrack = trackId;
        audio.Seek(preroll);
        audio.Play();
        PlayGlyph.Glyph = "Pause";
        clock.Start();
        SetStatus($"Recording into {track.Name}… Space or Record stops; notes starting from here are replaced."
            + (keyboard.Open.Count == 0 ? " No MIDI keyboard is open: connect one and press ⟳." : ""));
    }

    /// <summary>Ends the take where playback is, and puts it into the track as one undo step.</summary>
    public void StopRecording()
    {
        if (recorder is not { } r || doc is null) return;
        double stop = audio?.Position ?? Roll.Playhead;
        recorder = null;
        RecordButton.IsChecked = false;
        player?.SetClicks(null);
        Stop();
        Roll.RecordingPreview = null;
        Roll.InvalidateVisual();
        var take = r.Finish(stop);
        if (doc.Session.State.Track(recordTrack) is not { } track) return;
        var recorded = KeyRecording.Apply(track, take);
        if (ReferenceEquals(recorded, track)) { SetStatus("Nothing was played; the track is unchanged."); return; }
        doc.Session.ReplaceTrack(recorded, "Record");
        SetStatus($"Recorded {take.Notes.Count} note{(take.Notes.Count == 1 ? "" : "s")} into {track.Name}. Undo removes the whole take.");
    }

    // While recording, the piano roll shows what has been played so far.
    private void ShowRecording(double now)
    {
        if (recorder is { } r && r.StartMs <= now) Roll.RecordingPreview = r.Preview(now);
    }
}

using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.Audio;

/// <summary>Keysound tracks and keyboard controllers as MIDI bytes for a VST3 instrument (channel 1).</summary>
public static class KeysoundMidi
{
    public const byte NoteOn = 0x90, NoteOff = 0x80, ControlChange = 0xB0, PitchBend = 0xE0, AllNotesOff = 123;

    /// <summary>A controller change as a MIDI message: the sustain pedal (CC64), the mod wheel (CC1) or pitch bend (centre 8192).</summary>
    public static (byte Status, byte Data1, byte Data2) Controller(ControllerKind kind, int value) => kind switch
    {
        ControllerKind.Sustain => (ControlChange, 64, (byte)Math.Clamp(value, 0, 127)),
        ControllerKind.Modulation => (ControlChange, 1, (byte)Math.Clamp(value, 0, 127)),
        _ => Bend(Math.Clamp(value + 8192, 0, 16383)),
    };

    private static (byte, byte, byte) Bend(int v) => (PitchBend, (byte)(v & 0x7F), (byte)(v >> 7));

    /// <summary>A track's notes and controllers in timeline ms, and the time of the last one (0 for none). The plug-in reads the pedal itself.</summary>
    public static List<TimedMidi> Events(KeyTrack track, TempoMap tempo, out double last)
    {
        var events = new List<TimedMidi>(track.Notes.Count * 2 + track.Controllers.Count);
        last = 0;
        foreach (var n in track.Notes)
        {
            double end = tempo.TimeAt(n.End);
            events.Add(new TimedMidi(tempo.TimeAt(n.Start), NoteOn, (byte)n.Pitch, (byte)Math.Clamp(n.Velocity, 1, 127))); // velocity 0 would be a note-off
            events.Add(new TimedMidi(end, NoteOff, (byte)n.Pitch, 0));
            last = Math.Max(last, end);
        }

        foreach (var c in track.Controllers)
        {
            var (status, data1, data2) = Controller(c.Kind, c.Value);
            double at = tempo.TimeAt(c.Tick);
            events.Add(new TimedMidi(at, status, data1, data2));
            last = Math.Max(last, at);
        }

        return events;
    }
}

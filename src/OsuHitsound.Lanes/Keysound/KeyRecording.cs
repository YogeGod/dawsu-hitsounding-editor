namespace OsuHitsound.Lanes.Keysound;

/// <summary>
/// Collects one recording pass from a MIDI keyboard: notes and sustain/modulation/pitch-bend changes at the times they
/// were played (timeline ms), kept as played (no quantize). Events before the record start (the count-in) are ignored.
/// </summary>
public sealed class KeyRecorder(TempoMap tempo, double startMs)
{
    private readonly Dictionary<int, (double Start, int Velocity)> held = [];
    private readonly List<KeyNote> notes = [];
    private readonly List<KeyController> controllers = [];

    private readonly Dictionary<ControllerKind, int> lastValue = [];

    public double StartMs => startMs;

    public void NoteOn(double ms, int pitch, int velocity)
    {
        if (ms < startMs || pitch is < KeyNote.MinPitch or > KeyNote.MaxPitch) return;
        if (velocity <= 0) { NoteOff(ms, pitch); return; }
        if (held.ContainsKey(pitch)) NoteOff(ms, pitch); // a repeated key without its note-off
        held[pitch] = (ms, Math.Clamp(velocity, KeyNote.MinVelocity, KeyNote.MaxVelocity));
    }

    public void NoteOff(double ms, int pitch)
    {
        if (!held.Remove(pitch, out var on)) return;
        notes.Add(Note(on.Start, Math.Max(ms, on.Start), pitch, on.Velocity));
    }

    public void Controller(double ms, ControllerKind kind, int value)
    {
        // A wheel sends a stream of messages; only changes are kept.
        if (ms < startMs || lastValue.TryGetValue(kind, out int last) && last == value) return;
        var c = new KeyController(tempo.RoundTick(ms), kind, value);
        if (!c.IsValid) return;
        controllers.Add(c);
        lastValue[kind] = value;
    }

    /// <summary>The notes so far, with held ones running to <paramref name="nowMs"/>: what the piano roll shows while recording.</summary>
    public List<KeyNote> Preview(double nowMs) => [.. notes, .. held.Select(h => Note(h.Value.Start, Math.Max(nowMs, h.Value.Start), h.Key, h.Value.Velocity))];

    /// <summary>Ends the pass at <paramref name="stopMs"/>: held notes end there. Returns the range and what was played.</summary>
    public KeyTake Finish(double stopMs)
    {
        foreach (int pitch in held.Keys.ToList()) NoteOff(stopMs, pitch);
        long start = tempo.RoundTick(startMs), end = Math.Max(start, tempo.RoundTick(stopMs));
        return new KeyTake(start, end, [.. notes.Where(n => n.IsValid)], [.. controllers.OrderBy(c => c.Tick)]);
    }

    private KeyNote Note(double startMs, double endMs, int pitch, int velocity)
    {
        long start = tempo.RoundTick(startMs);
        return new KeyNote(Guid.NewGuid(), start, Math.Max(1, tempo.RoundTick(endMs) - start), pitch, velocity);
    }
}

/// <summary>One recorded pass: its range in ticks [Start, End) and what was played in it.</summary>
public sealed record KeyTake(long Start, long End, IReadOnlyList<KeyNote> Notes, IReadOnlyList<KeyController> Controllers);

/// <summary>Replacement recording: how a take goes into the armed track.</summary>
public static class KeyRecording
{
    /// <summary>
    /// The track after a take:
    /// <list type="bullet">
    /// <item>Notes starting inside [Start, End) are replaced by the take's notes; earlier notes (even ones overlapping
    /// the range) and later notes stay.</item>
    /// <item>Sustain, modulation and pitch-bend changes inside the range are replaced too.</item>
    /// <item>At the range's end, each controller goes back to the value the original track had there, so untouched music
    /// after the take sounds as before.</item>
    /// </list>
    /// A take with nothing played leaves the track unchanged.
    /// </summary>
    public static KeyTrack Apply(KeyTrack track, KeyTake take)
    {
        if (take.Notes.Count == 0 && take.Controllers.Count == 0) return track;
        bool Inside(long tick) => tick >= take.Start && tick < take.End;

        var controllers = track.Controllers.Where(c => !Inside(c.Tick)).Concat(take.Controllers.Where(c => Inside(c.Tick)))
            .OrderBy(c => c.Tick).ThenBy(c => c.Kind).ToList();
        var restore = new List<KeyController>();
        foreach (var kind in Enum.GetValues<ControllerKind>())
        {
            int original = KeyControllers.ValueAt(track.Controllers, kind, take.End);
            int recorded = KeyControllers.ValueAt(controllers, kind, take.End - 1);
            bool changedAtEnd = track.Controllers.Any(c => c.Kind == kind && c.Tick == take.End);
            if (recorded != original && !changedAtEnd) restore.Add(new KeyController(take.End, kind, original));
        }

        return track with
        {
            Notes = [.. track.Notes.Where(n => !Inside(n.Start)), .. take.Notes],
            Controllers = [.. controllers.Concat(restore).OrderBy(c => c.Tick).ThenBy(c => c.Kind)],
        };
    }
}

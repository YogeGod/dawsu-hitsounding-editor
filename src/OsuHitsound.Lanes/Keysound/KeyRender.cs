namespace OsuHitsound.Lanes.Keysound;

/// <summary>A controller value for a rendered note, in ms from the note's start (0: its value when the note starts).</summary>
public readonly record struct KeyRenderController(double Time, ControllerKind Kind, int Value);

/// <summary>
/// One note occurrence to render on its own (Generate): the key is held for <see cref="Length"/> ms and the sustain
/// pedal keeps it sounding until <see cref="Held"/> ms. <see cref="Controllers"/> give the pedal, modulation and pitch bend
/// in force at the start and their changes until <see cref="Held"/>, so each render bakes in the performance.
/// </summary>
public sealed record KeyRenderJob(Guid Track, Guid Note, int Pitch, int Velocity, double Length, double Held, IReadOnlyList<KeyRenderController> Controllers)
{
    /// <summary>What decides the sound of a deterministic instrument: equal keys render equal audio.</summary>
    public string Key => $"{Pitch}/{Velocity}/{Length:R}/{Held:R}/" + string.Join(",", Controllers.Select(c => $"{c.Time:R}:{(int)c.Kind}:{c.Value}"));
}

public static class KeyRender
{
    /// <summary>The render job for a note of a track, timed by the tempo map.</summary>
    public static KeyRenderJob Job(KeyTrack track, KeyNote note, TempoMap tempo)
    {
        var sustain = track.Controllers.Where(c => c.Kind == ControllerKind.Sustain).ToList();
        long heldEnd = sustain.Count > 0 ? KeyControllers.SustainedEnd(sustain, note.End) : note.End;
        double start = tempo.TimeAt(note.Start);
        var controllers = new List<KeyRenderController>();
        foreach (var kind in Enum.GetValues<ControllerKind>())
            controllers.Add(new KeyRenderController(0, kind, KeyControllers.ValueAt(track.Controllers, kind, note.Start)));
        foreach (var c in track.Controllers)
            if (c.Tick > note.Start && c.Tick <= heldEnd)
                controllers.Add(new KeyRenderController(tempo.TimeAt(c.Tick) - start, c.Kind, c.Value));

        return new KeyRenderJob(track.Id, note.Id, note.Pitch, note.Velocity,
            tempo.TimeAt(note.End) - start, tempo.TimeAt(heldEnd) - start, controllers);
    }
}

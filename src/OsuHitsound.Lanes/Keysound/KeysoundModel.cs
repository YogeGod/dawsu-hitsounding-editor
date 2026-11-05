using System.Collections.Immutable;

namespace OsuHitsound.Lanes.Keysound;

/// <summary>A pitched note on an instrument track, placed in ticks (<see cref="TempoMap.Ppq"/> per beat) from the first red line.</summary>
public sealed record KeyNote(Guid Id, long Start, long Length, int Pitch, int Velocity)
{
    public const int MinPitch = 0, MaxPitch = 127, MinVelocity = 1, MaxVelocity = 127;
    public long End => Start + Length;

    public bool IsValid => Id != Guid.Empty && Length > 0 && Pitch is >= MinPitch and <= MaxPitch && Velocity is >= MinVelocity and <= MaxVelocity
        && Start is > -100_000_000 and < 100_000_000 && Length < 100_000_000;
}

/// <summary>
/// What plays a track. M1 knows the bundled piano (not yet audible) and keeps a VST3 path and saved state for later
/// milestones unchanged.
/// </summary>
public sealed record KeyInstrument(string Kind, string? Path = null, string? State = null)
{
    public const string BundledPiano = "piano";
    public static KeyInstrument Piano { get; } = new(BundledPiano);
    public string DisplayName => Kind == BundledPiano ? "Bundled piano" : System.IO.Path.GetFileNameWithoutExtension(Path ?? "VST3 instrument");
}

/// <summary>An instrument track: its notes and whether Generate exports them (mute and solo live in <see cref="KeysoundSession"/>, playback only).</summary>
public sealed record KeyTrack(Guid Id, string Name, KeyInstrument Instrument, ImmutableList<KeyNote> Notes)
{
    public bool IncludeInGenerate { get; init; } = true;

    public static KeyTrack Create(string name) => new(Guid.NewGuid(), name, KeyInstrument.Piano, []);
}

/// <summary>Everything Undo restores: the tracks, in order, with their notes.</summary>
public sealed record KeysoundState(ImmutableList<KeyTrack> Tracks)
{
    public KeyTrack? Track(Guid id) => Tracks.FirstOrDefault(t => t.Id == id);

    public KeysoundState WithTrack(Guid id, Func<KeyTrack, KeyTrack> change)
    {
        int i = Tracks.FindIndex(t => t.Id == id);
        return i < 0 ? this : this with { Tracks = Tracks.SetItem(i, change(Tracks[i])) };
    }
}

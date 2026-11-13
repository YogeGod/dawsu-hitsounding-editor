using System.Collections.Immutable;

namespace OsuHitsound.Lanes.Keysound;

/// <summary>A pitched note on an instrument track, placed in ticks (<see cref="TempoMap.Ppq"/> per beat) from the first red line.</summary>
public sealed record KeyNote(Guid Id, long Start, long Length, int Pitch, int Velocity)
{
    public const int MinPitch = 0, MaxPitch = 127, MinVelocity = 1, MaxVelocity = 127;
    public long End => Start + Length;

    /// <summary>
    /// The gameplay position Sync assigned this note to, or null when it isn't synced. Moving the note's start clears it
    /// (<see cref="KeysoundSession"/>); pitch, length and velocity edits keep it.
    /// </summary>
    public KeySyncTarget? Target { get; init; }

    public bool IsValid => Id != Guid.Empty && Length > 0 && Pitch is >= MinPitch and <= MaxPitch && Velocity is >= MinVelocity and <= MaxVelocity
        && Start is > -100_000_000 and < 100_000_000 && Length < 100_000_000 && Target?.IsValid != false;
}

/// <summary>
/// A sync target: a reference hit object (its place in [HitObjects]) and node (0 = head; for sliders the repeats, then
/// the tail), with the play time and kind it had when Sync chose it, so a changed reference shows as moved or missing.
/// </summary>
public sealed record KeySyncTarget(int ObjectIndex, int Node, double Time, HitPosition Position)
{
    public bool IsValid => ObjectIndex is >= 0 and < 1_000_000 && Node is >= 0 and < 100_000 && double.IsFinite(Time) && Enum.IsDefined(Position);
    public bool SamePlace(KeySyncTarget other) => ObjectIndex == other.ObjectIndex && Node == other.Node;
}

/// <summary>
/// What plays a track. M1 knows the bundled piano (not yet audible) and keeps a VST3 path and saved state for later
/// milestones unchanged.
/// </summary>
public sealed record KeyInstrument(string Kind, string? Path = null, string? State = null)
{
    public const string BundledPiano = "piano", Vst3 = "vst3", SoundFont = "sf2";

    /// <summary>A VST3 instrument: its .vst3, the class in it, and its display name; no saved state yet.</summary>
    public static KeyInstrument Vst3Plugin(string path, string? classId, string name) => new(Vst3, path) { ClassId = classId, Name = name };

    public bool IsVst3 => Kind == Vst3;

    /// <summary>A SoundFont preset: the .sf2 file, its bank (128 = a percussion kit) and program, and the preset's name.</summary>
    public static KeyInstrument SoundFontPreset(string path, int bank, int program, string name, bool effects = true) =>
        new(SoundFont, path) { Bank = bank, Program = program, Name = name, Effects = effects };

    public bool IsSoundFont => Kind == SoundFont;

    /// <summary>A SoundFont preset's bank (0–128).</summary>
    public int Bank { get; init; }

    /// <summary>A SoundFont preset's program (0–127).</summary>
    public int Program { get; init; }

    /// <summary>A SoundFont's own reverb and chorus (on unless switched off for dry keysounds).</summary>
    public bool Effects { get; init; } = true;

    /// <summary>Played by MIDI outside this process's piano: a VST3 plug-in or a SoundFont.</summary>
    public bool IsMidiInstrument => IsVst3 || IsSoundFont;

    /// <summary>The same plug-in (or both the piano), whatever its saved settings: switching between them reloads nothing.</summary>
    public bool SamePlugin(KeyInstrument other) =>
        Kind == other.Kind && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase) && ClassId == other.ClassId
        && Bank == other.Bank && Program == other.Program && Effects == other.Effects;
    /// <summary>The VST3 class id inside the .vst3 (a plug-in can hold several); null for its first instrument.</summary>
    public string? ClassId { get; init; }
    /// <summary>The plug-in's own name, as its scan reported it.</summary>
    public string? Name { get; init; }
    /// <summary>Semitones at full pitch bend: the General MIDI default, which the bundled piano uses.</summary>
    public double PitchBendRange => 2;
    public static KeyInstrument Piano { get; } = new(BundledPiano);
    public string DisplayName => Kind == BundledPiano ? "Bundled piano"
        : IsSoundFont ? $"{Name ?? "SoundFont"} ({System.IO.Path.GetFileName(Path)})"
        : Name ?? System.IO.Path.GetFileNameWithoutExtension(Path ?? "VST3 instrument");
}

public enum ControllerKind { Sustain, Modulation, PitchBend }

/// <summary>
/// A controller change on a track, in ticks: sustain and modulation 0–127, pitch bend −8192–8191 (0 is centre,
/// ±8192 is the instrument's bend range, ±2 semitones for the bundled piano).
/// </summary>
public sealed record KeyController(long Tick, ControllerKind Kind, int Value)
{
    public bool IsValid => Enum.IsDefined(Kind) && Tick is > -100_000_000 and < 100_000_000
        && (Kind == ControllerKind.PitchBend ? Value is >= -8192 and <= 8191 : Value is >= 0 and <= 127);
}

/// <summary>Rules for a track's controllers, which are kept sorted by tick.</summary>
public static class KeyControllers
{
    /// <summary>The sustain pedal is down at 64 and above (MIDI CC 64).</summary>
    public const int PedalDown = 64;

    /// <summary>
    /// A controller's value at a tick: its last change at or before it, else the resting value (pedal up, no
    /// modulation, bend centred).
    /// </summary>
    public static int ValueAt(IReadOnlyList<KeyController> controllers, ControllerKind kind, long tick)
    {
        // Sorted by tick: from the last change at or before the tick, back to the last one of this kind.
        for (int i = Sorted.LowerBound(controllers, tick + 0.5, c => c.Tick) - 1; i >= 0; i--)
            if (controllers[i].Kind == kind) return controllers[i].Value;
        return 0;
    }

    /// <summary>
    /// When the sustain pedal keeps a note sounding: its end, or the next pedal release after it when the pedal is down
    /// then. A pedal never released doesn't hold the note forever. <paramref name="sustain"/> is sorted by tick.
    /// </summary>
    public static long SustainedEnd(IReadOnlyList<KeyController> sustain, long end)
    {
        int i = Sorted.LowerBound(sustain, end + 0.5, c => c.Tick) - 1; // the last change at or before the end
        if (i < 0 || sustain[i].Value < PedalDown) return end;
        for (int j = i + 1; j < sustain.Count; j++)
            if (sustain[j].Value < PedalDown) return sustain[j].Tick;
        return end;
    }
}

/// <summary>An instrument track: its notes and whether Generate exports them (mute and solo live in <see cref="KeysoundSession"/>, playback only).</summary>
public sealed record KeyTrack(Guid Id, string Name, KeyInstrument Instrument, ImmutableList<KeyNote> Notes)
{
    public bool IncludeInGenerate { get; init; } = true;
    /// <summary>Sustain, modulation and pitch-bend changes, by tick (imported from MIDI; editing lanes come later).</summary>
    public ImmutableList<KeyController> Controllers { get; init; } = [];
    /// <summary>Imported from a MIDI percussion channel (10): it needs a drum instrument, and plays on the piano until then.</summary>
    public bool Percussion { get; init; }
    /// <summary>The track's level in Generate's WAVs, in dB (osu! has one volume per object, so the balance is baked in).</summary>
    public double VolumeDb { get; init; }
    public const double MinVolumeDb = -60, MaxVolumeDb = 12;

    public static KeyTrack Create(string name) => new(Guid.NewGuid(), name, KeyInstrument.Piano, []);
}

/// <summary>Everything Undo restores: the tracks, in order, with their notes.</summary>
public sealed record KeysoundState(ImmutableList<KeyTrack> Tracks)
{
    /// <summary>Bookmarks on the timeline (ms, sorted): the reference's at first, then the project's own edits.</summary>
    public ImmutableList<KeyBookmark> Bookmarks { get; init; } = [];

    public KeyTrack? Track(Guid id) => Tracks.FirstOrDefault(t => t.Id == id);

    public KeysoundState WithTrack(Guid id, Func<KeyTrack, KeyTrack> change)
    {
        int i = Tracks.FindIndex(t => t.Id == id);
        return i < 0 ? this : this with { Tracks = Tracks.SetItem(i, change(Tracks[i])) };
    }
}

/// <summary>A timeline bookmark (ms) with an optional label, as the Hitsound editor's.</summary>
public sealed record KeyBookmark(double Time, string Label = "");

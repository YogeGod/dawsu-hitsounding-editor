using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

public enum LaneSource
{
    /// <summary>A sample file in the mapset folder: a custom-index sample or a custom filename.</summary>
    BeatmapFile,

    /// <summary>A sound the player's skin provides: index 0, or a custom-index file that is missing.</summary>
    Skin,

    /// <summary>A file played by a storyboard Sample line.</summary>
    StoryboardFile,
}

/// <summary>The lane groups of the UI (docs/ui-design.md).</summary>
public enum LaneGroup
{
    SoftSet,
    NormalSet,
    DrumSet,
    OwnFiles,
    Skin,
}

/// <summary>
/// One sound source, as in the research note's lane model. <see cref="Name"/> is the file name as it is on disk for
/// files that exist, the name the map asks for when <see cref="Missing"/>, and e.g. "soft-hitclap" for skin sounds.
/// </summary>
public sealed record Lane(LaneSource Source, string Name, bool Missing = false)
{
    public LaneGroup Group => Source == LaneSource.Skin ? LaneGroup.Skin : SetOf(Name) switch
    {
        SampleSet.Soft => LaneGroup.SoftSet,
        SampleSet.Normal => LaneGroup.NormalSet,
        SampleSet.Drum => LaneGroup.DrumSet,
        _ => LaneGroup.OwnFiles,
    };

    /// <summary>The sample set a file name follows from its "soft-" / "normal-" / "drum-" prefix, if any.</summary>
    private static SampleSet SetOf(string name)
    {
        var file = Path.GetFileName(name);
        int dash = file.IndexOf('-');
        return dash > 0 && Enum.TryParse(file[..dash], ignoreCase: true, out SampleSet set) && set != SampleSet.None ? set : SampleSet.None;
    }
}

public enum HitPosition
{
    Circle,
    SliderHead,
    SliderRepeat,
    SliderTail,
    SpinnerEnd,
}

/// <summary>One lane playing at a moment. A layered hitnormal is one a skin may switch off (LayeredHitSounds: 0).</summary>
public sealed record Hit(Lane Lane, bool Layered = false);

/// <summary>
/// A moment where hitsounds play: a circle, a slider head/repeat/tail, or a spinner end.
/// All its hits share one <see cref="Volume"/>: osu! has a single volume per object or slider node.
/// </summary>
public sealed record Moment(
    double Time,
    HitObject Object,
    int Node,
    HitPosition Position,
    int Volume,
    IReadOnlyList<Hit> Hits,
    TimingPoint? SamplePoint)
{
    /// <summary>What osu!stable actually plays: it never goes below 8 %.</summary>
    public int PlayedVolume => Math.Max(Volume, HitsoundRules.StableMinimumVolume);
}

public enum PassiveKind
{
    SliderSlide,
    SliderWhistle,
    SliderTick,
    StoryboardSample,
}

/// <summary>
/// A sound shown read-only in v1: slider body slide/whistle (a span), slider ticks, and storyboard samples.
/// <see cref="EndTime"/> equals <see cref="Time"/> for one-shot sounds.
/// </summary>
public sealed record PassiveSound(PassiveKind Kind, double Time, double EndTime, Lane Lane, int Volume, HitObject? Object = null);

public sealed record DecompiledMap(
    MapSettings Settings,
    IReadOnlyList<Lane> Lanes,
    IReadOnlyList<Moment> Moments,
    IReadOnlyList<PassiveSound> Passive,
    IReadOnlyList<string> Warnings);

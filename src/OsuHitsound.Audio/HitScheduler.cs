using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Audio;

/// <summary>A sample at a timeline time and volume; with EndTime it loops over that half-open interval.</summary>
public readonly record struct Cue(double Time, Lane Lane, float Volume, double? EndTime = null);

/// <summary>
/// Every sound a decompiled map plays, in time order: hits, ticks, storyboard samples and slider-body loops.
/// </summary>
public sealed class HitScheduler
{
    private readonly Cue[] cues;
    private readonly Cue[] loops;

    public HitScheduler(DecompiledMap map)
        : this(map.Moments.SelectMany(m => m.Hits.Select(h => HitCue(m.Time, h.Lane, m.Volume))).Concat(PassiveCues(map)))
    {
    }

    public HitScheduler(IEnumerable<Cue> cues)
    {
        this.cues = [.. cues.OrderBy(c => c.Time)];
        loops = [.. this.cues.Where(c => c.EndTime > c.Time)];
    }

    /// <summary>A hitsound as osu!stable plays it: never below 8 %.</summary>
    public static Cue HitCue(double time, Lane lane, int volume) =>
        new(time, lane, Math.Clamp(Math.Max(volume, HitsoundRules.StableMinimumVolume), 0, 100) / 100f);

    /// <summary>The map's read-only sounds, including loops with explicit end times.</summary>
    public static IEnumerable<Cue> PassiveCues(DecompiledMap map)
    {
        foreach (var p in map.Passive)
        {
            if (p.Kind is PassiveKind.SliderTick)
                yield return HitCue(p.Time, p.Lane, p.Volume);
            else if (p.Kind is PassiveKind.StoryboardSample)
                yield return new Cue(p.Time, p.Lane, Math.Clamp(p.Volume, 0, 100) / 100f);
            else if (p.Kind is PassiveKind.SliderSlide or PassiveKind.SliderWhistle && p.EndTime > p.Time)
                yield return HitCue(p.Time, p.Lane, p.Volume) with { EndTime = p.EndTime };
        }
    }

    public IReadOnlyList<Cue> Cues => cues;

    public double EndTime => cues.Select(c => c.EndTime ?? c.Time).DefaultIfEmpty().Max();

    /// <summary>The cues with <paramref name="from"/> &lt; time ≤ <paramref name="to"/>: what to start when playback moves from one to the other.</summary>
    public IEnumerable<Cue> Between(double from, double to)
    {
        for (int i = FirstAfter(from); i < cues.Length && cues[i].Time <= to; i++)
            if (cues[i].EndTime is null) yield return cues[i];
    }

    /// <summary>Loops overlapping [from, to), including a loop already running when playback seeks into it.</summary>
    public IEnumerable<Cue> LoopsBetween(double from, double to)
    {
        foreach (var loop in loops)
        {
            if (loop.Time >= to) yield break;
            if (loop.EndTime > from) yield return loop;
        }
    }

    private int FirstAfter(double time)
    {
        int lo = 0, hi = cues.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (cues[mid].Time <= time) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }
}

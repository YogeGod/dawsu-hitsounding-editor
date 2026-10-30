using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Audio;

/// <summary>One sound to start: a lane's sample at a time (ms, as shown on the timeline) and volume (0–1).</summary>
public readonly record struct Cue(double Time, Lane Lane, float Volume);

/// <summary>
/// Every sound a decompiled map plays, in time order: each moment's hits, slider ticks and storyboard samples. Slider
/// slides and slider whistles are left out: they loop for the slider's length and are not played yet.
/// </summary>
public sealed class HitScheduler
{
    private readonly Cue[] cues;

    public HitScheduler(DecompiledMap map)
    {
        var all = new List<Cue>();
        foreach (var m in map.Moments)
            foreach (var hit in m.Hits)
                all.Add(new Cue(m.Time, hit.Lane, m.PlayedVolume / 100f));

        foreach (var p in map.Passive)
        {
            if (p.Kind is PassiveKind.SliderTick)
                all.Add(new Cue(p.Time, p.Lane, Math.Max(p.Volume, HitsoundRules.StableMinimumVolume) / 100f));
            else if (p.Kind is PassiveKind.StoryboardSample)
                all.Add(new Cue(p.Time, p.Lane, Math.Clamp(p.Volume, 0, 100) / 100f));
        }

        cues = [.. all.OrderBy(c => c.Time)];
    }

    public IReadOnlyList<Cue> Cues => cues;

    /// <summary>The cues with <paramref name="from"/> &lt; time ≤ <paramref name="to"/>: what to start when playback moves from one to the other.</summary>
    public IEnumerable<Cue> Between(double from, double to)
    {
        for (int i = FirstAfter(from); i < cues.Length && cues[i].Time <= to; i++)
            yield return cues[i];
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

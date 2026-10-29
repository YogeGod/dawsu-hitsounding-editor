namespace OsuHitsound.Format;

/// <summary>
/// How osu! picks the timing point and sample file for a hitsound. See docs/osu-hitsound-rules.md.
/// </summary>
public static class HitsoundRules
{
    /// <summary>Objects read the timing point in effect this many ms after their time (lazer CONTROL_POINT_LENIENCY).</summary>
    public const double ControlPointLeniency = 5;

    /// <summary>
    /// The timing point whose sample settings apply to a hitsound at <paramref name="time"/>: the latest one at or
    /// before time + 5 ms. Among points at the same time, the one later in the file wins. Null before the first point.
    /// </summary>
    public static TimingPoint? SamplePointAt(IEnumerable<TimingPoint> pointsInFileOrder, double time)
    {
        TimingPoint? best = null;
        foreach (var p in pointsInFileOrder)
            if (p.Time <= time + ControlPointLeniency && (best is null || p.Time >= best.Time))
                best = p;
        return best;
    }

    /// <summary>
    /// The beatmap sample file name without extension, e.g. "soft-hitclap3". Index 0 means the player's skin only,
    /// so there is no beatmap file and this returns null. Index 1 has no number suffix.
    /// </summary>
    public static string? BeatmapSampleName(SampleSet set, string sound, int index)
    {
        if (index <= 0) return null;
        if (set is SampleSet.None) throw new ArgumentException("Resolve SampleSet.None to a real set first.", nameof(set));
        var name = $"{set.ToString().ToLowerInvariant()}-{sound}";
        return index == 1 ? name : name + index;
    }

    /// <summary>Extensions osu! tries for a beatmap sample, in order.</summary>
    public static readonly string[] SampleExtensions = [".wav", ".mp3", ".ogg"];
}

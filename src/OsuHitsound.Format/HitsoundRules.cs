namespace OsuHitsound.Format;

/// <summary>
/// How osu! picks the timing point and sample file for a hitsound. See docs/osu-hitsound-rules.md.
/// </summary>
public static class HitsoundRules
{
    /// <summary>Objects read the timing point in effect this many ms after their time (lazer CONTROL_POINT_LENIENCY).</summary>
    public const double ControlPointLeniency = 5;

    /// <summary>osu!stable never plays a hitsound below 8 % (lazer: 5 %).</summary>
    public const int StableMinimumVolume = 8;

    /// <summary>
    /// The timing point whose sample settings apply to a hitsound at <paramref name="time"/>: the latest one at or
    /// before time + 5 ms. Among points at the same time, the one later in the file wins. Before the first point, the
    /// first point. Unreadable points are skipped. Builds a <see cref="TimingPointIndex"/> per call: for many lookups,
    /// build one index and query it instead.
    /// </summary>
    public static TimingPoint? SamplePointAt(IEnumerable<TimingPoint> pointsInFileOrder, double time) =>
        new TimingPointIndex(pointsInFileOrder).SamplePointAt(time);

    /// <summary>The sample name without index or extension, e.g. "soft-hitclap"; also the skin's file name.</summary>
    public static string SampleName(SampleSet set, string sound)
    {
        if (set is SampleSet.None) throw new ArgumentException("Resolve SampleSet.None to a real set first.", nameof(set));
        return $"{set.ToString().ToLowerInvariant()}-{sound}";
    }

    /// <summary>
    /// The beatmap sample file name without extension, e.g. "soft-hitclap3". Index 0 means the player's skin only,
    /// so there is no beatmap file and this returns null. Index 1 has no number suffix.
    /// </summary>
    public static string? BeatmapSampleName(SampleSet set, string sound, int index)
    {
        if (index <= 0) return null;
        var name = SampleName(set, sound);
        return index == 1 ? name : name + index;
    }

    /// <summary>Extensions osu! tries for a beatmap sample, in order.</summary>
    public static readonly string[] SampleExtensions = [".wav", ".mp3", ".ogg"];

    /// <summary>
    /// The reverse of <see cref="BeatmapSampleName"/>: set, sound and index from a sample name without extension, e.g.
    /// "drum-hitclap3" or "soft-sliderslide" (index 1). Null for names osu! never asks for by index: other prefixes, an
    /// explicit "1" or "0", or a leading zero (those are only reachable through the filename field).
    /// </summary>
    public static (SampleSet Set, string Sound, int Index)? ParseSampleName(string nameWithoutExtension)
    {
        int dash = nameWithoutExtension.IndexOf('-');
        if (dash <= 0 || !Enum.TryParse(nameWithoutExtension[..dash], ignoreCase: true, out SampleSet set) || set == SampleSet.None
            || !nameWithoutExtension[..dash].All(char.IsAsciiLetter))
            return null;
        var rest = nameWithoutExtension[(dash + 1)..];
        int digitsAt = rest.Length;
        while (digitsAt > 0 && char.IsAsciiDigit(rest[digitsAt - 1])) digitsAt--;
        string sound = rest[..digitsAt], digits = rest[digitsAt..];
        if (sound.Length == 0 || !sound.All(char.IsAsciiLetter)) return null;
        if (digits.Length == 0) return (set, sound.ToLowerInvariant(), 1);
        return digits[0] != '0' && int.TryParse(digits, out int index) && index >= 2 ? (set, sound.ToLowerInvariant(), index) : null;
    }
}

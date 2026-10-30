namespace OsuHitsound.Format;

/// <summary>
/// Lookups over a map's timing points: the latest point at or before the query time, and among points at the same time
/// the later line in the file. A query before the first point gets the first point (lazer's behaviour; unverified for
/// stable). Unreadable points are skipped and reported through the optional warnings list.
/// </summary>
public sealed class TimingPointIndex
{
    private readonly (double Time, TimingPoint Point)[] all;
    private readonly (double Time, TimingPoint Point)[] red;
    private readonly (double Time, TimingPoint Point)[] greens;

    public TimingPointIndex(IEnumerable<TimingPoint> pointsInFileOrder, ICollection<string>? warnings = null)
    {
        var parsed = new List<(double, TimingPoint)>();
        foreach (var p in pointsInFileOrder)
        {
            try
            {
                parsed.Add((p.Time, p));
            }
            catch (FormatException)
            {
                warnings?.Add($"Skipped an unreadable timing point: {p.Line.Text}");
            }
        }

        // OrderBy is stable, so equal times keep file order and the last of them is the later line.
        all = [.. parsed.OrderBy(p => p.Item1)];
        red = [.. all.Where(p => p.Point.Uninherited)];
        greens = [.. all.Where(p => !p.Point.Uninherited)];
    }

    /// <summary>The point whose sample settings apply to a hitsound at <paramref name="time"/> (looked up at time + 5 ms).</summary>
    public TimingPoint? SamplePointAt(double time) => AtOrBefore(all, time + HitsoundRules.ControlPointLeniency);

    /// <summary>The red line (BPM) in effect at <paramref name="time"/>.</summary>
    public TimingPoint? RedLineAt(double time) => AtOrBefore(red, time);

    /// <summary>Slider velocity multiplier at <paramref name="time"/> (red lines reset it to 1) and whether ticks are on (NaN green lines turn them off).</summary>
    public (double Velocity, bool Ticks) SliderVelocityAt(double time)
    {
        // SV is its own timeline (lazer's DifficultyControlPoints): a red line only resets it when strictly later than
        // the green line, whatever their order in the file. Before the first point, the first point's time applies.
        if (all.Length == 0) return (1, true);
        time = Math.Max(time, all[0].Time);
        var green = AtOrBefore(greens, time, fallBackToFirst: false);
        var redLine = AtOrBefore(red, time, fallBackToFirst: false);
        if (green is null || (redLine is not null && redLine.Time > green.Time))
            return (1, true);

        double beatLength = green.BeatLength;
        if (double.IsNaN(beatLength))
            return (1, false);

        return (beatLength < 0 ? Math.Clamp(100.0 / -beatLength, 0.1, 10) : 1, true);
    }

    private static TimingPoint? AtOrBefore((double Time, TimingPoint Point)[] points, double time, bool fallBackToFirst = true)
    {
        if (points.Length == 0 || (!fallBackToFirst && points[0].Time > time)) return null;
        int lo = 0, hi = points.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (points[mid].Time <= time) lo = mid + 1;
            else hi = mid;
        }

        return points[Math.Max(lo - 1, 0)].Point;
    }
}

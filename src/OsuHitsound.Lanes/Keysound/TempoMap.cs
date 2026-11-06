using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Keysound;

/// <summary>One red line as the Keysound project uses it: where it starts, its beat length and meter, and the tick it falls on.</summary>
public sealed record TempoSegment(double Time, double BeatLength, int Meter, double StartTick);

/// <summary>
/// Converts between song time (ms) and musical position (ticks, <see cref="Ppq"/> per beat) using the reference beatmap's
/// red lines. Tick 0 is the first red line; beats accumulate across red lines, so notes keep their musical positions
/// when tempo changes. Times before the first red line extend its tempo backwards.
/// </summary>
public sealed class TempoMap
{
    public const int Ppq = 960;
    private const double DefaultBeatLength = 500; // 120 BPM, used only when the reference has no valid red line

    public IReadOnlyList<TempoSegment> Segments { get; }

    public TempoMap(IEnumerable<(double Time, double BeatLength, int Meter)> redLines)
    {
        // osu! lets a later red line at the same time win; invalid beat lengths are ignored.
        var lines = redLines.Where(r => double.IsFinite(r.Time) && double.IsFinite(r.BeatLength) && r.BeatLength > 0)
            .Select((r, i) => (r.Time, r.BeatLength, Meter: r.Meter > 0 ? r.Meter : 4, Order: i))
            .OrderBy(r => r.Time).ThenBy(r => r.Order)
            .GroupBy(r => r.Time).Select(g => g.Last()).ToList();
        if (lines.Count == 0) lines.Add((0, DefaultBeatLength, 4, 0));

        var segments = new List<TempoSegment>(lines.Count);
        double tick = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) tick += (lines[i].Time - lines[i - 1].Time) / lines[i - 1].BeatLength * Ppq;
            segments.Add(new TempoSegment(lines[i].Time, lines[i].BeatLength, lines[i].Meter, tick));
        }

        Segments = segments;
        // Bars count as in Hitsound mode: from a whole bar at or before 0 ms, so bars before the first red line exist.
        var first = segments[0];
        FirstBar = 1 + Math.Max(0, (int)Math.Ceiling(first.Time / (first.BeatLength * first.Meter) - 1e-6));
    }

    /// <summary>The bar number of the first red line: 1, plus the whole bars that fit between 0 ms and it.</summary>
    public int FirstBar { get; }

    /// <summary>The reference's red lines, in play time (<paramref name="timeOffset"/> added, as the editor shows them).</summary>
    public static TempoMap FromBeatmap(OsuFile file, double timeOffset = 0) =>
        new(file.TimingPoints.Where(p => p.Uninherited).Select(p => (p.Time + timeOffset, p.BeatLength, p.Meter)));

    /// <summary>The red line governing <paramref name="time"/> (the first one before it starts).</summary>
    public TempoSegment SegmentAtTime(double time) => Segments[Index(s => s.Time <= time)];

    public TempoSegment SegmentAtTick(double tick) => Segments[Index(s => s.StartTick <= tick)];

    public double TickAt(double time)
    {
        var s = SegmentAtTime(time);
        return s.StartTick + (time - s.Time) / s.BeatLength * Ppq;
    }

    public double TimeAt(double tick)
    {
        var s = SegmentAtTick(tick);
        return s.Time + (tick - s.StartTick) / Ppq * s.BeatLength;
    }

    /// <summary>The nearest tick to <paramref name="time"/>.</summary>
    public long RoundTick(double time) => (long)Math.Round(TickAt(time));

    /// <summary>
    /// Snaps a time to the grid of its red line: every <paramref name="beats"/>/<paramref name="divisions"/> beats counted
    /// from that red line (as Hitsound mode's snap does); the next red line is also a snap point.
    /// </summary>
    public double Snap(double time, int beats, int divisions)
    {
        int i = Index(s => s.Time <= time);
        var s = Segments[i];
        double step = s.BeatLength * beats / divisions;
        double snapped = s.Time + Math.Round((time - s.Time) / step) * step;
        // The next red line is a grid point too; nothing snaps past it.
        if (i + 1 < Segments.Count && (snapped > Segments[i + 1].Time || Math.Abs(Segments[i + 1].Time - time) < Math.Abs(snapped - time)))
            snapped = Segments[i + 1].Time;
        return snapped;
    }

    /// <summary>Grid lines between two times: (time, level) where level 0 is a bar, 1 a beat and 2 a subdivision.</summary>
    public IEnumerable<(double Time, int Level)> GridLines(double from, double to, int divisions)
    {
        divisions = Math.Max(1, divisions);
        for (int i = Index(s => s.Time <= from); i < Segments.Count && Segments[i].Time <= to; i++)
        {
            var s = Segments[i];
            double end = i + 1 < Segments.Count ? Segments[i + 1].Time : to;
            double step = s.BeatLength / divisions;
            long first = (long)Math.Floor((from - s.Time) / step);
            if (i > 0) first = Math.Max(first, 0); // later red lines restart the grid; the first one extends backwards
            for (long n = first; ; n++)
            {
                double t = s.Time + n * step;
                if (t > to || i + 1 < Segments.Count && t >= end - 0.01) break;
                if (t < from) continue;
                long beat = Math.DivRem(n, divisions, out long sub);
                if (sub < 0) { sub += divisions; beat--; }
                int level = sub != 0 ? 2 : Mod(beat, s.Meter) == 0 ? 0 : 1;
                yield return (t, level);
            }
        }
    }

    /// <summary>"bar.beat" at a time: the first red line is bar <see cref="FirstBar"/>, and each red line starts a new bar.</summary>
    public (int Bar, int Beat) BarBeatAt(double time)
    {
        int bar = FirstBar;
        for (int i = 0; i < Segments.Count; i++)
        {
            var s = Segments[i];
            bool last = i + 1 == Segments.Count || Segments[i + 1].Time > time;
            double beats = ((last ? time : Segments[i + 1].Time) - s.Time) / s.BeatLength;
            if (last)
            {
                long whole = (long)Math.Floor(beats + 1e-6);
                return (bar + (int)Math.Floor((double)whole / s.Meter), (int)Mod(whole, s.Meter) + 1);
            }

            bar += Math.Max(1, (int)Math.Ceiling(beats / s.Meter - 1e-6));
        }

        return (bar, 1);
    }

    /// <summary>
    /// The tick where a bar and beat start (bar 1 beat 1 is tick 0, the first red line), counting bars as
    /// <see cref="BarBeatAt"/> does: each red line starts a new bar. Bars before 1 extend the first red line backwards.
    /// </summary>
    public double TickOfBarBeat(int bar, int beat)
    {
        long startBar = FirstBar;
        for (int i = 0; ; i++)
        {
            var s = Segments[i];
            bool last = i + 1 == Segments.Count;
            // Bars before the first red line extend it backwards; otherwise the bar lies in this red line's span or later.
            if (last || bar < startBar + Math.Max(1, (long)Math.Ceiling((Segments[i + 1].Time - s.Time) / s.BeatLength / s.Meter - 1e-6)))
                return s.StartTick + ((double)(bar - startBar) * s.Meter + (beat - 1)) * Ppq;
            startBar += Math.Max(1, (long)Math.Ceiling((Segments[i + 1].Time - s.Time) / s.BeatLength / s.Meter - 1e-6));
        }
    }

    /// <summary>bar.beat.tick for the time display: the tick is the sixteenth of the beat, from 1 (as Hitsound mode shows it).</summary>
    public (int Bar, int Beat, int Tick) BarBeatTickAt(double time)
    {
        var (bar, beat) = BarBeatAt(time);
        double beats = (TickAt(time) - TickOfBarBeat(bar, beat)) / Ppq;
        return (bar, beat, Math.Clamp((int)Math.Floor(beats * 16 + 1e-6), 0, 15) + 1);
    }

    private static long Mod(long a, long m) => ((a % m) + m) % m;

    // Index of the last segment matching (segments are sorted), or 0 when none does.
    private int Index(Func<TempoSegment, bool> atOrBefore)
    {
        int lo = 0, hi = Segments.Count - 1, found = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (atOrBefore(Segments[mid])) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }

        return found;
    }
}

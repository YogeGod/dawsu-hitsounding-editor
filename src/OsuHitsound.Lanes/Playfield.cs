using OsuHitsound.Format;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.Lanes;

/// <summary>A point on osu!'s 512 × 384 playfield.</summary>
public readonly record struct PlayfieldPoint(double X, double Y)
{
    public double DistanceTo(PlayfieldPoint other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));

    public static PlayfieldPoint Lerp(PlayfieldPoint a, PlayfieldPoint b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}

/// <summary>
/// One object as the map preview draws it. Times are editor times (the file's plus <see cref="MapSettings.TimeOffset"/>).
/// <see cref="Path"/> is a slider's body from head to the end of one span (a single point for other objects), before
/// stacking; <see cref="StackOffset"/> moves the whole object, as osu! does for stacked notes.
/// </summary>
public sealed record PlayfieldObject(HitObjectKind Kind, double Start, double End, IReadOnlyList<PlayfieldPoint> Path, int Spans,
    IReadOnlyList<double> TickTimes, int Combo, int Colour)
{
    private double[]? lengths; // distance along Path up to each point

    public int StackHeight { get; internal set; }

    /// <summary>How far the object is moved up and left by stacking, in playfield units (the same on both axes).</summary>
    public double StackOffset { get; internal set; }

    public PlayfieldPoint Position => Path[0];

    public double SpanDuration => Spans > 0 ? (End - Start) / Spans : 0;

    /// <summary>Where the object ends: a slider's tail side after its last span, else its position.</summary>
    public PlayfieldPoint EndPosition => Kind == HitObjectKind.Slider && Spans % 2 == 1 ? Path[^1] : Path[0];

    /// <summary>The point at a share (0 to 1) of the path's length.</summary>
    public PlayfieldPoint PointAt(double progress)
    {
        if (Path.Count < 2) return Path[0];
        if (lengths is null)
        {
            var built = new double[Path.Count];
            for (int i = 1; i < built.Length; i++) built[i] = built[i - 1] + Path[i - 1].DistanceTo(Path[i]);
            lengths = built;
        }

        double target = Math.Clamp(progress, 0, 1) * lengths[^1];
        int index = Array.BinarySearch(lengths, target);
        if (index >= 0) return Path[index];
        index = Math.Clamp(~index, 1, Path.Count - 1);
        double segment = lengths[index] - lengths[index - 1];
        return segment <= 0 ? Path[index] : PlayfieldPoint.Lerp(Path[index - 1], Path[index], (target - lengths[index - 1]) / segment);
    }

    /// <summary>Where a slider's ball is at a time: back and forth along the path, once per span.</summary>
    public PlayfieldPoint BallAt(double time)
    {
        if (Kind != HitObjectKind.Slider || SpanDuration <= 0) return Position;
        double spans = Math.Clamp((time - Start) / SpanDuration, 0, Spans);
        int span = Math.Min((int)spans, Spans - 1);
        double progress = spans - span;
        return PointAt(span % 2 == 1 ? 1 - progress : progress);
    }
}

/// <summary>
/// The gameplay objects of an osu!standard map laid out for the map preview: where each one is, when it appears and
/// how big it is, read from the map as osu! reads it (circle size, approach rate, slider curves, stacking).
/// </summary>
public sealed class Playfield
{
    public const double Width = 512, Height = 384;

    private const double StackDistance = 3;

    private Playfield(IReadOnlyList<PlayfieldObject> objects, double radius, double preempt, int mode)
    {
        (Objects, Radius, Preempt, Mode) = (objects, radius, preempt, mode);
        LongestObject = objects.Count > 0 ? objects.Max(o => o.End - o.Start) : 0;
    }

    /// <summary>Every readable object, in time order.</summary>
    public IReadOnlyList<PlayfieldObject> Objects { get; }

    /// <summary>The hit circle radius in playfield units, from the map's circle size.</summary>
    public double Radius { get; }

    /// <summary>How long before its time an object starts to appear, in ms, from the map's approach rate.</summary>
    public double Preempt { get; }

    /// <summary>How long an object takes to fade in, in ms.</summary>
    public double FadeIn => 400 * Math.Min(1, Preempt / 450);

    /// <summary>The map's game mode: only 0 (osu!standard) has a playfield to show.</summary>
    public int Mode { get; }

    public double LongestObject { get; }

    /// <summary>The objects on screen between two times (an object counts from when it appears until it has gone), in time order.</summary>
    public IEnumerable<PlayfieldObject> Between(double from, double to)
    {
        for (int i = Sorted.LowerBound(Objects, from - LongestObject, o => o.Start); i < Objects.Count && Objects[i].Start <= to; i++)
            if (Objects[i].End >= from) yield return Objects[i];
    }

    public static Playfield Build(OsuFile file)
    {
        var settings = MapSettings.Read(file);
        var (circleSize, approachRate, stackLeniency) = Difficulty(file);
        double radius = 54.4 - 4.48 * circleSize;
        double preempt = approachRate < 5 ? 1200 + 600 * (5 - approachRate) / 5 : 1200 - 750 * (approachRate - 5) / 5;

        var timing = new TimingPointIndex(file.TimingPoints);
        var combos = Combos(file);
        var warnings = new List<string>();
        var objects = new List<PlayfieldObject>();
        for (int i = 0; i < file.HitObjects.Count; i++)
        {
            var o = file.HitObjects[i];
            try
            {
                var position = new PlayfieldPoint(o.X, o.Y);
                double start = o.Time;
                var (combo, colour) = combos[i];
                PlayfieldObject built;
                switch (o.Kind)
                {
                    case HitObjectKind.Slider:
                        int spans = o.NodeCount - 1;
                        var slider = SliderTiming.Compute(o, start, spans, settings, timing, warnings);
                        var path = SliderPath.Compute(position, CurveType(o), [.. o.CurvePoints.Select(p => new PlayfieldPoint(p.X, p.Y))], SliderLength(o));
                        built = new(o.Kind, start, start + slider.SpanDuration * spans, path, spans, [.. slider.TickTimes.Select(t => t + settings.TimeOffset)], combo, colour);
                        break;
                    case HitObjectKind.Spinner:
                        built = new(o.Kind, start, Math.Max(start, o.SpinnerEndTime), [new(Width / 2, Height / 2)], 0, [], 0, colour);
                        break;
                    default:
                        built = new(o.Kind, start, start, [position], 0, [], combo, colour);
                        break;
                }

                if (!double.IsFinite(built.Start) || !double.IsFinite(built.End) || !built.Path.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y))) continue;
                objects.Add(built with { Start = built.Start + settings.TimeOffset, End = built.End + settings.TimeOffset });
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException)
            {
                // An unreadable object line: the preview leaves it out, as the lanes do.
            }
        }

        objects = [.. objects.OrderBy(o => o.Start)];
        if (settings.Mode == 0 && settings.FormatVersion >= 6) Stack(objects, preempt * stackLeniency);
        foreach (var o in objects) o.StackOffset = o.StackHeight * radius / 10;
        return new Playfield(objects, radius, preempt, settings.Mode);
    }

    // Combo numbers and colours; plain 1s in the first colour when an object line can't be read.
    private static (int Combo, int Colour)[] Combos(OsuFile file)
    {
        try
        {
            return KeysoundStore.ComboOf(file);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return [.. file.HitObjects.Select(_ => (1, 0))];
        }
    }

    // CircleSize, ApproachRate (the overall difficulty when the map has none, as in old maps) and StackLeniency.
    private static (double CircleSize, double ApproachRate, double StackLeniency) Difficulty(OsuFile file)
    {
        double circleSize = 5, overall = 5, leniency = 0.7;
        double? approach = null;
        foreach (var line in file.Lines)
        {
            if (line.Section is not ("General" or "Difficulty")) continue;
            var text = line.Text;
            int colon = text.IndexOf(':');
            if (colon <= 0 || !OsuNumber.TryParseDouble(text[(colon + 1)..].Trim(), out double value) || !double.IsFinite(value)) continue;
            switch (line.Section, text[..colon].Trim())
            {
                case ("Difficulty", "CircleSize"): circleSize = value; break;
                case ("Difficulty", "OverallDifficulty"): overall = value; break;
                case ("Difficulty", "ApproachRate"): approach = value; break;
                case ("General", "StackLeniency"): leniency = value; break;
            }
        }

        return (Math.Clamp(circleSize, 0, 10), Math.Clamp(approach ?? overall, 0, 10), Math.Clamp(leniency, 0, 1));
    }

    // 0 (the path as its control points give it) when a map leaves the length out.
    private static double SliderLength(HitObject slider) =>
        slider.Line.Text.Split(',') is { Length: > 7 } fields && OsuNumber.TryParseDouble(fields[7], out double length) && length > 0 ? length : 0;

    private static char CurveType(HitObject slider)
    {
        var fields = slider.Line.Text.Split(',');
        return fields.Length > 5 && fields[5].Length > 0 ? char.ToUpperInvariant(fields[5][0]) : 'B';
    }

    /// <summary>lazer's stacking (OsuBeatmapProcessor.applyNewStacking): notes on the same spot within the stack time step up and left.</summary>
    private static void Stack(List<PlayfieldObject> objects, double threshold)
    {
        for (int i = objects.Count - 1; i > 0; i--)
        {
            var current = objects[i];
            if (current.StackHeight != 0 || current.Kind == HitObjectKind.Spinner) continue;

            if (current.Kind == HitObjectKind.Slider)
            {
                for (int n = i - 1; n >= 0; n--)
                {
                    var earlier = objects[n];
                    if (earlier.Kind == HitObjectKind.Spinner) continue;
                    if (current.Start - earlier.Start > threshold) break;
                    if (earlier.EndPosition.DistanceTo(current.Position) < StackDistance)
                    {
                        earlier.StackHeight = current.StackHeight + 1;
                        current = earlier;
                    }
                }

                continue;
            }

            for (int n = i - 1; n >= 0; n--)
            {
                var earlier = objects[n];
                if (earlier.Kind == HitObjectKind.Spinner) continue;
                if (current.Start - earlier.End > threshold) break;

                // A note on a slider's end: the slider stays, and the notes stacked after it move down and right instead.
                if (earlier.Kind == HitObjectKind.Slider && earlier.EndPosition.DistanceTo(current.Position) < StackDistance)
                {
                    int offset = current.StackHeight - earlier.StackHeight + 1;
                    for (int j = n + 1; j <= i; j++)
                        if (earlier.EndPosition.DistanceTo(objects[j].Position) < StackDistance) objects[j].StackHeight -= offset;
                    break;
                }

                if (earlier.Position.DistanceTo(current.Position) < StackDistance)
                {
                    earlier.StackHeight = current.StackHeight + 1;
                    current = earlier;
                }
            }
        }
    }
}

/// <summary>
/// A slider's body as points, from its curve field as osu! reads it: L is straight lines, P a circular arc through
/// three points, C a Catmull-Rom spline, B (and anything else) Bézier curves; a control point written twice starts a
/// new segment. The result is cut, or its last straight piece extended, to the slider's length.
/// </summary>
public static class SliderPath
{
    private const int MaxPoints = 20000; // a crafted slider can't ask for more

    public static IReadOnlyList<PlayfieldPoint> Compute(PlayfieldPoint start, char type, IReadOnlyList<PlayfieldPoint> controlPoints, double length)
    {
        List<PlayfieldPoint> all = [start, .. controlPoints];
        var points = new List<PlayfieldPoint> { start };
        int segmentStart = 0;
        for (int i = 0; i < all.Count; i++)
        {
            // A segment ends at the last point, or at a point that is written twice (the second one starts the next).
            if (i < all.Count - 1 && all[i + 1] != all[i]) continue;
            Append(points, Segment(type, all.GetRange(segmentStart, i + 1 - segmentStart)));
            segmentStart = i + 1;
        }

        return Fit(points, length);
    }

    private static IReadOnlyList<PlayfieldPoint> Segment(char type, List<PlayfieldPoint> points)
    {
        if (points.Count < 2) return points;
        return type switch
        {
            'L' => points,
            'P' when points.Count == 3 => Arc(points[0], points[1], points[2]) ?? Bezier(points),
            'C' => Catmull(points),
            _ => points.Count == 2 ? points : Bezier(points),
        };
    }

    private static void Append(List<PlayfieldPoint> to, IReadOnlyList<PlayfieldPoint> from)
    {
        foreach (var p in from)
            if (to.Count < MaxPoints && to[^1] != p) to.Add(p);
    }

    // Evenly spaced curve parameters, about one point every 4 playfield units of the control polygon.
    private static List<PlayfieldPoint> Bezier(List<PlayfieldPoint> control)
    {
        double polygon = 0;
        for (int i = 1; i < control.Count; i++) polygon += control[i - 1].DistanceTo(control[i]);
        int steps = (int)Math.Clamp(Math.Ceiling(polygon / 4), 8, 400);
        var result = new List<PlayfieldPoint>(steps + 1);
        var work = new PlayfieldPoint[control.Count];
        for (int s = 0; s <= steps; s++)
        {
            double t = (double)s / steps;
            control.CopyTo(work);
            for (int level = control.Count - 1; level > 0; level--)
                for (int i = 0; i < level; i++)
                    work[i] = PlayfieldPoint.Lerp(work[i], work[i + 1], t);
            result.Add(work[0]);
        }

        return result;
    }

    private static List<PlayfieldPoint> Catmull(List<PlayfieldPoint> p)
    {
        const int detail = 20;
        var result = new List<PlayfieldPoint>();
        for (int i = 0; i < p.Count - 1; i++)
        {
            var v1 = i > 0 ? p[i - 1] : p[i];
            var (v2, v3) = (p[i], p[i + 1]);
            var v4 = i < p.Count - 2 ? p[i + 2] : new PlayfieldPoint(v3.X * 2 - v2.X, v3.Y * 2 - v2.Y);
            for (int s = 0; s <= detail; s++)
            {
                double t = (double)s / detail;
                result.Add(new(Spline(v1.X, v2.X, v3.X, v4.X, t), Spline(v1.Y, v2.Y, v3.Y, v4.Y, t)));
            }
        }

        return result;

        static double Spline(double a, double b, double c, double d, double t) =>
            0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
    }

    // The arc from a through b to c; null when the points are on one line (osu! then draws a Bézier).
    private static List<PlayfieldPoint>? Arc(PlayfieldPoint a, PlayfieldPoint b, PlayfieldPoint c)
    {
        double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-6) return null;
        double aSq = a.X * a.X + a.Y * a.Y, bSq = b.X * b.X + b.Y * b.Y, cSq = c.X * c.X + c.Y * c.Y;
        var centre = new PlayfieldPoint(
            (aSq * (b.Y - c.Y) + bSq * (c.Y - a.Y) + cSq * (a.Y - b.Y)) / d,
            (aSq * (c.X - b.X) + bSq * (a.X - c.X) + cSq * (b.X - a.X)) / d);
        double radius = centre.DistanceTo(a);
        if (!double.IsFinite(radius) || radius > 1e6) return null;

        double from = Math.Atan2(a.Y - centre.Y, a.X - centre.X), to = Math.Atan2(c.Y - centre.Y, c.X - centre.X);
        // Which way round: the side of a→c that b is on.
        bool clockwise = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) > 0;
        if (clockwise) { while (to < from) to += 2 * Math.PI; }
        else { while (to > from) to -= 2 * Math.PI; }

        double sweep = to - from;
        int steps = (int)Math.Clamp(Math.Ceiling(Math.Abs(sweep) * radius / 4), 8, 400);
        var result = new List<PlayfieldPoint>(steps + 1);
        for (int s = 0; s <= steps; s++)
        {
            double angle = from + sweep * s / steps;
            result.Add(new(centre.X + radius * Math.Cos(angle), centre.Y + radius * Math.Sin(angle)));
        }

        return result;
    }

    // Cuts the path at the slider's length, or runs its last piece on in a straight line to reach it.
    private static List<PlayfieldPoint> Fit(List<PlayfieldPoint> points, double length)
    {
        if (points.Count < 2 || !(length > 0) || !double.IsFinite(length)) return points;
        double travelled = 0;
        for (int i = 1; i < points.Count; i++)
        {
            double step = points[i - 1].DistanceTo(points[i]);
            if (travelled + step >= length)
            {
                var cut = PlayfieldPoint.Lerp(points[i - 1], points[i], step <= 0 ? 0 : (length - travelled) / step);
                points.RemoveRange(i, points.Count - i);
                if (points[^1] != cut) points.Add(cut);
                return points;
            }

            travelled += step;
        }

        double lastStep = points[^2].DistanceTo(points[^1]);
        if (lastStep > 0)
            points[^1] = PlayfieldPoint.Lerp(points[^2], points[^1], (lastStep + length - travelled) / lastStep);
        return points;
    }
}

namespace OsuHitsound.Lanes;

/// <summary>
/// A slider's body as points, from its curve field as osu! reads it. This follows osu!lazer's
/// ConvertHitObjectParser (which curve a slider gets, and where a control point written twice starts a new segment),
/// osu-framework's PathApproximator (how each curve becomes straight pieces) and lazer's SliderPath (cutting the path,
/// or running its last piece on, to the slider's length). Both are MIT-licensed, © ppy Pty Ltd.
/// </summary>
public static class SliderPath
{
    private const double BezierTolerance = 0.25, ArcTolerance = 0.1;
    private const int CatmullDetail = 50;
    private const int MaxPoints = 50000; // a crafted slider can't ask for more

    /// <param name="start">The slider's own position.</param>
    /// <param name="type">The curve letter: L, P, C or B.</param>
    /// <param name="controlPoints">The points after the start, as written.</param>
    /// <param name="length">The slider's length; 0 keeps the path as the control points give it.</param>
    public static IReadOnlyList<PlayfieldPoint> Compute(PlayfieldPoint start, char type, IReadOnlyList<PlayfieldPoint> controlPoints, double length)
    {
        List<PlayfieldPoint> all = [start, .. controlPoints];

        // stable's edge cases: a perfect curve needs exactly three points, and three on one line are a straight line.
        if (type == 'P')
            type = all.Count != 3 ? 'B' : IsLinear(all[0], all[1], all[2]) ? 'L' : 'P';
        else if (type is not ('L' or 'C'))
            type = 'B';

        var path = new List<PlayfieldPoint>();
        int segmentStart = 0;
        for (int end = 1; end <= all.Count; end++)
        {
            // A point written twice ends a segment, and the second one starts the next. Catmull curves have one
            // segment only (apart from a first point that repeats the start), and the last point never starts one.
            bool last = end == all.Count;
            if (!last)
            {
                if (all[end] != all[end - 1]) continue;
                if (type == 'C' && end > 1) continue;
                if (end == all.Count - 1) continue;
            }

            var segment = all.GetRange(segmentStart, end - segmentStart);
            var piece = segment.Count == 1 ? segment : Segment(type, segment);
            // The first point is left out when it is where the previous segment ended.
            for (int i = path.Count > 0 && piece.Count > 0 && path[^1] == piece[0] ? 1 : 0; i < piece.Count && path.Count < MaxPoints; i++)
                path.Add(piece[i]);
            segmentStart = end;
        }

        return Fit(path, length);
    }

    private static bool IsLinear(PlayfieldPoint a, PlayfieldPoint b, PlayfieldPoint c) =>
        Math.Abs((b.Y - a.Y) * (c.X - a.X) - (b.X - a.X) * (c.Y - a.Y)) <= 1e-3;

    private static List<PlayfieldPoint> Segment(char type, List<PlayfieldPoint> points) => type switch
    {
        'L' => points,
        'P' => Arc(points[0], points[1], points[2]) ?? Bezier(points),
        'C' => Catmull(points),
        _ => Bezier(points),
    };

    /// <summary>
    /// A Bézier curve as straight pieces: the curve is halved until each part is flat enough (PathApproximator's
    /// BSplineToPiecewiseLinear for a single curve), then each flat part gives one point per control point.
    /// </summary>
    private static List<PlayfieldPoint> Bezier(List<PlayfieldPoint> control)
    {
        int count = control.Count;
        var output = new List<PlayfieldPoint>();
        var toFlatten = new Stack<PlayfieldPoint[]>();
        var free = new Stack<PlayfieldPoint[]>();
        toFlatten.Push([.. control]);
        var buffer1 = new PlayfieldPoint[count];
        var buffer2 = new PlayfieldPoint[count * 2 - 1];
        var left = buffer2;

        while (toFlatten.Count > 0 && output.Count < MaxPoints)
        {
            var parent = toFlatten.Pop();
            if (IsFlatEnough(parent))
            {
                Approximate(parent, output, buffer1, buffer2, count);
                free.Push(parent);
                continue;
            }

            var right = free.Count > 0 ? free.Pop() : new PlayfieldPoint[count];
            Subdivide(parent, left, right, buffer1, count);
            for (int i = 0; i < count; i++) parent[i] = left[i];
            toFlatten.Push(right);
            toFlatten.Push(parent);
        }

        output.Add(control[^1]);
        return output;
    }

    private static bool IsFlatEnough(PlayfieldPoint[] p)
    {
        for (int i = 1; i < p.Length - 1; i++)
        {
            double x = p[i - 1].X - 2 * p[i].X + p[i + 1].X, y = p[i - 1].Y - 2 * p[i].Y + p[i + 1].Y;
            if (x * x + y * y > BezierTolerance * BezierTolerance * 4) return false;
        }

        return true;
    }

    // De Casteljau at the middle: the control points of the curve's first and second half.
    private static void Subdivide(PlayfieldPoint[] control, PlayfieldPoint[] left, PlayfieldPoint[] right, PlayfieldPoint[] midpoints, int count)
    {
        for (int i = 0; i < count; i++) midpoints[i] = control[i];
        for (int i = 0; i < count; i++)
        {
            left[i] = midpoints[0];
            right[count - i - 1] = midpoints[count - i - 1];
            for (int j = 0; j < count - i - 1; j++)
                midpoints[j] = PlayfieldPoint.Lerp(midpoints[j], midpoints[j + 1], 0.5);
        }
    }

    private static void Approximate(PlayfieldPoint[] control, List<PlayfieldPoint> output, PlayfieldPoint[] buffer1, PlayfieldPoint[] buffer2, int count)
    {
        var (left, right) = (buffer2, buffer1);
        Subdivide(control, left, right, buffer1, count);
        for (int i = 0; i < count - 1; i++) left[count + i] = right[i + 1];
        output.Add(control[0]);
        for (int i = 1; i < count - 1; i++)
        {
            int index = 2 * i;
            output.Add(new(0.25 * (left[index - 1].X + 2 * left[index].X + left[index + 1].X), 0.25 * (left[index - 1].Y + 2 * left[index].Y + left[index + 1].Y)));
        }
    }

    private static List<PlayfieldPoint> Catmull(List<PlayfieldPoint> p)
    {
        var result = new List<PlayfieldPoint>((p.Count - 1) * CatmullDetail * 2);
        for (int i = 0; i < p.Count - 1; i++)
        {
            var v1 = i > 0 ? p[i - 1] : p[i];
            var (v2, v3) = (p[i], p[i + 1]);
            var v4 = i < p.Count - 2 ? p[i + 2] : new PlayfieldPoint(v3.X * 2 - v2.X, v3.Y * 2 - v2.Y);
            for (int c = 0; c < CatmullDetail; c++)
            {
                result.Add(Point(v1, v2, v3, v4, (double)c / CatmullDetail));
                result.Add(Point(v1, v2, v3, v4, (double)(c + 1) / CatmullDetail));
            }
        }

        return result;

        static PlayfieldPoint Point(PlayfieldPoint a, PlayfieldPoint b, PlayfieldPoint c, PlayfieldPoint d, double t) =>
            new(Spline(a.X, b.X, c.X, d.X, t), Spline(a.Y, b.Y, c.Y, d.Y, t));

        static double Spline(double a, double b, double c, double d, double t) =>
            0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
    }

    // The arc from a through b to c (CircularArcProperties); null where lazer falls back to a Bézier curve.
    private static List<PlayfieldPoint>? Arc(PlayfieldPoint a, PlayfieldPoint b, PlayfieldPoint c)
    {
        if (IsLinear(a, b, c)) return null;
        double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        double aSq = a.X * a.X + a.Y * a.Y, bSq = b.X * b.X + b.Y * b.Y, cSq = c.X * c.X + c.Y * c.Y;
        var centre = new PlayfieldPoint(
            (aSq * (b.Y - c.Y) + bSq * (c.Y - a.Y) + cSq * (a.Y - b.Y)) / d,
            (aSq * (c.X - b.X) + bSq * (a.X - c.X) + cSq * (b.X - a.X)) / d);
        double radius = centre.DistanceTo(a);
        if (!double.IsFinite(radius)) return null;

        double thetaStart = Math.Atan2(a.Y - centre.Y, a.X - centre.X), thetaEnd = Math.Atan2(c.Y - centre.Y, c.X - centre.X);
        while (thetaEnd < thetaStart) thetaEnd += 2 * Math.PI;
        double direction = 1, range = thetaEnd - thetaStart;

        // Which way round: the side of a→c that b is on.
        if ((c.Y - a.Y) * (b.X - a.X) - (c.X - a.X) * (b.Y - a.Y) < 0)
        {
            direction = -1;
            range = 2 * Math.PI - range;
        }

        int points = 2 * radius <= ArcTolerance ? 2 : Math.Max(2, (int)Math.Ceiling(range / (2 * Math.Acos(1 - ArcTolerance / radius))));
        if (points >= 1000) return null;
        var result = new List<PlayfieldPoint>(points);
        for (int i = 0; i < points; i++)
        {
            double theta = thetaStart + direction * range * i / (points - 1);
            result.Add(new(centre.X + radius * Math.Cos(theta), centre.Y + radius * Math.Sin(theta)));
        }

        return result;
    }

    // SliderPath.calculateLength: the path cut at the slider's length, or its last piece run on in a straight line
    // to reach it (not when the path ends in the same point twice, as in stable).
    private static List<PlayfieldPoint> Fit(List<PlayfieldPoint> path, double length)
    {
        if (path.Count < 2 || !(length > 0) || !double.IsFinite(length)) return path;
        var cumulative = new List<double>(path.Count) { 0 };
        double total = 0;
        for (int i = 0; i < path.Count - 1; i++) cumulative.Add(total += path[i].DistanceTo(path[i + 1]));
        if (total == length) return path;
        if (path[^1] == path[^2] && length > total) return path;

        cumulative.RemoveAt(cumulative.Count - 1);
        int end = path.Count - 1;
        if (total > length)
            while (cumulative.Count > 0 && cumulative[^1] >= length)
            {
                cumulative.RemoveAt(cumulative.Count - 1);
                path.RemoveAt(end--);
            }

        if (end <= 0) return path;
        double step = path[end - 1].DistanceTo(path[end]);
        if (step > 0) path[end] = PlayfieldPoint.Lerp(path[end - 1], path[end], (length - cumulative[^1]) / step);
        return path;
    }
}

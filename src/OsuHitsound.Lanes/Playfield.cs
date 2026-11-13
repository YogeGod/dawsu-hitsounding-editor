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
        // lazer's OsuHitObject: a 64-unit circle scaled by the circle size (with stable's 1.00041 rounding allowance),
        // and the approach time from 1800 ms (AR 0) through 1200 (AR 5) to 450 (AR 10), in whole ms.
        double radius = 64 * (1 - 0.7 * (circleSize - 5) / 5) / 2 * 1.00041;
        double preempt = (int)(approachRate < 5 ? 1200 + 600 * (5 - approachRate) / 5 : 1200 - 750 * (approachRate - 5) / 5);

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
                        var path = SliderPath.Compute(position, CurveType(o), [.. o.CurvePoints.Select(p => new PlayfieldPoint(Math.Truncate(p.X), Math.Truncate(p.Y)))], SliderLength(o)); // whole units, as stable reads them
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
        if (settings.Mode == 0)
        {
            if (settings.FormatVersion >= 6) Stack(objects, preempt * stackLeniency);
            else StackOld(objects, preempt * stackLeniency);
        }

        foreach (var o in objects) o.StackOffset = o.StackHeight * radius / 10; // lazer: StackHeight × Scale × 6.4
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

    /// <summary>Stacking for files before v6 (OsuBeatmapProcessor.applyStackingOld): counted forwards from each object.</summary>
    private static void StackOld(List<PlayfieldObject> objects, double threshold)
    {
        for (int i = 0; i < objects.Count; i++)
        {
            var current = objects[i];
            if (current.StackHeight != 0 && current.Kind != HitObjectKind.Slider) continue;

            double startTime = current.End;
            int sliderStack = 0;
            var pathEnd = current.Kind == HitObjectKind.Slider ? current.Path[^1] : current.Position;
            for (int j = i + 1; j < objects.Count; j++)
            {
                if (objects[j].Start - threshold > startTime) break;
                if (objects[j].Position.DistanceTo(current.Position) < StackDistance)
                {
                    current.StackHeight++;
                    startTime = objects[j].Start;
                }
                else if (objects[j].Position.DistanceTo(pathEnd) < StackDistance)
                {
                    // Notes on a slider's end go down and right instead.
                    sliderStack++;
                    objects[j].StackHeight -= sliderStack;
                    startTime = objects[j].Start;
                }
            }
        }
    }

    /// <summary>lazer's stacking (OsuBeatmapProcessor.applyStacking, v6 and later): notes on the same spot within the stack time step up and left.</summary>
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
                if ((int)current.Start - (int)earlier.End > threshold) break; // whole ms, as stable compares them

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


using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>
/// When a slider's nodes and ticks happen, with lazer's formulas (Slider.ApplyDefaultsToSelf, SliderEventGenerator,
/// LegacyRulesetExtensions.GetPrecisionAdjustedBeatLength). Used to place sounds on the timeline only.
/// </summary>
internal static class SliderTiming
{
    private const double MaxLength = 100000;
    private const int MaxTicksPerSlider = 100000;

    public sealed record Result(double SpanDuration, IReadOnlyList<double> TickTimes);

    public static Result Compute(HitObject slider, double start, int spans, MapSettings settings, TimingPointIndex timing, ICollection<string> warnings)
    {
        var redLine = timing.RedLineAt(start);
        if (redLine is null || !(redLine.BeatLength > 0))
        {
            warnings.Add($"Slider at {start} has no usable red line; its repeats and tail are placed at its start.");
            return new Result(0, []);
        }

        var (sv, ticksOn) = timing.SliderVelocityAt(start);
        double beatLength = Math.Clamp(redLine.BeatLength, 6, 60000); // lazer's TimingControlPoint limits

        // The multiplier 100 / SV goes through a float, as in stable.
        double bpmMultiplier = Math.Clamp((float)(100 / sv), 10, 1000) / 100.0;
        double velocity = 100 * settings.SliderMultiplier / (beatLength * bpmMultiplier);

        double length = Length(slider);
        double spanDuration = length / velocity;

        double scoringDistance = velocity * beatLength;
        double tickMultiplier = settings.FormatVersion < 8 ? 1 / sv : 1;
        double tickDistance = ticksOn ? scoringDistance / settings.SliderTickRate * tickMultiplier : double.PositiveInfinity;

        return new Result(spanDuration, Ticks(start, spanDuration, velocity, tickDistance, length, spans, warnings));
    }

    /// <summary>SliderEventGenerator's tick placement: from the path start in every span, none within 10 ms of travel of a span end.</summary>
    private static List<double> Ticks(double start, double spanDuration, double velocity, double tickDistance, double totalDistance, int spans, ICollection<string> warnings)
    {
        var ticks = new List<double>();
        double length = Math.Min(MaxLength, totalDistance);
        tickDistance = Math.Clamp(tickDistance, 0, length);
        if (tickDistance == 0) return ticks;

        // Tick positions along the path are the same in every span; reversed spans walk them back to front.
        var progress = new List<double>();
        double minDistanceFromEnd = velocity * 10;
        for (double d = tickDistance; d <= length && d < length - minDistanceFromEnd; d += tickDistance)
            progress.Add(d / length);

        // A crafted slider (9001 spans, long, tiny tick distance) could otherwise ask for billions of ticks.
        if ((long)progress.Count * spans > MaxTicksPerSlider)
        {
            warnings.Add($"Slider at {start} would have {(long)progress.Count * spans} ticks; its ticks are not shown.");
            return ticks;
        }

        for (int span = 0; span < spans; span++)
        {
            double spanStart = start + span * spanDuration;
            bool reversed = span % 2 == 1;
            for (int i = 0; i < progress.Count; i++)
            {
                double p = reversed ? 1 - progress[progress.Count - 1 - i] : progress[i];
                ticks.Add(spanStart + p * spanDuration);
            }
        }

        return ticks;
    }

    /// <summary>The length field, or (when a map omits it) the straight-line length through the control points.</summary>
    private static double Length(HitObject slider)
    {
        if (slider.Line.Text.Split(',') is { Length: > 7 } fields && OsuNumber.TryParseDouble(fields[7], out double field) && field > 0)
            return field;

        double x = slider.X, y = slider.Y, total = 0;
        foreach (var (nx, ny) in slider.CurvePoints)
        {
            total += Math.Sqrt((nx - x) * (nx - x) + (ny - y) * (ny - y));
            (x, y) = (nx, ny);
        }

        return total;
    }
}

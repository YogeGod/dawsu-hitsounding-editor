namespace OsuHitsound.Lanes;

/// <summary>Curve shapes for volume ramps (dawsu's ramp shapes).</summary>
public enum RampShape
{
    Linear,
    EaseIn,
    EaseOut,
    SCurve,
}

/// <summary>Volume ramps and value edits over moments; pure functions the volume lane and tests share.</summary>
public static class VolumeRamp
{
    /// <summary>The value at <paramref name="t"/> (0–1) of a ramp from <paramref name="from"/> to <paramref name="to"/>, rounded to 1–100.</summary>
    public static int At(RampShape shape, double t, int from, int to)
    {
        t = Math.Clamp(t, 0, 1);
        double k = shape switch
        {
            RampShape.EaseIn => t * t,
            RampShape.EaseOut => 1 - (1 - t) * (1 - t),
            RampShape.SCurve => t * t * (3 - 2 * t),
            _ => t,
        };
        return Math.Clamp((int)Math.Round(from + (to - from) * k, MidpointRounding.AwayFromZero), 1, 100);
    }

    /// <summary>
    /// Volumes for moments between two times: each gets the ramp value at its place between
    /// <paramref name="startTime"/> and <paramref name="endTime"/>.
    /// </summary>
    public static Dictionary<MomentKey, int> Ramp(IEnumerable<(double Time, MomentKey Key)> moments, double startTime, int startVolume,
        double endTime, int endVolume, RampShape shape)
    {
        if (endTime < startTime) (startTime, startVolume, endTime, endVolume) = (endTime, endVolume, startTime, startVolume);
        double span = endTime - startTime;
        return moments.Where(m => m.Time >= startTime && m.Time <= endTime)
            .ToDictionary(m => m.Key, m => At(shape, span <= 0 ? 0 : (m.Time - startTime) / span, startVolume, endVolume));
    }

    /// <summary>Relative edit: every volume moves by the same amount, kept within 1–100.</summary>
    public static Dictionary<MomentKey, int> Shift(IReadOnlyDictionary<MomentKey, int> volumes, IEnumerable<MomentKey> keys, int delta) =>
        keys.Distinct().ToDictionary(k => k, k => Math.Clamp(volumes[k] + delta, 1, 100));
}

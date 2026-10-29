using System.Globalization;

namespace OsuHitsound.Format;

/// <summary>How osu! reads numbers in .osu fields. Every parse failure is a <see cref="FormatException"/>.</summary>
internal static class OsuNumber
{
    // osu! writes some integer fields as decimals ("1234.5" times, "100.0" volumes); lazer truncates them the same way.
    public static int ParseInt(string s)
    {
        double d = ParseDouble(s);
        if (double.IsNaN(d) || d < int.MinValue || d > int.MaxValue)
            throw new FormatException($"Not an integer in range: {s}");
        return (int)d;
    }

    public static double ParseDouble(string s) =>
        double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
}

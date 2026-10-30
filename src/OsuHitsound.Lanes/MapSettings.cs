using System.Globalization;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>The header values the decompiler needs, with osu!'s defaults and lazer's clamps.</summary>
public sealed record MapSettings(int FormatVersion, int Mode, SampleSet DefaultSampleSet, double SliderMultiplier, double SliderTickRate)
{
    /// <summary>v3/v4 files play 24 ms later than written. Used for display only; written numbers never change.</summary>
    public double TimeOffset => FormatVersion < 5 ? 24 : 0;

    public static MapSettings Read(OsuFile file)
    {
        int version = 14, mode = 0;
        var sampleSet = SampleSet.Normal;
        double multiplier = 1.4, tickRate = 1;

        foreach (var line in file.Lines)
        {
            var text = line.Text.Trim();
            if (line.Section == "" && text.StartsWith("osu file format v", StringComparison.Ordinal))
                version = int.TryParse(text["osu file format v".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : version;

            if (line.Section is not ("General" or "Difficulty")) continue;
            int colon = text.IndexOf(':');
            if (colon <= 0) continue;
            string key = text[..colon].Trim(), value = text[(colon + 1)..].Trim();

            switch (line.Section, key)
            {
                case ("General", "Mode"):
                    mode = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) ? m : 0;
                    break;
                case ("General", "SampleSet"):
                    // "None", unknown names and numbers outside the enum mean Normal.
                    sampleSet = Enum.TryParse(value, ignoreCase: true, out SampleSet s) && Enum.IsDefined(s) && s != SampleSet.None ? s : SampleSet.Normal;
                    break;
                case ("Difficulty", "SliderMultiplier"):
                    multiplier = Number(value, multiplier);
                    break;
                case ("Difficulty", "SliderTickRate"):
                    tickRate = Number(value, tickRate);
                    break;
            }
        }

        return new MapSettings(version, mode, sampleSet, Math.Clamp(multiplier, 0.4, 3.6), Math.Clamp(tickRate, 0.5, 8));
    }

    private static double Number(string s, double fallback) =>
        OsuNumber.TryParseDouble(s, out double d) && double.IsFinite(d) ? d : fallback;
}

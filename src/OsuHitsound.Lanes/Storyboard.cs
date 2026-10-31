using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>
/// Storyboard Sample lines (<c>Sample,time,layer,"path",volume</c>) from a map's [Events] and from the mapset's .osb.
/// .osb [Variables] are substituted to read paths; nothing here writes.
/// </summary>
internal static class Storyboard
{
    public static IEnumerable<(double Time, string Path, int Volume)> Samples(OsuFile file, ICollection<string> warnings)
    {
        var variables = file.Lines
            .Where(l => l.Section == "Variables" && l.Text.TrimStart().StartsWith('$') && l.Text.Contains('='))
            .Select(l => l.Text.Split('=', 2))
            .GroupBy(kv => kv[0].Trim()) // lazer's SplitKeyVal trims both sides
            .ToDictionary(g => g.Key, g => g.Last()[1].Trim());

        foreach (var line in file.Lines.Where(l => l.Section == "Events"))
        {
            var text = line.Text.Trim();
            if (!text.StartsWith("Sample,", StringComparison.Ordinal) && !text.StartsWith("5,", StringComparison.Ordinal))
                continue;

            // Longest names first, so "$a" doesn't eat the start of "$ab".
            foreach (var (name, value) in variables.OrderByDescending(v => v.Key.Length))
                text = text.Replace(name, value, StringComparison.Ordinal);

            var fields = SplitQuoted(text);
            if (fields.Count < 4 || !OsuNumber.TryParseDouble(fields[1], out double time) || !double.IsFinite(time))
            {
                warnings.Add($"Skipped an unreadable storyboard sample: {line.Text}");
                continue;
            }

            int volume = fields.Count > 4 && OsuNumber.TryParseInt(fields[4], out int v) ? Math.Clamp(v, 0, 100) : 100;
            yield return (time, CleanPath(fields[3]), volume);
        }
    }

    /// <summary>lazer's LegacyDecoder.CleanFilename: no quotes, "\\" → "\", one separator style.</summary>
    private static string CleanPath(string path) => path.Trim('"').Replace(@"\\", @"\").Replace('\\', '/');

    private static List<string> SplitQuoted(string text)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        foreach (char c in text)
        {
            if (c == '"') quoted = !quoted;
            if (c == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(c);
        }

        fields.Add(current.ToString());
        return fields;
    }
}

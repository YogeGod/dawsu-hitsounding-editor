using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>
/// Storyboard Sample lines (<c>Sample,time,layer,"path",volume</c>) from a map's [Events] and from the mapset's .osb.
/// .osb [Variables] are substituted to read paths. Copy hitsounds writes them through OsuFile.InsertStoryboardSample.
/// </summary>
internal static class Storyboard
{
    public static IEnumerable<(double Time, string Path, int Volume)> Samples(OsuFile file, ICollection<string> warnings)
    {
        var variables = Variables(file);
        foreach (var line in file.Lines.Where(l => l.Section == "Events"))
        {
            var text = line.Text.Trim();
            if (!text.StartsWith("Sample,", StringComparison.Ordinal) && !text.StartsWith("5,", StringComparison.Ordinal))
                continue;

            text = Substitute(text, variables);
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

    /// <summary>
    /// A file's own Sample lines (its [Events], without .osb variables): the line, its time, layer and volume as
    /// written, and the cleaned path. Unreadable lines are left out.
    /// </summary>
    public static IEnumerable<(OsuLine Line, double Time, string Layer, string PathField, string Path, int Volume)> SampleLines(OsuFile file)
    {
        foreach (var line in file.Lines.Where(l => l.Section == "Events"))
        {
            var text = line.Text.Trim();
            if (!text.StartsWith("Sample,", StringComparison.Ordinal) && !text.StartsWith("5,", StringComparison.Ordinal)) continue;
            var fields = SplitQuoted(text);
            if (fields.Count < 4 || !OsuNumber.TryParseDouble(fields[1], out double time) || !double.IsFinite(time)) continue;
            int volume = fields.Count > 4 && OsuNumber.TryParseInt(fields[4], out int v) ? Math.Clamp(v, 0, 100) : 100;
            yield return (line, time, fields[2].Trim(), fields[3].Trim(), CleanPath(fields[3]), volume);
        }
    }

    /// <summary>
    /// The image and video files a map's [Events] (or an .osb) shows: background, video, sprites and every animation
    /// frame ("name0.png", "name1.png"…). Paths are cleaned but not checked; callers look them up in a <see cref="MapFolder"/>.
    /// </summary>
    public static IEnumerable<string> VisualPaths(OsuFile file)
    {
        var variables = Variables(file);
        foreach (var line in file.Lines.Where(l => l.Section == "Events"))
        {
            var fields = SplitQuoted(Substitute(line.Text.Trim(), variables));
            if (fields.Count < 3) continue;
            switch (fields[0].Trim())
            {
                case "0" or "1" or "Video":
                    yield return CleanPath(fields[2]);
                    break;
                case "4" or "Sprite" when fields.Count >= 4:
                    yield return CleanPath(fields[3]);
                    break;
                case "6" or "Animation" when fields.Count >= 7:
                    var path = CleanPath(fields[3]);
                    int frames = OsuNumber.TryParseInt(fields[6], out int n) ? Math.Clamp(n, 1, 1000) : 1;
                    string stem = Path.ChangeExtension(path, null), extension = Path.GetExtension(path);
                    for (int i = 0; i < frames; i++) yield return stem + i + extension;
                    break;
            }
        }
    }

    // .osb [Variables]: "$name=value"; lazer's SplitKeyVal trims both sides.
    private static Dictionary<string, string> Variables(OsuFile file) => file.Lines
        .Where(l => l.Section == "Variables" && l.Text.TrimStart().StartsWith('$') && l.Text.Contains('='))
        .Select(l => l.Text.Split('=', 2))
        .GroupBy(kv => kv[0].Trim())
        .ToDictionary(g => g.Key, g => g.Last()[1].Trim());

    // Longest names first, so "$a" doesn't eat the start of "$ab".
    private static string Substitute(string text, Dictionary<string, string> variables)
    {
        foreach (var (name, value) in variables.OrderByDescending(v => v.Key.Length))
            text = text.Replace(name, value, StringComparison.Ordinal);
        return text;
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

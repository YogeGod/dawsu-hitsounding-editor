using System.Globalization;
using System.Text;

namespace OsuHitsound.Format;

/// <summary>What kind of line a <see cref="LineChange"/> is about.</summary>
public enum LineKind
{
    /// <summary>A line in [HitObjects].</summary>
    HitObject,

    /// <summary>An inherited timing point (green line).</summary>
    GreenLine,

    /// <summary>Anything else: red lines, metadata, events, colours, comments, the BOM, a changed line ending.</summary>
    Other,
}

/// <summary>One changed line: <see cref="Before"/> is null for an added line, <see cref="After"/> for a removed one.</summary>
public sealed record LineChange(string Section, LineKind Kind, string? Before, string? After);

/// <summary>
/// What a write would change in a .osu, compared byte by byte with the file on disk (ADR 0002): objects changed, green
/// lines added, edited and removed, and every other changed line. A line whose bytes or line ending differ counts as
/// changed. Lines are matched by their exact bytes within each section, so an unedited line never counts; an object or
/// green line that changed at the same time counts as one edit, not a removal plus an addition.
/// </summary>
public sealed record WriteSummary(int ObjectsChanged, int GreenLinesAdded, int GreenLinesEdited, int GreenLinesRemoved, int OtherLines,
    IReadOnlyList<LineChange> Changes)
{
    /// <summary>Nothing would change.</summary>
    public bool IsEmpty => Changes.Count == 0;

    /// <summary>Only hit objects and green lines would change, so a plain save needs no confirmation.</summary>
    public bool OnlyHitsounds => OtherLines == 0;

    /// <summary>"42 objects, 3 green lines added", for a status bar.</summary>
    public string Counts()
    {
        var parts = new List<string>();
        if (ObjectsChanged > 0) parts.Add(Plural(ObjectsChanged, "object"));
        if (GreenLinesAdded > 0) parts.Add(Plural(GreenLinesAdded, "green line") + " added");
        if (GreenLinesEdited > 0) parts.Add(Plural(GreenLinesEdited, "green line") + " edited");
        if (GreenLinesRemoved > 0) parts.Add(Plural(GreenLinesRemoved, "green line") + " removed");
        if (OtherLines > 0) parts.Add(Plural(OtherLines, "other line"));
        return parts.Count == 0 ? "no changes" : string.Join(", ", parts);
    }

    private static string Plural(int n, string what) => n.ToString(CultureInfo.InvariantCulture) + " " + what + (n == 1 ? "" : "s");

    public static WriteSummary Compare(byte[] before, byte[] after) => Compare(OsuFile.Parse(before), OsuFile.Parse(after));

    /// <summary>Compares two parsed files; <paramref name="after"/> may be an edited copy (it is compared as it would be written).</summary>
    public static WriteSummary Compare(OsuFile before, OsuFile after)
    {
        var changes = new List<LineChange>();
        int objects = 0, greenAdded = 0, greenEdited = 0, greenRemoved = 0, other = 0;

        if (before.HasBom != after.HasBom)
        {
            changes.Add(new("", LineKind.Other, before.HasBom ? "(UTF-8 BOM)" : null, after.HasBom ? "(UTF-8 BOM)" : null));
            other++;
        }

        var (removed, added) = Unmatched(before.Lines, after.Lines);
        foreach (var section in removed.Select(l => l.Section).Concat(added.Select(l => l.Section)).Distinct())
        {
            var gone = removed.Where(l => l.Section == section).ToList();
            var come = added.Where(l => l.Section == section).ToList();
            // Pair a removed line with an added one at the same time (objects, timing points) or with the same key (other
            // sections, "Key: value"): that's one edit. What stays unpaired was added or removed.
            var pairs = new List<(OsuLine? Before, OsuLine? After)>();
            foreach (var line in come)
            {
                var key = Key(line);
                int match = key is null ? -1 : gone.FindIndex(g => Key(g) == key);
                if (match >= 0) { pairs.Add((gone[match], line)); gone.RemoveAt(match); }
                else pairs.Add((null, line));
            }

            pairs.AddRange(gone.Select(g => ((OsuLine?)g, (OsuLine?)null)));

            foreach (var (b, a) in pairs)
            {
                var kind = KindOf(section, a ?? b!);
                changes.Add(new(section, kind, b is null ? null : Show(b), a is null ? null : Show(a)));
                switch (kind)
                {
                    case LineKind.HitObject: objects++; break;
                    case LineKind.GreenLine when b is null: greenAdded++; break;
                    case LineKind.GreenLine when a is null: greenRemoved++; break;
                    case LineKind.GreenLine: greenEdited++; break;
                    default: other++; break;
                }
            }
        }

        return new(objects, greenAdded, greenEdited, greenRemoved, other, changes);
    }

    // Lines of one file with no exact (bytes and terminator) twin left in the other, kept in file order.
    private static (List<OsuLine> Removed, List<OsuLine> Added) Unmatched(IReadOnlyList<OsuLine> before, IReadOnlyList<OsuLine> after)
    {
        var left = new Dictionary<string, int>();
        foreach (var line in before) left[Identity(line)] = left.GetValueOrDefault(Identity(line)) + 1;
        var added = new List<OsuLine>();
        foreach (var line in after)
        {
            var id = Identity(line);
            if (left.GetValueOrDefault(id) > 0) left[id]--;
            else added.Add(line);
        }

        var removed = new List<OsuLine>();
        foreach (var line in before.Reverse()) // the last copies of a repeated line are the unmatched ones
        {
            var id = Identity(line);
            if (left.GetValueOrDefault(id) > 0) { removed.Add(line); left[id]--; }
        }

        removed.Reverse();
        return (removed, added);
    }

    private static string Identity(OsuLine line) =>
        line.Section + "\n" + Convert.ToBase64String(line.Content) + "\n" + Convert.ToBase64String(line.Terminator);

    // What pairs a changed line with its earlier version: the time for objects and timing points, the key for "Key: value".
    private static string? Key(OsuLine line)
    {
        var text = line.Text.Trim();
        if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal) || text.StartsWith('[')) return null;
        if (line.Section == "HitObjects") return text.Split(',') is { Length: > 2 } f && OsuNumber.TryParseDouble(f[2], out double t) ? "t" + t : null;
        if (line.Section == "TimingPoints")
            return text.Split(',') is { Length: > 0 } f && OsuNumber.TryParseDouble(f[0], out double t) ? (IsGreen(text) ? "g" : "r") + t : null;
        int colon = text.IndexOf(':');
        return colon > 0 ? "k" + text[..colon].Trim() : null;
    }

    private static LineKind KindOf(string section, OsuLine line) =>
        section == "HitObjects" && Key(line) is not null ? LineKind.HitObject
        : section == "TimingPoints" && IsGreen(line.Text.Trim()) ? LineKind.GreenLine
        : LineKind.Other;

    // An inherited point: the uninherited field (7th) is 0, or, in old files without it, the beat length is negative.
    private static bool IsGreen(string text)
    {
        var f = text.Split(',');
        if (f.Length > 6) return f[6].Trim() == "0";
        return f.Length > 1 && OsuNumber.TryParseDouble(f[1], out double beat) && beat < 0;
    }

    private static string Show(OsuLine line)
    {
        string ending = line.Terminator.Length == 0 ? " (no line ending)"
            : line.Terminator.SequenceEqual("\r\n"u8.ToArray()) ? "" : line.Terminator.SequenceEqual("\n"u8.ToArray()) ? " (LF)" : " (CR)";
        return Encoding.UTF8.GetString(line.Content) + ending;
    }
}

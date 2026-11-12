using System.Text;

namespace OsuHitsound.Format;

/// <summary>
/// A .osu or .osb file held as its original lines. Writing it back without edits gives the same bytes;
/// edits change only the fields that were set, on the lines that hold them.
/// </summary>
public sealed class OsuFile
{
    private readonly List<OsuLine> lines;
    private readonly List<TimingPoint> timingPoints;
    private readonly List<HitObject> hitObjects;

    private OsuFile(bool hasBom, List<OsuLine> lines, List<TimingPoint> timingPoints, List<HitObject> hitObjects)
    {
        HasBom = hasBom;
        this.lines = lines;
        this.timingPoints = timingPoints;
        this.hitObjects = hitObjects;
    }

    public bool HasBom { get; }

    public IReadOnlyList<OsuLine> Lines => lines;

    /// <summary>In file order.</summary>
    public IReadOnlyList<TimingPoint> TimingPoints => timingPoints;

    /// <summary>In file order.</summary>
    public IReadOnlyList<HitObject> HitObjects => hitObjects;

    /// <summary>Inserts a centred circle without rewriting other object lines.</summary>
    public HitObject InsertHitCircle(int time)
    {
        var newline = Newline();
        int header = lines.FindIndex(l => l.Section == "HitObjects" && l.Text.Trim() == "[HitObjects]");
        if (header < 0)
        {
            EnsureNewlineBefore(lines.Count, newline);
            header = lines.Count;
            InsertLine(header, "[HitObjects]", "HitObjects", newline);
        }
        // The first object (in file order) later than the circle; unreadable lines count as earlier, so the circle goes
        // after them. When no object is later (circles added in time order, as Generate does) it is appended without a scan.
        int objectIndex = LatestObjectTime() <= time ? -1 : hitObjects.FindIndex(o => ObjectTime(o) is { } t && t > time);
        if (objectIndex < 0) objectIndex = hitObjects.Count;
        int lineIndex = objectIndex < hitObjects.Count ? lines.IndexOf(hitObjects[objectIndex].Line)
            : hitObjects.Count > 0 ? lines.LastIndexOf(hitObjects[^1].Line) + 1 : header + 1; // the last object is near the end
        EnsureNewlineBefore(lineIndex, newline);
        var line = InsertLine(lineIndex, $"256,192,{time.ToString(System.Globalization.CultureInfo.InvariantCulture)},1,0,0:0:0:0:", "HitObjects", newline);
        var circle = new HitObject(line);
        hitObjects.Insert(objectIndex, circle);
        if (latestObjectTime is { } latest) latestObjectTime = Math.Max(latest, time);
        return circle;
    }

    // The latest readable object time, or -∞ when there is none; computed once, kept up to date by inserts (object times
    // are never edited) and forgotten when an object is removed.
    private double? latestObjectTime;

    private double LatestObjectTime() =>
        latestObjectTime ??= hitObjects.Select(ObjectTime).OfType<double>().DefaultIfEmpty(double.NegativeInfinity).Max();

    // An object's time field (the third), or null when it can't be read. Reads it without splitting the whole line.
    private static double? ObjectTime(HitObject o)
    {
        var text = o.Line.Text.AsSpan();
        for (int field = 0; field < 2; field++)
        {
            int comma = text.IndexOf(',');
            if (comma < 0) return null;
            text = text[(comma + 1)..];
        }
        int end = text.IndexOf(',');
        if (end >= 0) text = text[..end]; // "x,y,time" with nothing after it still has a time
        return double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t) ? t : null;
    }

    /// <summary>Removes exactly this object, preserving other lines and their original bytes.</summary>
    public void RemoveHitObject(HitObject obj)
    {
        if (!hitObjects.Remove(obj)) return;
        lines.Remove(obj.Line);
        removedLines = true;
        latestObjectTime = null; // it may have been the latest
    }

    public bool IsEdited => removedLines || Lines.Any(l => l.IsEdited);

    private bool removedLines;

    /// <summary>Removes a line (e.g. a storyboard Sample line). Timing points and hit objects can't be removed this way.</summary>
    public void RemoveLine(OsuLine line)
    {
        if (line.Section is "TimingPoints" or "HitObjects") throw new InvalidOperationException("Timing points and hit objects aren't removed line by line.");
        if (lines.Remove(line)) removedLines = true;
    }

    /// <summary>
    /// Adds a storyboard sample line (<c>Sample,time,layer,"path",volume</c>) to [Events]: after the last sample line at
    /// or before its time, else after the "//Storyboard Sound Samples" comment, else at the end of [Events].
    /// </summary>
    public OsuLine InsertStoryboardSample(string text, double time)
    {
        int events = lines.FindIndex(l => l.Section == "Events" && l.Text.Trim() == "[Events]");
        if (events < 0) throw new InvalidOperationException("The map has no [Events] section to add storyboard samples to.");
        int last = -1, comment = -1, end = events;
        for (int i = events + 1; i < lines.Count && lines[i].Section == "Events"; i++)
        {
            var t = lines[i].Text.Trim();
            if (t.Length > 0) end = i;
            if (t.StartsWith("//Storyboard Sound Samples", StringComparison.OrdinalIgnoreCase)) comment = i;
            if ((t.StartsWith("Sample,", StringComparison.Ordinal) || t.StartsWith("5,", StringComparison.Ordinal))
                && t.Split(',') is [_, var at, ..] && OsuNumber.TryParseDouble(at, out double existing) && existing <= time)
                last = i;
        }

        int after = last >= 0 ? last : comment >= 0 ? comment : end;
        if (lines[after].Terminator.Length == 0) throw new InvalidOperationException("The file ends without a newline; nothing can be added after it.");
        return InsertLine(after + 1, text, "Events", lines[after].Terminator);
    }

    public static OsuFile Read(string path) => Parse(File.ReadAllBytes(path));

    public static OsuFile Parse(ReadOnlySpan<byte> bytes)
    {
        var bom = Encoding.UTF8.Preamble;
        bool hasBom = bytes.StartsWith(bom);
        if (hasBom) bytes = bytes[bom.Length..];

        var lines = new List<OsuLine>();
        var timingPoints = new List<TimingPoint>();
        var hitObjects = new List<HitObject>();
        string section = "";
        int start = 0;
        while (start < bytes.Length)
        {
            int end = bytes[start..].IndexOfAny((byte)'\r', (byte)'\n');
            int contentEnd = end < 0 ? bytes.Length : start + end;
            int next = contentEnd;
            if (next < bytes.Length && bytes[next] == '\r') next++;
            if (next < bytes.Length && bytes[next] == '\n') next++;

            var content = bytes[start..contentEnd];
            var text = Encoding.UTF8.GetString(content);
            var header = SectionName(text);
            if (header is not null) section = header;

            var line = new OsuLine(content.ToArray(), text, bytes[contentEnd..next].ToArray(), section);
            lines.Add(line);
            if (header is null && IsData(text))
            {
                if (section == "TimingPoints") timingPoints.Add(new TimingPoint(line));
                else if (section == "HitObjects") hitObjects.Add(new HitObject(line));
            }
            start = next;
        }
        return new OsuFile(hasBom, lines, timingPoints, hitObjects);
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        if (HasBom) ms.Write(Encoding.UTF8.Preamble);
        foreach (var line in Lines) line.WriteTo(ms);
        return ms.ToArray();
    }

    private const string BookmarksKey = "Bookmarks:";

    /// <summary>osu!'s editor bookmarks ([Editor] Bookmarks: comma-separated ms), in file order; empty when there are none.</summary>
    public IReadOnlyList<int> Bookmarks
    {
        get
        {
            var line = BookmarksLine();
            if (line is null) return [];
            var list = new List<int>();
            foreach (var part in line.Text.Trim()[BookmarksKey.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (OsuNumber.TryParseInt(part, out int t)) list.Add(t);
            return list;
        }
    }

    /// <summary>
    /// Writes the bookmarks: changes the existing Bookmarks line, or adds one under [Editor], or adds an [Editor]
    /// section before [Metadata] when the map has none. Nothing changes when the times are the same.
    /// </summary>
    public void SetBookmarks(IReadOnlyList<int> times)
    {
        if (Bookmarks.SequenceEqual(times)) return;
        string text = BookmarksKey + " " + string.Join(',', times.Select(t => t.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (BookmarksLine() is { } existing)
        {
            existing.Text = text;
            return;
        }

        int editor = lines.FindIndex(l => l.Section == "Editor" && l.Text.Trim() == "[Editor]");
        var newline = Newline();
        if (editor >= 0)
        {
            EnsureNewlineBefore(editor + 1, newline);
            InsertLine(editor + 1, text, "Editor", lines[editor].Terminator);
            return;
        }

        // No [Editor] section: add one before [Metadata] (osu!'s order), or at the end.
        int metadata = lines.FindIndex(l => l.Text.Trim() == "[Metadata]");
        if (metadata < 0)
        {
            if (lines.Count > 0 && lines[^1].Terminator.Length == 0) throw new InvalidOperationException("The file ends without a newline; nothing can be added after it.");
            metadata = lines.Count;
        }

        InsertLine(metadata, "[Editor]", "Editor", newline);
        InsertLine(metadata + 1, text, "Editor", newline);
        InsertLine(metadata + 2, "", "Editor", newline);
    }

    private OsuLine? BookmarksLine() =>
        lines.FirstOrDefault(l => l.Section == "Editor" && l.Text.TrimStart().StartsWith(BookmarksKey, StringComparison.Ordinal));

    /// <summary>The file's own line ending (the first line's), or "\r\n".</summary>
    private byte[] Newline() => lines.FirstOrDefault(l => l.Terminator.Length > 0)?.Terminator ?? "\r\n"u8.ToArray();

    /// <summary>Gives the line before <paramref name="index"/> a line ending when it is the file's last and has none.</summary>
    private void EnsureNewlineBefore(int index, byte[] newline)
    {
        if (index > 0 && lines[index - 1].Terminator.Length == 0)
        { lines[index - 1].Terminator = newline; removedLines = true; }
    }

    private OsuLine InsertLine(int index, string text, string section, byte[] terminator)
    {
        var line = new OsuLine(Encoding.UTF8.GetBytes(text), text, terminator, section);
        line.MarkInserted();
        lines.Insert(index, line);
        return line;
    }

    /// <summary>
    /// Inserts a timing point line after the last one (in file order) at or before its time, so it wins over points at
    /// the same time (the later line wins). Its line ending copies the neighbouring line's.
    /// </summary>
    public TimingPoint InsertTimingPoint(string text)
    {
        double time = OsuNumber.ParseDouble(text.Split(',')[0]);
        if (timingPoints.Count == 0) throw new InvalidOperationException("The map has no timing points to insert next to.");

        int after = -1;
        for (int i = 0; i < timingPoints.Count; i++)
        {
            try
            {
                if (timingPoints[i].Time <= time) after = i;
            }
            catch (FormatException)
            {
            }
        }

        var neighbour = timingPoints[Math.Max(after, 0)].Line;
        if (after >= 0 && neighbour.Terminator.Length == 0)
            throw new InvalidOperationException("The last timing point ends the file without a newline; nothing can follow it.");
        var terminator = neighbour.Terminator.Length > 0 ? neighbour.Terminator : "\r\n"u8.ToArray();
        var line = InsertLine(lines.IndexOf(neighbour) + (after >= 0 ? 1 : 0), text, "TimingPoints", terminator);
        var point = new TimingPoint(line);
        timingPoints.Insert(after + 1, point);
        return point;
    }

    private static bool IsData(string text)
    {
        var t = text.AsSpan().Trim();
        return t.Length > 0 && !t.StartsWith("//") && t.Contains(',');
    }

    private static string? SectionName(string text)
    {
        var t = text.AsSpan().Trim();
        return t.Length > 2 && t[0] == '[' && t[^1] == ']' ? t[1..^1].ToString() : null;
    }
}

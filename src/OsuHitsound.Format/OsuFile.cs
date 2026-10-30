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

    private OsuFile(bool hasBom, List<OsuLine> lines, List<TimingPoint> timingPoints, List<HitObject> hitObjects)
    {
        HasBom = hasBom;
        this.lines = lines;
        this.timingPoints = timingPoints;
        HitObjects = hitObjects;
    }

    public bool HasBom { get; }

    public IReadOnlyList<OsuLine> Lines => lines;

    /// <summary>In file order.</summary>
    public IReadOnlyList<TimingPoint> TimingPoints => timingPoints;

    /// <summary>In file order.</summary>
    public IReadOnlyList<HitObject> HitObjects { get; }

    public bool IsEdited => Lines.Any(l => l.IsEdited);

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

    public void Write(string path) => File.WriteAllBytes(path, ToBytes());

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
        var line = new OsuLine(Encoding.UTF8.GetBytes(text), text, terminator, "TimingPoints");
        line.MarkInserted();
        int lineIndex = lines.IndexOf(neighbour) + (after >= 0 ? 1 : 0);
        lines.Insert(lineIndex, line);
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

using System.Text;

namespace OsuHitsound.Format;

/// <summary>
/// A .osu or .osb file held as its original lines. Writing it back without edits gives the same bytes;
/// edits change only the fields that were set, on the lines that hold them.
/// </summary>
public sealed class OsuFile
{
    private OsuFile(bool hasBom, List<OsuLine> lines, List<TimingPoint> timingPoints, List<HitObject> hitObjects)
    {
        HasBom = hasBom;
        Lines = lines;
        TimingPoints = timingPoints;
        HitObjects = hitObjects;
    }

    public bool HasBom { get; }

    public IReadOnlyList<OsuLine> Lines { get; }

    /// <summary>In file order.</summary>
    public IReadOnlyList<TimingPoint> TimingPoints { get; }

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

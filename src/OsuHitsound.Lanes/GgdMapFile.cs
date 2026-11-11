namespace OsuHitsound.Lanes;

/// <summary>
/// A GGD drum library's own mapping, as its Mappings page saves it with EXPORT MAP: a Kontakt array file (.nka) whose
/// first line is "%ART__articulation_map" and whose next lines are one MIDI note per articulation (-1 = unmapped).
/// GGD saves these in the library's folder under Documents ("…\Data\*.nka").
/// </summary>
public static class GgdMapFile
{
    public const string Header = "%ART__articulation_map";
    private const int MaxBytes = 65536; // an export is under 1 KB; anything this big isn't one
    private const int LastLine = 37; // the highest articulation line below (the header is line -1)

    /// <summary>
    /// Which line is which articulation, in the order the Mappings page shows them (worked out from an export of a
    /// P4 Kit "Invasion" map against its Mappings page). Lines 0 and 11 are always -1 (11 is likely Pedal Ching).
    /// </summary>
    private static readonly (int Line, string Group, string Item)[] Lines =
    [
        (1, "KICK", "Hit"),
        (4, "SNARE", "Hit"), (3, "SNARE", "Flam"), (6, "SNARE", "Wires Off"), (5, "SNARE", "Ruff"), (2, "SNARE", "Sidestick"),
        (7, "TOMS", "Rack Tom 1: Hit"), (8, "TOMS", "Rack Tom 2: Hit"), (9, "TOMS", "Floor Tom 1: Hit"), (10, "TOMS", "Floor Tom 2: Hit"),
        (37, "AUX", "Stick Click"),
        (19, "HATS", "Tip Tight"), (13, "HATS", "Edge Tight"), (18, "HATS", "Tip Closed"), (12, "HATS", "Edge Closed"),
        (14, "HATS", "Open 1"), (15, "HATS", "Open 2"), (16, "HATS", "Open 3"), (17, "HATS", "Pedal"), (20, "HATS", "CC"),
        (21, "RIDE", "Bell"), (22, "RIDE", "Ride"),
        (30, "CHINA", "Hit"), (29, "CHINA", "Choke"),
        (24, "CRASHES", "Main Crash L: Hit"), (23, "CRASHES", "Main Crash L: Choke"),
        (26, "CRASHES", "Wide Crash L: Hit"), (25, "CRASHES", "Wide Crash L: Choke"),
        (28, "CRASHES", "Main Crash R: Hit"), (27, "CRASHES", "Main Crash R: Choke"),
        (32, "SPLASHES", "Splash L: Hit"), (31, "SPLASHES", "Splash L: Choke"),
        (34, "SPLASHES", "Splash C: Hit"), (33, "SPLASHES", "Splash C: Choke"),
        (35, "MINI HATS", "Hit"),
        (36, "STACK", "Hit"),
    ];

    /// <summary>The kit's articulations from an exported map's text.</summary>
    /// <exception cref="FormatException">Not a GGD articulation map.</exception>
    public static IReadOnlyList<DrumKitArticulation> Parse(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < LastLine + 2 || lines[0] != Header) throw new FormatException("This isn't a GGD map export (EXPORT MAP on the library's Mappings page).");
        var notes = new int?[lines.Length - 1];
        for (int i = 1; i < lines.Length; i++)
        {
            if (!int.TryParse(lines[i], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int note) || note is < -1 or > 127)
                throw new FormatException($"Line {i + 1} of the GGD map isn't a MIDI note.");
            notes[i - 1] = note < 0 ? null : note;
        }
        return [.. Lines.Select(l => new DrumKitArticulation(l.Group, l.Item, notes[l.Line]))];
    }

    /// <summary>GGD map exports in the libraries' folders under <paramref name="documents"/>, newest first.</summary>
    public static IReadOnlyList<(string Library, string Name, string Path)> Find(string documents)
    {
        var found = new List<(string Library, string Name, string Path, DateTime Time)>();
        try
        {
            foreach (var library in Directory.EnumerateDirectories(documents))
            {
                string data = System.IO.Path.Combine(library, "Data");
                if (!Directory.Exists(data)) continue;
                foreach (var file in Directory.EnumerateFiles(data, "*.nka"))
                    if (IsMap(file)) found.Add((System.IO.Path.GetFileName(library), System.IO.Path.GetFileNameWithoutExtension(file), file, File.GetLastWriteTimeUtc(file)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return [.. found.OrderByDescending(f => f.Time).Select(f => (f.Library, f.Name, f.Path))];
    }

    /// <summary>Reads a map file (at most 64 KB).</summary>
    public static IReadOnlyList<DrumKitArticulation> Read(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) throw new FormatException("This file is too large to be a GGD map export.");
        return Parse(File.ReadAllText(path));
    }

    private static bool IsMap(string file)
    {
        try
        {
            if (new FileInfo(file).Length > MaxBytes) return false;
            using var reader = new StreamReader(file);
            return reader.ReadLine()?.Trim() == Header;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}

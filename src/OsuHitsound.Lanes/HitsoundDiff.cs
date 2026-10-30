using System.Globalization;
using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>One generated sample: these lanes mixed at these volumes into a new file in the mapset.</summary>
public sealed record MixFile(string FileName, IReadOnlyList<(Lane Lane, int Volume)> Layers);

/// <summary>
/// What Generate will write: the [Hitsounds] diff (path and contents), the mixes to render first, the circle count,
/// and anything that still can't be saved.
/// </summary>
public sealed record GeneratePlan(string OsuPath, OsuFile Diff, IReadOnlyList<MixFile> Mixes, int Circles, IReadOnlyList<Conflict> Conflicts);

/// <summary>
/// Generate mode: every placed sound becomes a circle at its time in a separate [Hitsounds] difficulty, for Mapping
/// Tools' Hitsound Copier. Circles are encoded by the <see cref="Compiler"/>; a moment whose sounds osu! can't play
/// together (two indexes or volumes, no hitnormal, own files) is mixed into one WAV played as the hitnormal of a fresh
/// index that no difficulty, storyboard or sample file in the folder uses. Identical mixes share one file.
/// </summary>
public static class HitsoundDiff
{
    public const string Version = "Hitsounds";

    public static GeneratePlan Plan(OsuFile source, string sourcePath, MapFolder folder, EditSession session)
    {
        double offset = session.Map.Settings.TimeOffset;

        // 1. Every placed sound, grouped by its time as written (whole ms) with its volume.
        var groups = session.State.Hits
            .GroupBy(h => (int)Math.Round(h.Time - offset))
            .OrderBy(g => g.Key)
            .Select(g => (Time: g.Key, Layers: g.Select(h => (h.Lane, Volume: session.VolumeOf(h))).Distinct().ToList()))
            .ToList();

        // 2. Moments that need a mix: different volumes, own files, or what the compiler can't encode. Compiler.Apply
        //    changes the file it is given, so each pass compiles a fresh parse of the same diff text.
        var diffBytes = Encoding.UTF8.GetBytes(DiffText(source, groups.Select(g => g.Time)));
        var direct = OsuFile.Parse(diffBytes);
        var first = Compiler.Apply(direct, folder, Wishes(direct, folder, groups.Select(g => Wish(g.Layers)).ToList()));
        var needsMix = first.Conflicts.Select(c => c.Moment.ObjectIndex).ToHashSet();
        for (int i = 0; i < groups.Count; i++)
            if (groups[i].Layers.Select(l => l.Volume).Distinct().Count() > 1) needsMix.Add(i);

        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sourcePath))!, FileNameFor(source, sourcePath));
        if (needsMix.Count == 0) return new GeneratePlan(path, direct, [], groups.Count, first.Conflicts);

        // 3. Mix files on fresh indexes; identical layer sets share one. Volumes below stable's 8 % play at 8 %.
        int next = FirstFreeIndex(folder);
        var mixes = new Dictionary<string, MixFile>();
        var wishes = new List<WantedSound>();
        for (int i = 0; i < groups.Count; i++)
        {
            var layers = groups[i].Layers.Where(l => !l.Lane.Missing)
                .Select(l => (l.Lane, Volume: Math.Max(l.Volume, HitsoundRules.StableMinimumVolume)))
                .OrderBy(l => ProjectStore.KeyOf(l.Lane)).ToList();
            if (!needsMix.Contains(i) || layers.Count == 0)
            {
                wishes.Add(Wish(groups[i].Layers));
                continue;
            }

            string signature = string.Join(';', layers.Select(l => $"{ProjectStore.KeyOf(l.Lane)}@{l.Volume}"));
            if (!mixes.TryGetValue(signature, out var mix))
                mixes[signature] = mix = new MixFile($"soft-hitnormal{next++}.wav", layers);
            wishes.Add(new WantedSound([new Lane(LaneSource.BeatmapFile, mix.FileName)], 100));
        }

        // 4. The final diff, compiled against the folder as it will be (with the mixes in it).
        var withMixes = new MapFolder(folder.Files.Concat(mixes.Values.Select(m => m.FileName)));
        var final = OsuFile.Parse(diffBytes);
        var result = Compiler.Apply(final, withMixes, Wishes(final, withMixes, wishes));
        return new GeneratePlan(path, final, [.. mixes.Values], groups.Count, result.Conflicts);
    }

    private static WantedSound Wish(List<(Lane Lane, int Volume)> layers) =>
        new([.. layers.Select(l => l.Lane)], layers.Count > 0 ? layers[0].Volume : 100);

    /// <summary>Wishes keyed by circle: the diff has one circle per group, in the same order.</summary>
    private static Dictionary<MomentKey, WantedSound> Wishes(OsuFile diff, MapFolder folder, IReadOnlyList<WantedSound> wishes) =>
        Decompiler.Decompile(diff, folder).Moments.ToDictionary(m => Compiler.KeyOf(diff, m), m => wishes[Compiler.KeyOf(diff, m).ObjectIndex]);

    /// <summary>
    /// The diff: the source's header (Version set to "Hitsounds", BeatmapID 0), no events, its red lines, and one plain
    /// circle per time. Line endings follow the source.
    /// </summary>
    public static string DiffText(OsuFile source, IEnumerable<int> times)
    {
        var sb = new StringBuilder();
        const string nl = "\r\n";
        string section = "";
        foreach (var line in source.Lines)
        {
            var text = line.Text.Trim();
            if (text.StartsWith('[') && text.EndsWith(']')) section = text[1..^1];
            if (section is "Events" or "TimingPoints" or "HitObjects" or "Colours") continue;
            if (section == "Metadata" && text.StartsWith("Version:", StringComparison.Ordinal)) text = "Version:" + Version;
            else if (section == "Metadata" && text.StartsWith("BeatmapID:", StringComparison.Ordinal)) text = "BeatmapID:0";
            else text = line.Text;
            sb.Append(text).Append(nl);
        }

        sb.Append("[Events]").Append(nl).Append(nl);
        sb.Append("[TimingPoints]").Append(nl);
        foreach (var p in source.TimingPoints)
        {
            try
            {
                if (p.Uninherited) sb.Append(p.Line.Text).Append(nl);
            }
            catch (FormatException)
            {
            }
        }

        sb.Append(nl).Append("[HitObjects]").Append(nl);
        foreach (int t in times)
            sb.Append("256,192,").Append(t.ToString(CultureInfo.InvariantCulture)).Append(",1,0,0:0:0:0:").Append(nl);
        return sb.ToString();
    }

    /// <summary>"Artist - Title (Creator) [Hitsounds].osu", from the source's metadata (or its file name).</summary>
    public static string FileNameFor(OsuFile source, string sourcePath)
    {
        string Meta(string key) => source.Lines.FirstOrDefault(l => l.Section == "Metadata" && l.Text.StartsWith(key + ":", StringComparison.Ordinal))
            ?.Text[(key.Length + 1)..].Trim() ?? "";
        string name = Meta("Artist") is { Length: > 0 } artist && Meta("Title") is { Length: > 0 } title
            ? $"{artist} - {title} ({Meta("Creator")}) [{Version}].osu"
            : System.Text.RegularExpressions.Regex.Replace(Path.GetFileName(sourcePath), @"\[[^\]]*\]\.osu$", $"[{Version}].osu");
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    /// <summary>
    /// The lowest index above everything the folder already uses: in sample file names, and in every .osu and .osb
    /// (timing points and hit samples), so a new file can't change how another difficulty sounds. A difficulty that can't be
    /// read stops it (IOException) rather than being skipped.
    /// </summary>
    public static int FirstFreeIndex(MapFolder folder)
    {
        int max = 1;
        foreach (var name in folder.Files)
        {
            var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(name), @"^(normal|soft|drum)-[a-z]+(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[2].Value, out int n)) max = Math.Max(max, n);
        }

        foreach (var name in folder.Files.Where(f => f.EndsWith(".osu", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".osb", StringComparison.OrdinalIgnoreCase)))
        {
            OsuFile file;
            try
            {
                file = OsuFile.Parse(folder.ReadBytes(name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // Fail closed: an index this file uses but we couldn't see could make a new mix change how it sounds.
                throw new IOException($"Couldn't read {name} to check which sample indexes it uses: {ex.Message}", ex);
            }

            foreach (var p in file.TimingPoints)
                try { max = Math.Max(max, p.SampleIndex ?? 0); } catch (FormatException) { }
            foreach (var o in file.HitObjects)
                try { max = Math.Max(max, o.HitSample?.Index ?? 0); } catch (Exception e) when (e is FormatException or InvalidOperationException) { }
        }

        return max + 1;
    }
}

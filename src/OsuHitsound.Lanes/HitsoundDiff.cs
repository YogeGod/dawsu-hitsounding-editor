using System.Globalization;
using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>One generated sample: these lanes mixed at these volumes into a new file in the mapset.</summary>
public sealed record MixFile(string FileName, IReadOnlyList<(Lane Lane, int Volume)> Layers)
{
    public IReadOnlyList<(Lane Lane, int Volume, double Duration)> Loops { get; init; } = [];
    /// <summary>What the mix contains, as text: equal signatures make equal files.</summary>
    public string Signature => SignatureOf(Layers, Loops);

    public static string SignatureOf(IEnumerable<(Lane Lane, int Volume)> layers, IEnumerable<(Lane Lane, int Volume, double Duration)>? loops = null)
    {
        var single = string.Join(';', layers.Select(l => $"{ProjectStore.KeyOf(l.Lane)}@{l.Volume}"));
        var repeated = string.Join(';', (loops ?? []).Select(l => $"{ProjectStore.KeyOf(l.Lane)}@{l.Volume}@{l.Duration.ToString("R", CultureInfo.InvariantCulture)}"));
        return repeated.Length == 0 ? single : single + "|loops:" + repeated;
    }
}

/// <summary>
/// What Generate will write: the [Hitsounds] diff (path and contents), the mixes to render first, the circle count,
/// and anything that still can't be saved.
/// </summary>
public sealed record GeneratePlan(string OsuPath, OsuFile Diff, IReadOnlyList<MixFile> Mixes, int Circles, IReadOnlyList<Conflict> Conflicts)
{
    public int LastMixIndex { get; init; }
    public IReadOnlyDictionary<string, string?> InputHashes { get; init; } = new Dictionary<string, string?>();

    /// <summary>Mixes from the previous Generate that are used again as they are (not rewritten).</summary>
    public IReadOnlyList<GeneratedMix> Reused { get; init; } = [];

    /// <summary>The previous Generate's mixes that nothing will use any more: removed after the diff is written (or overwritten by a new mix of the same name).</summary>
    public IReadOnlyList<string> Removes { get; init; } = [];
}

/// <summary>
/// Generate mode: every placed sound becomes a circle at its time in a separate [Hitsounds] difficulty, for Mapping
/// Tools' Hitsound Copier. Circles are encoded by the <see cref="Compiler"/>; a moment whose sounds osu! can't play
/// together (two indexes or volumes, no hitnormal, own files) is mixed into one WAV played as the hitnormal of a fresh
/// index that no difficulty, storyboard or sample file in the folder uses. Identical mixes share one file.
/// </summary>
public static class HitsoundDiff
{
    public const string Version = "Hitsounds";
    internal const string GeneratedIndexMarker = "//dawsu-generated-index:";

    public static GeneratePlan Plan(OsuFile source, string sourcePath, MapFolder folder, EditSession session, GenerateManifest? previous = null,
        IReadOnlyDictionary<string, byte[]>? skin = null)
    {
        double offset = session.Map.Settings.TimeOffset;

        var silentLoops = new Dictionary<Lane, bool>();
        bool IsEmptyLoop(Lane lane)
        {
            if (silentLoops.TryGetValue(lane, out bool empty)) return empty;
            byte[]? bytes = null;
            try
            {
                if (lane.Source == LaneSource.Skin) bytes = skin?.GetValueOrDefault(lane.Name);
                else if (folder.Find(lane.Name) is { } name) bytes = folder.ReadBytes(name);
            }
            catch (InvalidOperationException) { } // A name-only folder cannot establish that a sample is silent.
            return silentLoops[lane] = bytes is not null && BlankSample.IsEmptyWav(bytes);
        }

        // 1. Every placed sound, grouped by its time as written (whole ms) with its volume.
        var placed = session.State.Hits.Select(h => (h.Time, h.Lane, Volume: session.VolumeOf(h), Duration: 0.0));
        var passive = session.Map.Passive.Where(p => p.Kind is PassiveKind.SliderTick or PassiveKind.SliderSlide or PassiveKind.SliderWhistle)
            .Where(p => p.Kind == PassiveKind.SliderTick || !IsEmptyLoop(p.Lane))
            .Select(p => (p.Time, p.Lane, p.Volume, Duration: Math.Max(0, p.EndTime - p.Time)));
        var groups = placed.Concat(passive)
            .GroupBy(h => (int)Math.Round(h.Time - offset))
            .OrderBy(g => g.Key)
            .Select(g => (Time: g.Key, Layers: g.Where(h => h.Duration == 0).Select(h => (h.Lane, h.Volume)).ToList(),
                Loops: g.Where(h => h.Duration > 0).Select(h => (h.Lane, h.Volume, h.Duration)).ToList()))
            .ToList();

        // 2. Moments that need a mix: different volumes, own files, or what the compiler can't encode. Compiler.Apply
        //    changes the file it is given, so each pass compiles a fresh parse of the same diff text.
        var diffBytes = Encoding.UTF8.GetBytes(DiffText(source, groups.Select(g => g.Time)));
        var direct = OsuFile.Parse(diffBytes);
        var first = Compiler.Apply(direct, folder, Wishes(direct, folder, groups.Select(g => Wish(g.Layers)).ToList()));
        var needsMix = first.Conflicts.Select(c => c.Moment.ObjectIndex).ToHashSet();
        for (int i = 0; i < groups.Count; i++)
            if (groups[i].Loops.Count > 0 || groups[i].Layers.Any(l => l.Lane.Missing)
                || groups[i].Layers.Select(l => l.Volume).Distinct().Count() > 1
                || groups[i].Layers.Select(l => l.Lane).Distinct().Count() != groups[i].Layers.Count) needsMix.Add(i);

        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sourcePath))!, FileNameFor(source, sourcePath));

        // 3. Mix files ("fusing") for the moments that need one; see MixPlanner for reuse and fresh indexes.
        var layered = groups.Select(g => MixPlanner.Normalize(g.Layers, includeMissing: true)).ToList();
        var looped = groups.Select(g => g.Loops.Select(l => (l.Lane, Volume: Math.Max(l.Volume, HitsoundRules.StableMinimumVolume), l.Duration)).ToList()).ToList();
        var mixer = new MixPlanner(folder, previous, Path.GetFileName(path), needsMix.Select(i => layered[i]), skin,
            needsMix.Where(i => looped[i].Count > 0).Select(i => ((IReadOnlyList<(Lane Lane, int Volume)>)layered[i], (IReadOnlyList<(Lane Lane, int Volume, double Duration)>)looped[i])),
            groups.SelectMany(g => g.Layers.Select(l => l.Lane).Concat(g.Loops.Select(l => l.Lane))));
        var wishes = new List<WantedSound>();
        for (int i = 0; i < groups.Count; i++)
            wishes.Add(needsMix.Contains(i) && (layered[i].Count > 0 || looped[i].Count > 0)
                ? new WantedSound([new Lane(LaneSource.BeatmapFile, mixer.MixFor(layered[i], looped[i]).FileName)], 100)
                : Wish(groups[i].Layers));

        // 4. The final diff, compiled against the folder as it will be (with the mixes in it).
        var withMixes = folder.With(mixer.All.Select(m => m.FileName)); // keeps reading the folder's files (its .osb)
        var final = OsuFile.Parse(Encoding.UTF8.GetBytes(DiffText(source, groups.Select(g => g.Time), lastGeneratedIndex: mixer.LastIndex)));
        var result = Compiler.Apply(final, withMixes, Wishes(final, withMixes, wishes));
        return new GeneratePlan(path, final, mixer.ToWrite, groups.Count, result.Conflicts) { Reused = mixer.Reused, Removes = mixer.Removes, LastMixIndex = mixer.LastIndex, InputHashes = mixer.InputHashes };
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
    public static string DiffText(OsuFile source, IEnumerable<int> times, string version = Version, int? lastGeneratedIndex = null)
    {
        var sb = new StringBuilder();
        const string nl = "\r\n";
        string section = "";
        foreach (var line in source.Lines)
        {
            var text = line.Text.Trim();
            if (text.StartsWith('[') && text.EndsWith(']')) section = text[1..^1];
            if (section is "Events" or "TimingPoints" or "HitObjects" or "Colours") continue;
            if (section == "Metadata" && text.StartsWith("Version:", StringComparison.Ordinal)) text = "Version:" + version;
            else if (section == "Metadata" && text.StartsWith("BeatmapID:", StringComparison.Ordinal)) text = "BeatmapID:0";
            else text = line.Text;
            sb.Append(text).Append(nl);
        }

        sb.Append("[Events]").Append(nl);
        if (lastGeneratedIndex is { } last) sb.Append(GeneratedIndexMarker).Append(last.ToString(CultureInfo.InvariantCulture)).Append(nl);
        sb.Append(nl);
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

    /// <summary>
    /// File → New hitsound diff (Ctrl+N): an empty difficulty next to the source, made like Generate's (same header with
    /// this Version and BeatmapID 0, red lines only, no events or objects; bookmarks kept). Refuses a blank name, and a
    /// name or file another difficulty in the folder already has (osu! compares names ignoring case). Returns its path.
    /// </summary>
    public static string CreateEmpty(OsuFile source, string sourcePath, string version)
    {
        version = version.Trim();
        if (version.Length == 0) throw new ArgumentException("The difficulty needs a name.", nameof(version));
        var folder = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var path = Path.Combine(folder, FileNameFor(source, sourcePath, version));
        foreach (var other in Directory.EnumerateFiles(folder, "*.osu"))
        {
            if (string.Equals(other, path, StringComparison.OrdinalIgnoreCase)) throw new IOException($"{Path.GetFileName(path)} already exists.");
            if (VersionOf(other) is { } taken && string.Equals(taken, version, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"The difficulty \"{taken}\" already exists in this folder.");
        }

        // CreateNew: if another program made the file since the check above, this fails instead of replacing it.
        var bytes = Encoding.UTF8.GetBytes(DiffText(source, [], version));
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            stream.Write(bytes);
        }
        catch (Exception ex) when (ex is not IOException { HResult: unchecked((int)0x80070050) }) // 0x50: the file exists (not ours)
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }

        return path;
    }

    /// <summary>A .osu's difficulty name ([Metadata] Version), or null when it can't be read.</summary>
    public static string? VersionOf(string osuPath)
    {
        try
        {
            return OsuFile.Read(osuPath).Lines.FirstOrDefault(l => l.Section == "Metadata" && l.Text.StartsWith("Version:", StringComparison.Ordinal))?.Text[8..].Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>"Artist - Title (Creator) [Hitsounds].osu", from the source's metadata (or its file name).</summary>
    public static string FileNameFor(OsuFile source, string sourcePath, string version = Version)
    {
        string Meta(string key) => source.Lines.FirstOrDefault(l => l.Section == "Metadata" && l.Text.StartsWith(key + ":", StringComparison.Ordinal))
            ?.Text[(key.Length + 1)..].Trim() ?? "";
        string name = Meta("Artist") is { Length: > 0 } artist && Meta("Title") is { Length: > 0 } title
            ? $"{artist} - {title} ({Meta("Creator")}) [{version}].osu"
            : System.Text.RegularExpressions.Regex.Replace(Path.GetFileName(sourcePath), @"\[[^\]]*\]\.osu$", $"[{version}].osu");
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    /// <summary>
    /// The lowest index above everything the folder already uses: in sample file names, and in every .osu and .osb
    /// (timing points and hit samples), so a new file can't change how another difficulty sounds. A difficulty that can't be
    /// read stops it (IOException) rather than being skipped.
    /// </summary>
    public static int FirstFreeIndex(MapFolder folder) => Math.Max(MaxFileIndex(folder.Files), IndexesUsed(folder, except: null).DefaultIfEmpty(1).Max()) + 1;

    /// <summary>The index in a sample file name ("soft-hitnormal7.wav" → 7), or null.</summary>
    public static int? IndexOf(string fileName) => HitsoundRules.ParseSampleName(Path.GetFileNameWithoutExtension(fileName))?.Index;

    internal static int MaxFileIndex(IEnumerable<string> files) => files.Select(IndexOf).OfType<int>().DefaultIfEmpty(1).Max();

    /// <summary>
    /// Every sample index the folder's .osu and .osb files use (timing points and hit samples), except in the file named
    /// <paramref name="except"/>. Fails closed: a file that can't be read throws IOException.
    /// </summary>
    internal static HashSet<int> IndexesUsed(MapFolder folder, string? except)
    {
        var used = new HashSet<int>();
        foreach (var name in folder.Files.Where(f => (f.EndsWith(".osu", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".osb", StringComparison.OrdinalIgnoreCase))
                                                     && !string.Equals(f, except, StringComparison.OrdinalIgnoreCase)))
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
                try { if (p.SampleIndex is > 0 and var i) used.Add(i); } catch (FormatException) { }
            foreach (var o in file.HitObjects)
                try
                {
                    if (o.HitSample is { } sample)
                    {
                        if (sample.Index > 0) used.Add(sample.Index);
                        if (sample.Filename is { } filename && IndexOf(filename) is { } namedIndex) used.Add(namedIndex);
                    }
                }
                catch (Exception e) when (e is FormatException or InvalidOperationException) { }

            var warnings = new List<string>();
            foreach (var (_, path, _) in Storyboard.Samples(file, warnings))
                if (IndexOf(path) is { } namedIndex) used.Add(namedIndex);
            if (warnings.Count > 0) throw new IOException($"Couldn't inspect storyboard samples in {name}: {warnings[0]}");
        }

        return used;
    }
}

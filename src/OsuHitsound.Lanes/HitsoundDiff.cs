using System.Globalization;
using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>One generated sample: these lanes mixed at these volumes into a new file in the mapset.</summary>
public sealed record MixFile(string FileName, IReadOnlyList<(Lane Lane, int Volume)> Layers)
{
    public byte[]? CopyBytes { get; init; }
    public string? PairSignature { get; init; }
    public IReadOnlyList<(Lane Lane, int Volume, double Duration)> Loops { get; init; } = [];
    public string? DestinationSlot { get; init; }
    public string? SilenceFor { get; init; }
    /// <summary>What the mix contains, as text: equal signatures make equal files.</summary>
    public string Signature => PairSignature ?? (SilenceFor is { } parent ? parent + "|silent-normal" :
        SignatureOf(Layers, Loops) + SlotSuffix(DestinationSlot));

    internal static string SlotSuffix(string? slot) => slot is null or "soft-hitnormal" ? "" : "|slot:" + slot;

    internal WantedSound Wanted()
    {
        var lanes = new List<Lane> { new(LaneSource.BeatmapFile, FileName) };
        if (DestinationSlot is { } slot && !slot.EndsWith("-hitnormal", StringComparison.Ordinal))
        {
            var sample = HitsoundRules.ParseSampleName(Path.GetFileNameWithoutExtension(FileName))!.Value;
            lanes.Add(new(LaneSource.BeatmapFile, $"{HitsoundRules.SampleName(sample.Set, "hitnormal")}{sample.Index}.wav"));
        }
        return new(lanes, 100);
    }

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
    public IReadOnlyList<string> Notes { get; init; } = [];
    /// <summary>Mapset files the plan read (name on disk → SHA-256), checked unchanged before anything is written.</summary>
    public IReadOnlyDictionary<string, string> InputFiles { get; init; } = new Dictionary<string, string>();
    public string? ExpectedOutputHash { get; init; }
    public IReadOnlyList<MergeCombination> MergeCombinations { get; init; } = [];
    public int LastMixIndex { get; init; }
    public IReadOnlyDictionary<string, string?> InputHashes { get; init; } = new Dictionary<string, string?>();

    /// <summary>Mixes from the previous Generate that are used again as they are (not rewritten).</summary>
    public IReadOnlyList<GeneratedMix> Reused { get; init; } = [];

    /// <summary>The previous Generate's mixes that nothing will use any more: removed after the diff is written (or overwritten by a new mix of the same name).</summary>
    public IReadOnlyList<string> Removes { get; init; } = [];
}

/// <summary>
/// Generate mode: hits at circles, slider heads/repeats/tails and spinner ends become circles in a separate
/// [Hitsounds] difficulty. Slider body/tick audio is opt-in. The compiler encodes compatible hits directly;
/// pairing mode prepares incompatible combinations without changing their roles, fusing finishes into finish.
/// Legacy non-pairing mode uses mixed WAVs. Identical combinations share prepared samples.
/// </summary>
public static class HitsoundDiff
{
    public const string Version = "Hitsounds";
    internal const string GeneratedIndexMarker = "//dawsu-generated-index:";

    public static GeneratePlan Plan(OsuFile source, string sourcePath, MapFolder folder, EditSession session, GenerateManifest? previous = null,
        IReadOnlyDictionary<string, byte[]>? skin = null, ExportMergeSettings? mergeSettings = null, bool includeSliderAudio = false) =>
        Plan(source, sourcePath, new LaneAudio(folder, skin), session, previous, mergeSettings, includeSliderAudio);

    /// <summary>Plans with the job's lane audio, which the caller keeps to render the mixes.</summary>
    internal static GeneratePlan Plan(OsuFile source, string sourcePath, LaneAudio audio, EditSession session, GenerateManifest? previous,
        ExportMergeSettings? mergeSettings, bool includeSliderAudio)
    {
        var folder = audio.Folder;
        mergeSettings?.Validate();
        double offset = session.Map.Settings.TimeOffset;
        var setGainLanes = session.State.Hits.Where(h => h.SetPlacement != Guid.Empty).ToLookup(h => (int)Math.Round(h.Time - offset), h => h.Lane);

        bool IsEmptyLoop(Lane lane) => audio.TryRead(lane) is { } bytes && BlankSample.IsEmptyWav(bytes);

        // 1. Every placed sound, grouped by its time as written (whole ms) with its volume.
        var placed = session.State.Hits.Select(h => (h.Time, h.Lane, Volume: session.VolumeOf(h), Duration: 0.0,
            Slider: h.Lane.Sample?.Sound is "slidertick" or "sliderslide" or "sliderwhistle"))
            .Where(h => includeSliderAudio || !h.Slider);
        var passive = session.Map.Passive.Where(p => includeSliderAudio && p.Kind is (PassiveKind.SliderTick or PassiveKind.SliderSlide or PassiveKind.SliderWhistle))
            .Where(p => p.Kind == PassiveKind.SliderTick || !IsEmptyLoop(p.Lane))
            .Select(p => (p.Time, p.Lane, p.Volume, Duration: Math.Max(0, p.EndTime - p.Time), Slider: true));
        var groups = placed.Concat(passive)
            .GroupBy(h => (Time: (int)Math.Round(h.Time - offset), h.Slider))
            .OrderBy(g => g.Key.Time).ThenByDescending(g => g.Key.Slider)
            .Select(g => (g.Key.Time, g.Key.Slider, Layers: g.Where(h => h.Duration == 0).Select(h => (h.Lane, h.Volume)).ToList(),
                Loops: g.Where(h => h.Duration > 0).Select(h => (h.Lane, h.Volume, h.Duration)).ToList()))
            .ToList();

        // 2. Moments that need a mix: different volumes, own files, or what the compiler can't encode. Compiler.Apply
        //    changes the file it is given, so each pass compiles a fresh parse of the same diff text.
        var diffBytes = DiffBytes(source, groups.Select(g => g.Time));
        var direct = OsuFile.Parse(diffBytes);
        var first = Compiler.Apply(direct, folder, Wishes(direct, folder, groups.Select(g => Wish(g.Layers)).ToList()));
        var needsMix = first.Conflicts.Select(c => c.Moment.ObjectIndex).ToHashSet();
        for (int i = 0; i < groups.Count; i++)
            if (groups[i].Loops.Count > 0 || groups[i].Layers.Any(l => l.Lane.Missing)
                || groups[i].Layers.Select(l => l.Volume).Distinct().Count() > 1
                || groups[i].Layers.Select(l => l.Lane).Distinct().Count() != groups[i].Layers.Count) needsMix.Add(i);

        string Fingerprint(Lane lane) => audio.TryHash(lane) ?? "unknown";
        var combinations = groups.Select(g => new MergeCombination(g.Layers, g.Loops)).ToList();
        var candidates = Enumerable.Range(0, groups.Count)
            .Where(i => !groups[i].Slider && (combinations[i].AdditionSlots.Count() >= 2 ||
                needsMix.Contains(i) && groups[i].Layers.Count + groups[i].Loops.Count > 1)).ToList();
        foreach (int i in candidates)
            combinations[i] = combinations[i] with { Fingerprints = combinations[i].Layers.Select(l => l.Lane).Concat(combinations[i].Loops.Select(l => l.Lane))
                .Distinct().ToDictionary(l => l, Fingerprint) };
        var destinations = new Dictionary<string, string>();
        if (mergeSettings is not null)
            foreach (int i in candidates)
            {
                var slot = mergeSettings.Destination(combinations[i]) ??
                    throw new InvalidOperationException("Choose a destination for every new layer combination before generating.");
                destinations[MixFile.SignatureOf(MixPlanner.Normalize(groups[i].Layers, includeMissing: true),
                    groups[i].Loops.Select(l => (l.Lane, Math.Max(l.Volume, HitsoundRules.StableMinimumVolume), l.Duration)))] = slot;
                needsMix.Add(i);
            }

        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sourcePath))!, FileNameFor(source, sourcePath));

        if (mergeSettings?.PairSamples == true)
        {
            // Passive slider samples retain their timed rendering path, outside ordinary pairing/fusion.
            var passiveMixer = new MixPlanner(audio, previous, Path.GetFileName(path), [],
                groups.Where(g => g.Slider).Select(g => ((IReadOnlyList<(Lane Lane, int Volume)>)MixPlanner.Normalize(g.Layers, true),
                    (IReadOnlyList<(Lane Lane, int Volume, double Duration)>)g.Loops)));
            var passiveWishes = groups.Select(g => g.Slider ? passiveMixer.MixFor(MixPlanner.Normalize(g.Layers, true), g.Loops).Wanted() : null).ToList();
            var pairing = new PairingPlanner(audio.With(passiveMixer.All.Select(m => m.FileName)), mergeSettings, previous);
            var pairWishes = groups.Select((g, i) => passiveWishes[i] ?? pairing.For(g.Layers, g.Time + offset, new(i, 0),
                alreadyEncoded: !needsMix.Contains(i), exactGainLanes: setGainLanes[g.Time].ToHashSet())).ToList();
            // Passive names may share indexes with paired outputs only if no filenames collide.
            if (pairing.Files.Select(m => m.FileName).Intersect(passiveMixer.All.Select(m => m.FileName), StringComparer.OrdinalIgnoreCase).Any())
                throw new IOException("A slider rendering conflicts with a paired sample filename. No files were written.");
            var all = pairing.Files.Concat(passiveMixer.All).ToList();
            var finalPair = OsuFile.Parse(diffBytes);
            var pairedFolder = folder.With(all.Select(m => m.FileName).Concat(pairing.Reused.Select(m => m.FileName)));
            var compiled = Compiler.Apply(finalPair, pairedFolder, Wishes(finalPair, pairedFolder, pairWishes), mergeSettings.Style);
            return new GeneratePlan(path, finalPair, pairing.Files.Concat(passiveMixer.ToWrite).ToList(), groups.Count, pairing.Conflicts.Concat(compiled.Conflicts).DistinctBy(c => c.Moment).ToList())
            {
                Reused = pairing.Reused.Concat(passiveMixer.Reused).ToList(), LastMixIndex = Math.Max(pairing.LastIndex, passiveMixer.LastIndex),
                InputHashes = pairing.InputHashes.Concat(passiveMixer.InputHashes).ToDictionary(p => p.Key, p => p.Value),
                MergeCombinations = pairing.Combinations, Notes = compiled.Notes,
                InputFiles = audio.FilesRead,
                ExpectedOutputHash = folder.Find(Path.GetFileName(path)) is { } oldOutput ? GenerateManifest.Hash(folder.ReadBytes(oldOutput)) : "missing",
            };
        }

        // 3. Mix files ("fusing") for the moments that need one; see MixPlanner for reuse and fresh indexes.
        var layered = groups.Select(g => MixPlanner.Normalize(g.Layers, includeMissing: true)).ToList();
        var looped = groups.Select(g => g.Loops.Select(l => (l.Lane, Volume: Math.Max(l.Volume, HitsoundRules.StableMinimumVolume), l.Duration)).ToList()).ToList();
        var mixer = new MixPlanner(audio, previous, Path.GetFileName(path), needsMix.Select(i => layered[i]),
            needsMix.Where(i => looped[i].Count > 0).Select(i => ((IReadOnlyList<(Lane Lane, int Volume)>)layered[i], (IReadOnlyList<(Lane Lane, int Volume, double Duration)>)looped[i])),
            groups.SelectMany(g => g.Layers.Select(l => l.Lane).Concat(g.Loops.Select(l => l.Lane))), destinations);
        var wishes = new List<WantedSound>();
        for (int i = 0; i < groups.Count; i++)
        {
            if (!needsMix.Contains(i) || layered[i].Count + looped[i].Count == 0) { wishes.Add(Wish(groups[i].Layers)); continue; }
            wishes.Add(mixer.MixFor(layered[i], looped[i]).Wanted());
        }

        // 4. The final diff, compiled against the folder as it will be (with the mixes in it).
        var withMixes = folder.With(mixer.All.Select(m => m.FileName)); // keeps reading the folder's files (its .osb)
        var final = OsuFile.Parse(DiffBytes(source, groups.Select(g => g.Time), lastGeneratedIndex: mixer.LastIndex));
        var result = Compiler.Apply(final, withMixes, Wishes(final, withMixes, wishes));
        return new GeneratePlan(path, final, mixer.ToWrite, groups.Count, result.Conflicts) { Reused = mixer.Reused, Removes = mixer.Removes, LastMixIndex = mixer.LastIndex, InputHashes = mixer.InputHashes,
            InputFiles = audio.FilesRead,
            MergeCombinations = candidates.Select(i => combinations[i]).DistinctBy(c => c.Key).ToList() };
    }

    private static WantedSound Wish(List<(Lane Lane, int Volume)> layers) =>
        new([.. layers.Select(l => l.Lane)], layers.Count > 0 ? layers[0].Volume : 100);

    /// <summary>Wishes keyed by circle: the diff has one circle per group, in the same order.</summary>
    private static Dictionary<MomentKey, WantedSound> Wishes(OsuFile diff, MapFolder folder, IReadOnlyList<WantedSound> wishes) =>
        Decompiler.Decompile(diff, folder).Moments.ToDictionary(m => Compiler.KeyOf(diff, m), m => wishes[Compiler.KeyOf(diff, m).ObjectIndex]);

    /// <summary>
    /// The diff: the source's bytes with only these changes (ADR 0002): Version set, BeatmapID 0, the generated-index
    /// marker at the top of [Events], and one plain circle per time in place of the source's objects. Every other line
    /// (green lines, events, storyboard, colours, unknown sections) keeps its bytes and line ending, and the BOM stays.
    /// </summary>
    internal static byte[] DiffBytes(OsuFile source, IEnumerable<int> times, string version = Version, int? lastGeneratedIndex = null)
    {
        var diff = OsuFile.Parse(source.ToBytes()); // a copy: the open map is never changed
        foreach (var line in diff.Lines.Where(l => l.Section == "Metadata"))
        {
            var text = line.Text.TrimStart();
            if (text.StartsWith("Version:", StringComparison.Ordinal)) line.Text = "Version:" + version;
            else if (text.StartsWith("BeatmapID:", StringComparison.Ordinal)) line.Text = "BeatmapID:0";
        }

        foreach (var marker in diff.Lines.Where(l => l.Section == "Events" && l.Text.Trim().StartsWith(GeneratedIndexMarker, StringComparison.Ordinal)).ToList())
            diff.RemoveLine(marker);
        if (lastGeneratedIndex is { } last)
            diff.InsertAfterHeader("Events", GeneratedIndexMarker + last.ToString(CultureInfo.InvariantCulture), beforeSection: "TimingPoints");

        foreach (var obj in diff.HitObjects.ToList()) diff.RemoveHitObject(obj);
        foreach (int t in times) diff.InsertHitCircle(t);
        return diff.ToBytes();
    }

    /// <summary>
    /// File → New hitsound diff (Ctrl+N): an empty difficulty next to the source, made like Generate's (the source's
    /// bytes with this Version, BeatmapID 0 and no objects; everything else kept, see DiffBytes). Refuses a blank name, and a
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
        var bytes = DiffBytes(source, [], version);
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

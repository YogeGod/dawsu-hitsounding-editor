namespace OsuHitsound.Lanes;

/// <summary>
/// Hands out file names for mixed samples ("fusing"): identical layer sets share one file; a mix the previous run
/// already wrote with the same contents keeps its file and index; new ones continue after the last generated index,
/// skipping occupied indexes without jumping past unrelated high indexes.
/// It also works out which of the previous run's mixes nothing will use any more (to remove). Used by Generate, and
/// by Save when it fuses sounds osu! can't play together.
/// </summary>
public sealed class MixPlanner
{
    private readonly Dictionary<string, MixFile> mixes = [];
    private readonly Dictionary<string, string> reused;
    private readonly HashSet<string> removable;
    private readonly IReadOnlyList<GeneratedMix> previousMixes;
    private readonly HashSet<int> used;
    private readonly Dictionary<string, string?> inputHashes = [];
    private long next;
    private readonly IReadOnlyDictionary<string, string> destinations;

    public int LastIndex { get; private set; }

    /// <param name="folder">The mapset as it is now.</param>
    /// <param name="previous">What the last run recorded, or null.</param>
    /// <param name="replacedFile">The .osu being written over: its own indexes don't count as in use.</param>
    /// <param name="needed">Every layer set that will be asked for, so reuse is decided before new indexes are handed out.</param>
    /// <exception cref="IOException">A difficulty or storyboard in the folder couldn't be read (fail closed).</exception>
    public MixPlanner(MapFolder folder, GenerateManifest? previous, string replacedFile, IEnumerable<IReadOnlyList<(Lane Lane, int Volume)>> needed,
        IReadOnlyDictionary<string, byte[]>? skin = null,
        IEnumerable<(IReadOnlyList<(Lane Lane, int Volume)> Layers, IReadOnlyList<(Lane Lane, int Volume, double Duration)> Loops)>? loopNeeded = null,
        IEnumerable<Lane>? liveLanes = null, IReadOnlyDictionary<string, string>? destinations = null)
    {
        this.destinations = destinations ?? new Dictionary<string, string>();
        if (this.destinations.Values.Any(slot => !ExportMergeSettings.Slots.Contains(slot)))
            throw new ArgumentException("Unknown destination hitsound slot.", nameof(destinations));
        var required = needed.ToList();
        var fingerprints = new Dictionary<Lane, string?>();
        string? Fingerprint(Lane lane)
        {
            if (fingerprints.TryGetValue(lane, out var found)) return found;
            byte[]? bytes = null;
            try
            {
                bytes = lane.Source == LaneSource.Skin ? skin?.GetValueOrDefault(lane.Name)
                    : folder.Find(lane.Name) is { } f ? folder.ReadBytes(f) : null;
            }
            catch (InvalidOperationException) { }
            return fingerprints[lane] = bytes is null ? null : GenerateManifest.Hash(bytes);
        }
        void AddFingerprint(IReadOnlyList<(Lane Lane, int Volume)> layers, IReadOnlyList<(Lane Lane, int Volume, double Duration)> loops)
        {
            var signature = MixFile.SignatureOf(layers, loops);
            var slot = this.destinations.GetValueOrDefault(signature);
            signature += MixFile.SlotSuffix(slot);
            var hashes = layers.Select(l => Fingerprint(l.Lane)).Concat(loops.Select(l => Fingerprint(l.Lane))).ToList();
            inputHashes[signature] = hashes.Any(h => h is null) ? null : GenerateManifest.Hash(System.Text.Encoding.UTF8.GetBytes("mix-v2;" + string.Join(";", hashes)));
            if (slot is not null && !slot.EndsWith("-hitnormal", StringComparison.Ordinal)) inputHashes[signature + "|silent-normal"] = "blank-v1";
        }
        foreach (var layers in required) AddFingerprint(layers, []);
        foreach (var (layers, loops) in loopNeeded ?? []) AddFingerprint(layers, loops);

        // The previous mixes still in the mapset with their bytes unchanged (plain file names only).
        var ours = (previous?.Mixes ?? []).Where(m => Path.GetFileName(m.FileName) == m.FileName && HitsoundDiff.IndexOf(m.FileName) is not null
                                                      && folder.Find(m.FileName) is { } f && GenerateManifest.Hash(folder.ReadBytes(f)) == m.Sha256)
            .GroupBy(m => m.Signature).Select(g => g.First()).ToList();
        var usedByOthers = HitsoundDiff.IndexesUsed(folder, except: replacedFile);
        var liveFiles = (liveLanes ?? []).Where(l => l.Source != LaneSource.Skin)
            .Select(l => folder.Find(l.Name) ?? l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        reused = ours.Where(m => m.InputHash is not null && inputHashes.GetValueOrDefault(m.Signature) == m.InputHash)
            .ToDictionary(m => m.Signature, m => m.FileName);
        // An addition and its silent normal are an indivisible pair at one index.
        foreach (var (signature, name) in reused.ToList())
            if (inputHashes.ContainsKey(signature + "|silent-normal"))
            {
                var sample = OsuHitsound.Format.HitsoundRules.ParseSampleName(Path.GetFileNameWithoutExtension(name))!.Value;
                string normal = $"{OsuHitsound.Format.HitsoundRules.SampleName(sample.Set, "hitnormal")}{sample.Index}.wav";
                if (reused.GetValueOrDefault(signature + "|silent-normal") != normal)
                { reused.Remove(signature); reused.Remove(signature + "|silent-normal"); }
            }
        foreach (string signature in reused.Keys.Where(k => k.EndsWith("|silent-normal", StringComparison.Ordinal)).ToList())
            if (!reused.ContainsKey(signature[..^"|silent-normal".Length])) reused.Remove(signature);
        removable = ours.Where(m => !reused.ContainsValue(m.FileName) && !liveFiles.Contains(m.FileName) && !(HitsoundDiff.IndexOf(m.FileName) is { } n && usedByOthers.Contains(n)))
            .Select(m => m.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        previousMixes = previous?.Mixes ?? [];
        LastIndex = Math.Max(previous?.LastIndex ?? 1, HitsoundDiff.MaxFileIndex(previousMixes.Select(m => m.FileName)));
        if (folder.Find(replacedFile) is { } oldDiff)
        {
            var onlyDiff = new MapFolder([oldDiff], folder.ReadBytes);
            var saved = OsuHitsound.Format.OsuFile.Parse(folder.ReadBytes(oldDiff)).Lines
                .Select(l => l.Text.Trim()).FirstOrDefault(l => l.StartsWith(HitsoundDiff.GeneratedIndexMarker, StringComparison.Ordinal));
            if (saved is not null && int.TryParse(saved[HitsoundDiff.GeneratedIndexMarker.Length..], out int last) && last > 0)
                LastIndex = Math.Max(LastIndex, last);
            else if (previous is null)
            {
                // Older generated diffs have no marker. Infer from the indexes used by the diff and its files.
                var generated = HitsoundDiff.IndexesUsed(onlyDiff, null).Where(i => folder.FindSample($"soft-hitnormal{i}") is not null);
                LastIndex = Math.Max(LastIndex, generated.DefaultIfEmpty(1).Max());
            }
        }
        next = (long)LastIndex + 1;
        used = [.. usedByOthers, .. folder.Files.Where(f => !removable.Contains(f)).Select(HitsoundDiff.IndexOf).OfType<int>()];
    }

    /// <summary>The mix for these layers (sorted by lane, volumes already at least stable's minimum).</summary>
    public MixFile MixFor(IReadOnlyList<(Lane Lane, int Volume)> layers, IReadOnlyList<(Lane Lane, int Volume, double Duration)>? loops = null)
    {
        loops ??= [];
        string signature = MixFile.SignatureOf(layers, loops);
        var slot = destinations.GetValueOrDefault(signature);
        signature += MixFile.SlotSuffix(slot);
        if (!mixes.TryGetValue(signature, out var mix))
        {
            string? file = reused.GetValueOrDefault(signature);
            if (file is null)
            {
                while (next <= int.MaxValue && used.Contains((int)next)) next++;
                if (next > int.MaxValue) throw new IOException("No sample index is available after the last generated index.");
                LastIndex = (int)next++;
                used.Add(LastIndex);
                file = $"{slot ?? "soft-hitnormal"}{LastIndex}.wav";
            }

            mixes[signature] = mix = new MixFile(file, layers) { Loops = loops, DestinationSlot = slot };
            if (slot is not null && !slot.EndsWith("-hitnormal", StringComparison.Ordinal))
            {
                var sample = OsuHitsound.Format.HitsoundRules.ParseSampleName(Path.GetFileNameWithoutExtension(file))!.Value;
                string normal = $"{OsuHitsound.Format.HitsoundRules.SampleName(sample.Set, "hitnormal")}{sample.Index}.wav";
                mixes[signature + "|silent-normal"] = new MixFile(normal, []) { SilenceFor = signature };
            }
        }
        return mix;
    }

    /// <summary>Every mix handed out, reused ones included (the folder will contain all of them).</summary>
    public IEnumerable<MixFile> All => mixes.Values;

    /// <summary>Mixes to render and write: those handed out that aren't reused as they are.</summary>
    public IReadOnlyList<MixFile> ToWrite => [.. mixes.Values.Where(m => !reused.ContainsValue(m.FileName))];

    /// <summary>The previous run's records for mixes used again unchanged.</summary>
    public IReadOnlyList<GeneratedMix> Reused => [.. previousMixes.Where(m => reused.ContainsValue(m.FileName))];

    public IReadOnlyDictionary<string, string?> InputHashes => inputHashes;

    /// <summary>The previous run's mixes nothing will use any more.</summary>
    public IReadOnlyList<string> Removes => [.. removable];

    /// <summary>Layers as a mix takes them: stable's 8 % minimum applied, sorted by lane.</summary>
    public static List<(Lane Lane, int Volume)> Normalize(IEnumerable<(Lane Lane, int Volume)> layers, bool includeMissing = false) =>
        [.. layers.Where(l => includeMissing || !l.Lane.Missing)
            .Select(l => (l.Lane, Volume: Math.Max(l.Volume, Format.HitsoundRules.StableMinimumVolume)))
            .OrderBy(l => ProjectStore.KeyOf(l.Lane))];
}

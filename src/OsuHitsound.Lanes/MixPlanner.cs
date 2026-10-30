namespace OsuHitsound.Lanes;

/// <summary>
/// Hands out file names for mixed samples ("fusing"): identical layer sets share one file; a mix the previous run
/// already wrote with the same contents keeps its file and index; new ones go on the lowest index nothing else uses.
/// It also works out which of the previous run's mixes nothing will use any more (to remove). Used by Generate, and
/// by Save when it fuses sounds osu! can't play together.
/// </summary>
public sealed class MixPlanner
{
    private readonly Dictionary<string, MixFile> mixes = [];
    private readonly Dictionary<string, string> reused;
    private readonly HashSet<string> removable;
    private readonly IReadOnlyList<GeneratedMix> previousMixes;
    private int next;

    /// <param name="folder">The mapset as it is now.</param>
    /// <param name="previous">What the last run recorded, or null.</param>
    /// <param name="replacedFile">The .osu being written over: its own indexes don't count as in use.</param>
    /// <param name="needed">Every layer set that will be asked for, so reuse is decided before new indexes are handed out.</param>
    /// <exception cref="IOException">A difficulty or storyboard in the folder couldn't be read (fail closed).</exception>
    public MixPlanner(MapFolder folder, GenerateManifest? previous, string replacedFile, IEnumerable<IReadOnlyList<(Lane Lane, int Volume)>> needed)
    {
        var signatures = needed.Where(l => l.Count > 0).Select(MixFile.SignatureOf).ToHashSet();

        // The previous mixes still in the mapset with their bytes unchanged (plain file names only).
        var ours = (previous?.Mixes ?? []).Where(m => Path.GetFileName(m.FileName) == m.FileName && HitsoundDiff.IndexOf(m.FileName) is not null
                                                      && folder.Find(m.FileName) is { } f && GenerateManifest.Hash(folder.ReadBytes(f)) == m.Sha256)
            .GroupBy(m => m.Signature).Select(g => g.First()).ToList();
        var usedByOthers = HitsoundDiff.IndexesUsed(folder, except: replacedFile);
        reused = ours.Where(m => signatures.Contains(m.Signature)).ToDictionary(m => m.Signature, m => m.FileName);
        removable = ours.Where(m => !reused.ContainsValue(m.FileName) && !(HitsoundDiff.IndexOf(m.FileName) is { } n && usedByOthers.Contains(n)))
            .Select(m => m.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        next = Math.Max(HitsoundDiff.MaxFileIndex(folder.Files.Where(f => !removable.Contains(f))), usedByOthers.DefaultIfEmpty(1).Max()) + 1;
        previousMixes = previous?.Mixes ?? [];
    }

    /// <summary>The mix for these layers (sorted by lane, volumes already at least stable's minimum).</summary>
    public MixFile MixFor(IReadOnlyList<(Lane Lane, int Volume)> layers)
    {
        string signature = MixFile.SignatureOf(layers);
        if (!mixes.TryGetValue(signature, out var mix))
            mixes[signature] = mix = new MixFile(reused.GetValueOrDefault(signature) ?? $"soft-hitnormal{next++}.wav", layers);
        return mix;
    }

    /// <summary>Every mix handed out, reused ones included (the folder will contain all of them).</summary>
    public IEnumerable<MixFile> All => mixes.Values;

    /// <summary>Mixes to render and write: those handed out that aren't reused as they are.</summary>
    public IReadOnlyList<MixFile> ToWrite => [.. mixes.Values.Where(m => !reused.ContainsValue(m.FileName))];

    /// <summary>The previous run's records for mixes used again unchanged.</summary>
    public IReadOnlyList<GeneratedMix> Reused => [.. previousMixes.Where(m => reused.ContainsValue(m.FileName))];

    /// <summary>The previous run's mixes nothing will use any more.</summary>
    public IReadOnlyList<string> Removes => [.. removable];

    /// <summary>Layers as a mix takes them: missing files left out, stable's 8 % minimum applied, sorted by lane.</summary>
    public static List<(Lane Lane, int Volume)> Normalize(IEnumerable<(Lane Lane, int Volume)> layers) =>
        [.. layers.Where(l => !l.Lane.Missing)
            .Select(l => (l.Lane, Volume: Math.Max(l.Volume, Format.HitsoundRules.StableMinimumVolume)))
            .OrderBy(l => ProjectStore.KeyOf(l.Lane))];
}

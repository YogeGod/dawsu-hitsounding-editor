using System.Globalization;
using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>Preserves sound roles while allocating a compatible bank/index for every component of an overlap.</summary>
public sealed class PairingPlanner
{
    private readonly LaneAudio audio;
    private readonly ExportMergeSettings settings;
    private readonly GenerateManifest? previous;
    private readonly HashSet<int> occupied;
    private readonly Dictionary<string, WantedSound> planned = [];
    private readonly List<MixFile> files = [];
    private readonly List<GeneratedMix> reused = [];
    private readonly Dictionary<string, string?> hashes = [];
    private readonly List<MergeCombination> combinations = [];
    private readonly List<Conflict> conflicts = [];
    private long next = 2;

    /// <param name="audio">The job's lane audio; its folder listing also decides which indexes are taken.</param>
    public PairingPlanner(LaneAudio audio, ExportMergeSettings? settings = null, GenerateManifest? previous = null)
    {
        this.audio = audio; this.settings = settings ?? new(); this.previous = previous;
        this.settings.Validate();
        occupied = [.. HitsoundDiff.IndexesUsed(audio.Folder, except: null), .. audio.Folder.Files.Select(HitsoundDiff.IndexOf).OfType<int>()];
    }

    /// <summary>The lane audio it reads (and whose files were read, to check before writing).</summary>
    public LaneAudio Audio => audio;

    public IReadOnlyList<MixFile> Files => files;
    public IReadOnlyList<GeneratedMix> Reused => reused;
    public IReadOnlyDictionary<string, string?> InputHashes => hashes;
    public IReadOnlyList<MergeCombination> Combinations => combinations;
    public IReadOnlyList<Conflict> Conflicts => conflicts;
    internal void ClearConflicts() => conflicts.Clear();
    public int LastIndex { get; private set; } = 1;

    private byte[] Read(Lane lane) => audio.Read(Pairable(lane));

    private string Fingerprint(Lane lane) => audio.Hash(Pairable(lane));

    // Storyboard samples are never paired: only skin sounds and the map's own sample files.
    private static Lane Pairable(Lane lane) => lane.Source == LaneSource.StoryboardFile ? throw new IOException($"Storyboard samples can't be paired: {lane.Name}.") : lane;

    public static bool CanEncode(MapFolder folder, IReadOnlyList<(Lane Lane, int Volume)> layers)
    {
        if (layers.Count == 0 || layers.Any(l => l.Lane.Missing || l.Lane.Sample is not { Sound: "hitnormal" or "hitwhistle" or "hitfinish" or "hitclap" })) return false;
        if (layers.Select(l => l.Volume).Distinct().Count() != 1) return false;
        if (layers.Count(l => l.Lane.Sample!.Value.Sound == "hitnormal") != 1) return false;
        var additions = layers.Where(l => l.Lane.Sample!.Value.Sound != "hitnormal").ToList();
        if (additions.GroupBy(l => l.Lane.Sample!.Value.Sound).Any(g => g.Count() > 1) || additions.Select(l => l.Lane.Sample!.Value.Set).Distinct().Count() > 1) return false;
        var indexes = layers.Where(l => l.Lane.Source != LaneSource.Skin).Select(l => l.Lane.Sample!.Value.Index).Distinct().ToList();
        if (indexes.Count > 1) return false;
        int index = indexes.FirstOrDefault();
        return layers.All(l => Decompiler.ResolveLane(folder, l.Lane.Sample!.Value.Set, l.Lane.Sample.Value.Sound, index) == l.Lane);
    }

    public WantedSound For(IEnumerable<(Lane Lane, int Volume)> input, double time = double.NaN, MomentKey? moment = null, bool alreadyEncoded = false, IReadOnlySet<Lane>? exactGainLanes = null)
    {
        var original = input.ToList();
        bool bakeExactGain = original.Any(l => exactGainLanes?.Contains(l.Lane) == true && l.Volume < HitsoundRules.StableMinimumVolume);
        var layers = bakeExactGain ? original.Select(l => (l.Lane, Volume: exactGainLanes?.Contains(l.Lane) == true ? l.Volume : Math.Max(l.Volume, HitsoundRules.StableMinimumVolume))).OrderBy(l => ProjectStore.KeyOf(l.Lane)).ToList()
            : MixPlanner.Normalize(original, includeMissing: true);
        var combination = new MergeCombination(layers, []) { Time = time, MatchBalance = true };
        // Native combinations always win over saved merge rules, including low inherited volumes.
        if (!bakeExactGain && original.Count > 0 && (alreadyEncoded || CanEncode(audio.Folder, original)))
            return new(original.Select(l => l.Lane).ToList(), original[0].Volume);
        combination = combination with { Fingerprints = layers.Select(l => l.Lane).Distinct().ToDictionary(l => l, Fingerprint) };
        if (layers.Count > 1) combinations.Add(combination);
        if (settings.KeepOriginal(combination) && (bakeExactGain || !alreadyEncoded && !CanEncode(audio.Folder, original)))
        {
            conflicts.Add(new(time, moment ?? new(-1, 0), "Original files were requested, but these layers need pairing or fusion. Use automatic handling in Review output, or change the layers."));
            return new(original.Select(l => l.Lane).ToList(), original.FirstOrDefault().Volume);
        }
        string? rule = settings.RuleFor(combination);
        var chosen = rule ?? combination.AutomaticSlot(settings) ?? "soft-hitnormal";
        var bank = HitsoundRules.ParseSampleName(chosen)!.Value.Set;
        var normalBank = layers.FirstOrDefault(l => l.Lane.Sample?.Sound == "hitnormal").Lane?.Sample?.Set ?? bank;
        int volume = layers.Count == 0 || bakeExactGain ? 100 : layers.Select(l => l.Volume).Distinct().Count() == 1 ? layers[0].Volume : 100;
        var components = layers.GroupBy(l => l.Lane.Sample?.Sound is "hitwhistle" or "hitfinish" or "hitclap" ? l.Lane.Sample.Value.Sound : "hitnormal")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (Slot: HitsoundRules.SampleName(g.Key == "hitnormal" ? normalBank : bank, g.Key), Layers: g.ToList())).ToList();
        if (!components.Any(c => c.Slot.EndsWith("-hitnormal", StringComparison.Ordinal))) components.Add((HitsoundRules.SampleName(normalBank, "hitnormal"), []));
        string identity = "pair-v1|" + string.Join(";", components.Select(c => c.Slot + ":" + string.Join(",", c.Layers.Select(l =>
            ProjectStore.KeyOf(l.Lane) + "@" + ((int)Math.Round(l.Volume * 100.0 / volume)).ToString(CultureInfo.InvariantCulture) + "@" + Fingerprint(l.Lane)))));
        string inputHash = GenerateManifest.Hash(Encoding.UTF8.GetBytes(identity));
        if (planned.TryGetValue(identity, out var found)) return found with { Volume = volume };

        string Signature(string slot) => identity + "|output:" + slot;
        var old = components.Select(c => previous?.Mixes.FirstOrDefault(m => m.Signature == Signature(c.Slot)
            && m.InputHash == inputHash && MapFolder.IsInside(m.FileName)
            && Path.GetFileName(m.FileName) == m.FileName && audio.Folder.Find(m.FileName) is { } f && GenerateManifest.Hash(audio.Folder.ReadBytes(f)) == m.Sha256)).ToList();
        bool reuse = old.All(m => m is not null) && old.Select(m => HitsoundDiff.IndexOf(m!.FileName)).Distinct().Count() == 1;
        var outputs = new List<Lane>();
        if (reuse)
        {
            foreach (var item in old) { if (!reused.Contains(item!)) reused.Add(item!); outputs.Add(new(LaneSource.BeatmapFile, item!.FileName)); }
        }
        else
        {
            while (next <= int.MaxValue && occupied.Contains((int)next)) next++;
            if (next > int.MaxValue) throw new IOException("No free sample index remains for pairing.");
            int index = (int)next++; occupied.Add(index); LastIndex = Math.Max(LastIndex, index);
            foreach (var component in components)
            {
                bool copy = component.Layers.Count == 1 && component.Layers[0].Volume == volume;
                string extension = copy && component.Layers[0].Lane.Source == LaneSource.BeatmapFile ? Path.GetExtension(component.Layers[0].Lane.Name).ToLowerInvariant() : ".wav";
                if (!HitsoundRules.SampleExtensions.Contains(extension)) throw new IOException("Unsupported paired sample extension.");
                string name = component.Slot + index + extension;
                var renderLayers = component.Layers.Select(l => (l.Lane, Volume: (int)Math.Round(l.Volume * 100.0 / volume))).ToList();
                var mix = new MixFile(name, renderLayers)
                {
                    DestinationSlot = component.Slot, PairSignature = Signature(component.Slot),
                    CopyBytes = component.Layers.Count == 0 ? BlankSample.Bytes : copy ? Read(component.Layers[0].Lane) : null,
                    SilenceFor = component.Layers.Count == 0 ? "pair-silence" : null,
                };
                files.Add(mix); hashes[mix.Signature] = inputHash;
                outputs.Add(new(LaneSource.BeatmapFile, name));
            }
        }
        return planned[identity] = new(outputs, volume);
    }
}

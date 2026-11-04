using System.Text;
using System.Text.Json;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>A complete overlap. Pairing can include relative balance in its override identity.</summary>
public sealed record MergeCombination(IReadOnlyList<(Lane Lane, int Volume)> Layers,
    IReadOnlyList<(Lane Lane, int Volume, double Duration)> Loops)
{
    public double Time { get; init; } = double.NaN;
    public bool MatchBalance { get; init; }
    public string OccurrenceKey => Time.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|" + Key;
    public IReadOnlyDictionary<Lane, string> Fingerprints { get; init; } = new Dictionary<Lane, string>();
    private string InputIdentity(Lane lane) => Identity(lane) + "@" + Fingerprints.GetValueOrDefault(lane, "unknown");
    private string Balance(int volume) => MatchBalance ? "@balance:" + ((decimal)volume / Math.Max(1, Layers.Max(l => l.Volume))).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
    public string Key => JsonSerializer.Serialize(Layers.Select(l => "hit:" + InputIdentity(l.Lane) + Balance(l.Volume))
        .Concat(Loops.Select(l => "loop:" + InputIdentity(l.Lane))).Order(StringComparer.Ordinal));

    public static string Identity(Lane lane) => $"{lane.Source}:{lane.Name.Replace('\\', '/').ToLowerInvariant()}";

    public IEnumerable<string> AdditionSlots => Layers.Select(l => l.Lane.Sample)
        .Where(s => s is { Sound: "hitwhistle" or "hitfinish" or "hitclap" })
        .Select(s => HitsoundRules.SampleName(s!.Value.Set, s.Value.Sound));

    public string? AutomaticSlot(ExportMergeSettings settings)
    {
        if (Loops.Count > 0 || Layers.Any(l => l.Lane.Sample?.Sound is "slidertick" or "sliderslide" or "sliderwhistle")) return null;
        var slots = AdditionSlots.ToList();
        if (slots.Count == 0)
            slots = Layers.Where(l => l.Lane.Sample?.Sound == "hitnormal")
                .Select(l => HitsoundRules.SampleName(l.Lane.Sample!.Value.Set, "hitnormal")).ToList();
        // The mandatory normal does not outrank additions. Unnamed custom inputs use the first configured slot.
        return slots.Count == 0 ? settings.Priorities[0] : settings.Priorities.First(slots.Contains);
    }
}

/// <summary>Explicitly loaded/saved rules. No map or application-wide automatic persistence.</summary>
public sealed class ExportMergeSettings
{
    public static IReadOnlyList<string> Slots { get; } = new[] { SampleSet.Normal, SampleSet.Soft, SampleSet.Drum }
        .SelectMany(set => new[] { "hitnormal", "hitwhistle", "hitfinish", "hitclap" }.Select(sound => HitsoundRules.SampleName(set, sound))).ToArray();

    public int Version { get; set; } = 1;
    public bool PairSamples { get; set; }
    public ExportStyle Style { get; set; } = ExportStyle.GreenLines;
    public Dictionary<string, string> OccurrenceRules { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, bool> KeepOriginalRules { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, bool> OccurrenceKeepOriginal { get; set; } = new(StringComparer.Ordinal);
    public List<string> Priorities { get; set; } = [.. Slots];
    public Dictionary<string, string> Rules { get; set; } = new(StringComparer.Ordinal);

    public string? Destination(MergeCombination combination) => Rules.GetValueOrDefault(combination.Key) ?? combination.AutomaticSlot(this);
    public string? RuleFor(MergeCombination combination) => OccurrenceRules.GetValueOrDefault(combination.OccurrenceKey) ?? Rules.GetValueOrDefault(combination.Key);
    public bool HasRule(MergeCombination combination) => RuleFor(combination) is not null;
    public bool KeepOriginal(MergeCombination combination) => OccurrenceKeepOriginal.TryGetValue(combination.OccurrenceKey, out bool local) ? local : KeepOriginalRules.GetValueOrDefault(combination.Key);
    public ExportMergeSettings Copy() => new()
    {
        Version = Version, PairSamples = PairSamples, Style = Style, Priorities = [.. Priorities],
        Rules = new(Rules, StringComparer.Ordinal), OccurrenceRules = new(OccurrenceRules, StringComparer.Ordinal),
        KeepOriginalRules = new(KeepOriginalRules, StringComparer.Ordinal), OccurrenceKeepOriginal = new(OccurrenceKeepOriginal, StringComparer.Ordinal),
    };

    public void Validate()
    {
        if (KeepOriginalRules is null || OccurrenceKeepOriginal is null || KeepOriginalRules.Count > 10000 || OccurrenceKeepOriginal.Count > 10000 ||
            KeepOriginalRules.Keys.Concat(OccurrenceKeepOriginal.Keys).Any(k => k.Length > 131072)) throw new FormatException("Invalid output overrides.");
        if (Version != 1 || !Enum.IsDefined(Style) || OccurrenceRules is null || OccurrenceRules.Count > 10000 || OccurrenceRules.Any(p => p.Key.Length > 131072 || !Slots.Contains(p.Value)) || Priorities is null || Rules is null || Priorities.Count != Slots.Count ||
            !Priorities.ToHashSet(StringComparer.Ordinal).SetEquals(Slots) || Rules.Count > 10000)
            throw new FormatException("Invalid merge preset: expected each hitsound slot exactly once and a supported version.");
        foreach (var (key, slot) in Rules)
        {
            if (!Slots.Contains(slot) || key.Length > 65536) throw new FormatException("Invalid destination or combination in merge preset.");
            try
            {
                var identities = JsonSerializer.Deserialize<string[]>(key);
                if (identities is not { Length: > 0 } || identities.Any(i => string.IsNullOrWhiteSpace(i)) ||
                    JsonSerializer.Serialize(identities.Order(StringComparer.Ordinal)) != key)
                    throw new FormatException("Invalid layer combination in merge preset.");
            }
            catch (JsonException ex) { throw new FormatException("Invalid layer combination in merge preset.", ex); }
        }
    }

    public static ExportMergeSettings Load(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new FormatException("Merge preset is too large.");
        try
        {
            var settings = JsonSerializer.Deserialize<ExportMergeSettings>(File.ReadAllText(path)) ?? throw new FormatException("Empty merge preset.");
            settings.Validate();
            return settings;
        }
        catch (JsonException ex) { throw new FormatException("Could not read merge preset JSON.", ex); }
    }

    public void Save(string path)
    {
        Validate();
        SafeFile.WriteAllBytes(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })));
    }
}

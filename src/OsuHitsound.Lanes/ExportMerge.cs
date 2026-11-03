using System.Text;
using System.Text.Json;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>A complete overlap. Identity includes multiplicity and loop lanes, but not volumes or durations.</summary>
public sealed record MergeCombination(IReadOnlyList<(Lane Lane, int Volume)> Layers,
    IReadOnlyList<(Lane Lane, int Volume, double Duration)> Loops)
{
    public IReadOnlyDictionary<Lane, string> Fingerprints { get; init; } = new Dictionary<Lane, string>();
    private string InputIdentity(Lane lane) => Identity(lane) + "@" + Fingerprints.GetValueOrDefault(lane, "unknown");
    public string Key => JsonSerializer.Serialize(Layers.Select(l => "hit:" + InputIdentity(l.Lane))
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
    public List<string> Priorities { get; set; } = [.. Slots];
    public Dictionary<string, string> Rules { get; set; } = new(StringComparer.Ordinal);

    public string? Destination(MergeCombination combination) => Rules.GetValueOrDefault(combination.Key) ?? combination.AutomaticSlot(this);

    public void Validate()
    {
        if (Version != 1 || Priorities is null || Rules is null || Priorities.Count != Slots.Count ||
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

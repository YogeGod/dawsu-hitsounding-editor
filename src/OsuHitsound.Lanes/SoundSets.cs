using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

public sealed record SoundSetMember(Guid Id, string OriginalKey, Lane Lane, string Slot, int Volume, byte[] Audio)
{
    private static readonly ConditionalWeakTable<byte[], string> Hashes = new();
    [JsonIgnore]
    public string Hash => Hashes.GetValue(Audio, bytes => GenerateManifest.Hash(bytes));
}
public sealed record SoundSet(Guid Id, string Name, ImmutableList<SoundSetMember> Members)
{
    public Guid? LibraryId { get; init; }
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 128 || Members is null || Members.Count is < 1 or > 128)
            throw new FormatException("A sound set needs a name and 1–128 sounds.");
        if (Members.Any(m => m is null || m.Id == Guid.Empty || m.Lane is null || m.Lane.Missing || !Enum.IsDefined(m.Lane.Source) || m.Lane.Source == LaneSource.StoryboardFile || m.Lane.Name is not { Length: > 0 and <= 1024 } || m.Volume is < 0 or > 100 ||
                !ExportMergeSettings.Slots.Contains(m.Slot) || m.Audio is not { Length: > 0 and <= 33554432 } ||
                m.OriginalKey is null || m.OriginalKey.Length > 1024 || !MapFolder.IsInside(m.Lane.Name) ||
                (m.Lane.Sample is { } sample ? HitsoundRules.SampleName(sample.Set, sample.Sound) != m.Slot : m.Slot != "normal-hitnormal")) ||
            Members.Select(m => m.Id).Distinct().Count() != Members.Count || Members.Sum(m => (long)m.Audio.Length) > 67108864)
            throw new FormatException("Invalid or oversized sound set.");
    }
}
public sealed record SoundSetOverride
{
    public Lane? Lane { get; init; }
    public int? Volume { get; init; }
    public bool Removed { get; init; }
    public bool Shared { get; init; }
}
public sealed record SoundSetPlacement(Guid Id, Guid SetId, double Time)
{
    public int Gain { get; init; } = 100;
    public ImmutableDictionary<Guid, SoundSetOverride> Overrides { get; init; } = ImmutableDictionary<Guid, SoundSetOverride>.Empty;
}
public sealed record SoundSetBinding(string Lane, double Time, int Ordinal, Guid Placement, Guid Member);
public sealed record SoundSetProject(string FileHash, List<SoundSet> Sets, List<SoundSetPlacement> Placements, List<SoundSetBinding> Bindings);

/// <summary>Self-contained set files and role-preserving snapshots in a mapset.</summary>
public static class SoundSetFiles
{
    public const string Extension = ".dawsuset";
    public static SoundSet Capture(string name, IEnumerable<(Lane Lane, int Volume)> layers, MapFolder folder, IReadOnlyDictionary<string, byte[]> skin)
    {
        var members = layers.Select(l =>
        {
            var sample = l.Lane.Sample;
            if (sample is { Sound: not ("hitnormal" or "hitwhistle" or "hitfinish" or "hitclap") }) throw new IOException("Use hitnormal, whistle, finish or clap sounds in a set.");
            byte[] raw = l.Lane.Source == LaneSource.Skin ? skin.GetValueOrDefault(l.Lane.Name) ?? throw new IOException($"Missing {l.Lane.Name}.")
                : folder.Find(l.Lane.Name) is { } file ? folder.ReadBytes(file) : throw new IOException($"Missing {l.Lane.Name}.");
            return new SoundSetMember(Guid.NewGuid(), ProjectStore.KeyOf(l.Lane), l.Lane,
                sample is { } s ? HitsoundRules.SampleName(s.Set, s.Sound) : "normal-hitnormal", l.Volume, raw.ToArray());
        }).ToImmutableList();
        var result = new SoundSet(Guid.NewGuid(), name.Trim(), members);
        result.Validate(); return result;
    }

    public static SoundSet Install(string directory, SoundSet set, SoundSet? previous = null)
    {
        set.Validate();
        var folder = MapFolder.FromDirectory(directory);
        var occupied = HitsoundDiff.IndexesUsed(folder, except: null).Concat(folder.Files.Select(HitsoundDiff.IndexOf).OfType<int>()).ToHashSet();
        var written = new List<string>();
        long index = 2;
        try
        {
            var members = set.Members.Select(member =>
            {
                string extension = member.Lane.Source == LaneSource.Skin ? ".wav" : Path.GetExtension(member.Lane.Name).ToLowerInvariant();
                if (!HitsoundRules.SampleExtensions.Contains(extension)) throw new IOException("Unsupported sound-set audio extension.");
                // Reuse only a previously installed snapshot, never its external source file.
                if (previous?.Members.Any(m => m.Id == member.Id && m.Slot == member.Slot && m.Lane == member.Lane && m.Hash == member.Hash) == true &&
                    ProjectStore.KeyOf(member.Lane) != member.OriginalKey && member.Lane.Source == LaneSource.BeatmapFile && folder.Find(member.Lane.Name) is { } prior &&
                    GenerateManifest.Hash(folder.ReadBytes(prior)) == member.Hash) return member;
                while (index <= int.MaxValue && occupied.Contains((int)index)) index++;
                if (index > int.MaxValue) throw new IOException("No free sound-set sample index remains.");
                string name = member.Slot + index + extension; occupied.Add((int)index++);
                string path = Path.Combine(directory, name);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { written.Add(path); stream.Write(member.Audio); }
                return member with { Lane = new Lane(LaneSource.BeatmapFile, name) };
            }).ToImmutableList();
            return set with { Members = members };
        }
        catch { foreach (string path in written) File.Delete(path); throw; }
    }

    public static void Save(string path, SoundSet set)
    {
        set.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        SafeFile.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(set, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static SoundSet Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 96 * 1024 * 1024) throw new FormatException("Sound set is too large.");
        try { var set = JsonSerializer.Deserialize<SoundSet>(stream) ?? throw new FormatException("Empty sound set."); set.Validate(); return set; }
        catch (JsonException ex) { throw new FormatException("Could not read this sound set.", ex); }
    }
}

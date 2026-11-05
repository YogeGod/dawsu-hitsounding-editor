using System.Collections.Immutable;
using System.Text.Json;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

public sealed record DrumMapEntry(Guid Id, string Name, string? LaneKey, string? SlotKey)
{
    // Missing fields in older presets acquire the starter mapping; explicit null stays unassigned.
    public int? MidiNote { get; init; } = DrumMapMidi.DefaultNote(Name);
}

/// <summary>General MIDI percussion notes (channel 10). Articulations absent from GM share their nearest sound.</summary>
public static class DrumMapMidi
{
    public static int? DefaultNote(string name) => name switch
    {
        "Kick" => 36,
        "Snare" or "Ghost Notes" => 38,
        "T1" => 50, "T2" => 48, "T3" => 45, "T4" => 41,
        "Tight Tip" or "Tight Edge" or "Closed Tip" or "Closed Edge" => 42,
        "Hi-Hat Open 1" or "Hi-Hat Open 2" or "Hi-Hat Open 3" => 46,
        "Ride Bell" => 53,
        "Bow Ride (Low)" => 51, "Bow Ride (High)" or "Ride Crash" => 59,
        "Right Crash" => 49, "Left Crash" => 57,
        "China" or "Stack" => 52,
        "Mini Stack Left" or "Mini Stack Right" or "Splash" => 55,
        _ => null
    };
}
public sealed record DrumMapAudio(string LaneKey, string SlotKey, byte[] Bytes);
public sealed record DrumMapPreset(Guid Id, string Name, ImmutableList<DrumMapEntry> Rows)
{
    public ImmutableList<DrumMapAudio> Audio { get; init; } = [];
    public static DrumMapPreset Empty(string name) => new(Guid.NewGuid(), name, []);
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 128 || Rows is null || Audio is null || Rows.Count > 4096 || Audio.Count > 512 ||
            Rows.Any(r => r is null || r.Id == Guid.Empty || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 128 || r.MidiNote is < 0 or > 127 || !ValidKey(r.LaneKey) || !ValidKey(r.SlotKey)) ||
            Rows.Select(r => r.Id).Distinct().Count() != Rows.Count ||
            Audio.Any(a => a is null || !ValidKey(a.LaneKey) || !ValidKey(a.SlotKey) || a.LaneKey is null || a.SlotKey is null || a.Bytes is not { Length: > 0 and <= 33554432 }) ||
            Audio.Select(a => a.LaneKey).Distinct().Count() != Audio.Count || Audio.Sum(a => (long)a.Bytes.Length) > 67108864)
            throw new FormatException("Invalid drum map. Use a name, up to 4096 rows and at most 64 MB of audio.");
    }
    private static bool ValidKey(string? key) => key is null || key.Length <= 1100 && ProjectStore.LaneOf(key) is { } lane &&
        Enum.IsDefined(lane.Source) && lane.Source != LaneSource.StoryboardFile && MapFolder.IsInside(lane.Name) &&
        (lane.Sample is null || lane.Sample?.Sound is "hitnormal" or "hitwhistle" or "hitfinish" or "hitclap");
}
public sealed record DrumMapCollection(ImmutableList<DrumMapPreset> Presets, Guid? Active, bool Enabled)
{
    public static DrumMapCollection Empty => new([], null, false);
    public void Validate()
    {
        if (Presets is null || Presets.Count > 64 || Presets.Sum(p => p?.Rows?.Count ?? 0) > 16384 || Presets.Select(p => p?.Id).Distinct().Count() != Presets.Count ||
            Active is { } id && Presets.All(p => p?.Id != id)) throw new FormatException("Invalid drum-map collection.");
        foreach (var preset in Presets) { if (preset is null) throw new FormatException("Invalid drum map."); preset.Validate(); }
    }
}

/// <summary>Editor-only mapset layouts and portable presets; never written into .osu or sample source files.</summary>
public static class DrumMapStore
{
    public const string Extension = ".dawsudrum";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string DefaultRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuHitsoundEditor", "drum-maps");
    public static string PathFor(string root, string mapset) => ProjectStore.PathFor(root, Path.Combine(Path.GetFullPath(mapset), "drum-maps"));
    public static (DrumMapCollection Maps, string Hash) Load(string root, string mapset)
    {
        string path = PathFor(root, mapset);
        if (!File.Exists(path)) return (DrumMapCollection.Empty, "missing");
        var bytes = Read(path, 48 * 1024 * 1024);
        try { var maps = JsonSerializer.Deserialize<DrumMapCollection>(bytes) ?? throw new FormatException("Empty drum-map collection."); maps.Validate(); return (maps, GenerateManifest.Hash(bytes)); }
        catch (JsonException ex) { throw new FormatException("Could not read the mapset's drum maps.", ex); }
    }
    public static string Save(string root, string mapset, DrumMapCollection maps, string expectedHash)
    {
        maps.Validate();
        if (maps.Presets.Any(p => p.Audio.Count > 0)) throw new FormatException("Mapset layouts reference installed samples; embedded audio belongs in a portable preset.");
        string path = PathFor(root, mapset); Directory.CreateDirectory(root);
        using var gate = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string hash = File.Exists(path) ? GenerateManifest.Hash(Read(path, 48 * 1024 * 1024)) : "missing";
        if (hash != expectedHash) throw new IOException("Drum maps changed in another editor. Reopen the difficulty before editing its drum maps.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(maps, Json);
        if (bytes.Length > 48 * 1024 * 1024) throw new FormatException("Drum-map collection is too large.");
        SafeFile.WriteAllBytes(path, bytes); return GenerateManifest.Hash(bytes);
    }
    public static void Export(string path, DrumMapPreset preset)
    {
        preset.Validate(); var bytes = JsonSerializer.SerializeToUtf8Bytes(preset, Json);
        if (bytes.Length > 96 * 1024 * 1024) throw new FormatException("Portable drum map is too large.");
        SafeFile.WriteAllBytes(path, bytes);
    }
    public static DrumMapPreset Import(string path)
    {
        try { var preset = JsonSerializer.Deserialize<DrumMapPreset>(Read(path, 96 * 1024 * 1024)) ?? throw new FormatException("Empty drum map."); preset.Validate(); return preset; }
        catch (JsonException ex) { throw new FormatException("Could not read this drum-map preset.", ex); }
    }
    private static byte[] Read(string path, int limit)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > limit) throw new FormatException("Drum-map file is too large.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new IOException("Drum-map file changed while reading. Try again.");
        return bytes;
    }
}

/// <summary>User-specified starter layouts. Samples are intentionally unassigned.</summary>
public static class DrumMapDefaults
{
    public static DrumMapCollection Create()
    {
        string[] expanded = ["Kick", "Snare", "Ghost Notes", "T1", "T2", "T3", "T4", "Tight Tip", "Tight Edge", "Closed Tip", "Closed Edge",
            "Hi-Hat Open 1", "Hi-Hat Open 2", "Hi-Hat Open 3", "Ride Bell", "Bow Ride (Low)", "Bow Ride (High)", "Right Crash", "Left Crash",
            "Ride Crash", "China", "Stack", "Mini Stack Left", "Mini Stack Right", "Splash"];
        string[] simple = ["Kick", "Snare", "T1", "T2", "T3", "T4", "Tight Tip", "Tight Edge", "Closed Tip", "Closed Edge",
            "Hi-Hat Open 1", "Hi-Hat Open 2", "Hi-Hat Open 3", "Ride Bell", "Right Crash", "Left Crash", "Ride Crash", "China"];
        DrumMapPreset Make(string name, string[] names) => new(Guid.NewGuid(), name,
            names.Select(n => new DrumMapEntry(Guid.NewGuid(), n, null, null)).ToImmutableList());
        var first = Make("Yoge Expanded Kit", expanded); var second = Make("Yoge Simple Kit", simple);
        return new([first, second], first.Id, false);
    }
}

using System.Security.Cryptography;
using System.Text.Json;

namespace OsuHitsound.Lanes;

public readonly record struct DrumRenderKey(int Note, int Velocity)
{
    public void Validate()
    {
        if (Note is < 0 or > 127 || Velocity is < 0 or > 127) throw new ArgumentException("Invalid drum MIDI note or velocity.");
    }

    public static bool TryParse(string name, out DrumRenderKey key)
    {
        key = default;
        var parts = Path.GetFileName(name).Split('-');
        if (parts.Length == 6)
        {
            if (parts[5].Length != 20 || !parts[5].EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || !parts[5][..16].All(Uri.IsHexDigit)) return false;
            parts = [parts[0], parts[1], parts[2], parts[3], parts[4] + ".wav"];
        }
        if (parts is not ["dawsu", "vst", var n, var v, var hash] || !n.StartsWith('n') || !v.StartsWith('v') ||
            !int.TryParse(n.AsSpan(1), out int note) || !int.TryParse(v.AsSpan(1), out int velocity) ||
            note is < 0 or > 127 || velocity is < 0 or > 127 || !hash.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
            hash.Length != 68 || !hash.AsSpan(0, 64).ToString().All(Uri.IsHexDigit)) return false;
        key = new(note, velocity); return true;
    }
}

public sealed record DrumRenderPlan(IReadOnlyDictionary<EditHit, DrumRenderKey> Hits)
{
    public IReadOnlyList<DrumRenderKey> Keys => Hits.Values.Distinct().OrderBy(k => k.Note).ThenBy(k => k.Velocity).ToArray();
    public static DrumRenderPlan Create(EditState state, IReadOnlyDictionary<Lane, int> mapping, IReadOnlyDictionary<string, DrumRenderSource>? rendered = null)
    {
        var hits = new Dictionary<EditHit, DrumRenderKey>();
        foreach (var hit in state.Hits)
            if (mapping.ContainsKey(hit.Lane) || rendered?.ContainsKey(ProjectStore.KeyOf(hit.Lane)) == true)
            {
                var source = rendered?.GetValueOrDefault(ProjectStore.KeyOf(hit.Lane));
                int note = source?.Note ?? mapping[hit.Lane];
                int volume = EditSession.VolumeOf(state, hit);
                if (hit.SetPlacement == Guid.Empty) volume = Math.Max(volume, Format.HitsoundRules.StableMinimumVolume);
                var key = new DrumRenderKey(note, source?.Velocity ?? Math.Clamp((int)Math.Round(volume * 127.0 / 100), 0, 127));
                key.Validate(); hits.Add(hit, key);
            }
        return new(hits);
    }
}

public sealed record DrumRenderAsset(DrumRenderKey Key, string FileName, string Hash);

/// <summary>Editor cache only. Every reused WAV is checked against its full content hash.</summary>
public sealed class DrumRenderCache
{
    private readonly Dictionary<string, DrumRenderAsset> entries;
    private readonly string path;
    public static string DefaultRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuHitsoundEditor", "vst-renders");
    public DrumRenderCache(string root, string mapPath)
    {
        path = ProjectStore.PathFor(root, mapPath);
        try
        {
            entries = File.Exists(path) && new FileInfo(path).Length <= 4 * 1024 * 1024
                ? JsonSerializer.Deserialize<Dictionary<string, DrumRenderAsset>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { entries = []; }
    }
    private static string Key(string fingerprint, DrumRenderKey key) => $"{fingerprint}:{key.Note}:{key.Velocity}";
    public DrumRenderAsset? Find(string fingerprint, DrumRenderKey key, string mapDirectory)
    {
        if (!entries.TryGetValue(Key(fingerprint, key), out var entry) || entry is null || string.IsNullOrEmpty(entry.FileName) || entry.Key != key ||
            !DrumRenderKey.TryParse(entry.FileName, out var parsed) || parsed != key || Path.GetFileName(entry.FileName) != entry.FileName) return null;
        try
        {
            using var file = File.OpenRead(Path.Combine(mapDirectory, entry.FileName));
            return Convert.ToHexString(SHA256.HashData(file)) == entry.Hash ? entry : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    public void Remember(string fingerprint, IEnumerable<DrumRenderAsset> assets)
    {
        foreach (var asset in assets) entries[Key(fingerprint, asset.Key)] = asset;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(entries)); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

/// <summary>Installs only completed renders. On failure, rolls back only files this transaction created.</summary>
public sealed class DrumRenderInstall(string mapDirectory) : IDisposable
{
    private readonly List<string> created = [];
    private bool committed;
    public DrumRenderAsset Install(DrumRenderKey key, string stagedWav, string? parent = null)
    {
        key.Validate();
        string hash;
        using (var input = File.OpenRead(stagedWav)) hash = Convert.ToHexString(SHA256.HashData(input));
        string suffix = parent is null ? "" : "-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(parent)))[..16];
        string name = $"dawsu-vst-n{key.Note}-v{key.Velocity}-{hash}{suffix}.wav";
        string target = Path.Combine(mapDirectory, name);
        if (File.Exists(target))
        {
            using var existing = File.OpenRead(target);
            if (Convert.ToHexString(SHA256.HashData(existing)) != hash) throw new IOException($"Existing render {name} has changed; it was not overwritten.");
        }
        else
        {
            using var input = File.OpenRead(stagedWav);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created.Add(target); input.CopyTo(output);
        }
        return new(key, name, hash);
    }
    public void Commit() => committed = true;
    public void Dispose()
    {
        if (committed) return;
        foreach (var file in created)
            try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

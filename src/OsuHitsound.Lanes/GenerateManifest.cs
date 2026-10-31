using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OsuHitsound.Lanes;

/// <summary>A mix Generate wrote: its file, what it contains (<see cref="MixFile.Signature"/>) and its bytes' SHA-256.</summary>
public sealed record GeneratedMix(string FileName, string Signature, string Sha256);

/// <summary>
/// The mixes Generate wrote for one [Hitsounds] diff, kept in the editor's app data (never in the mapset). The next
/// Generate reuses a mix with the same contents (same file, same index) and may remove ones no longer needed, but only
/// files whose bytes still match: a file changed or replaced since is no longer treated as the editor's.
/// </summary>
public sealed record GenerateManifest(IReadOnlyList<GeneratedMix> Mixes)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuHitsoundEditor", "generated");

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>The manifest for a [Hitsounds] diff path, or null when there is none or it can't be read.</summary>
    public static GenerateManifest? Load(string root, string diffPath)
    {
        try
        {
            var path = ProjectStore.PathFor(root, diffPath);
            return File.Exists(path) ? JsonSerializer.Deserialize<GenerateManifest>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // nothing is reused or removed: only safe consequences
        }
    }

    public void Save(string root, string diffPath)
    {
        Directory.CreateDirectory(root);
        SafeFile.WriteAllBytes(ProjectStore.PathFor(root, diffPath), Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Json)));
    }
}

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace OsuHitsound.Audio;

/// <summary>
/// osu!'s classic default hitsounds, read from the osu!lazer install (osu.Game.Resources.dll, "Skins/Legacy"). The
/// file is only read as data: its embedded resources are copied out and no code from it is loaded or run.
/// </summary>
public static class DefaultSkin
{
    private const string Prefix = "osu.Game.Resources.Skins.Legacy.";

    /// <summary>Where osu!lazer keeps its resources on Windows, or null when lazer isn't installed.</summary>
    public static string? FindLazerResources(string? preferred = null)
    {
        if (preferred is { Length: > 0 } && File.Exists(preferred)) return preferred;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osulazer", "current", "osu.Game.Resources.dll");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Sample name without extension (e.g. "soft-hitnormal") → file contents.</summary>
    public static Dictionary<string, byte[]> LoadSamples(string resourcesDll)
    {
        var samples = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var stream = File.OpenRead(resourcesDll);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var directory = pe.PEHeaders.CorHeader?.ResourcesDirectory ?? default;
        if (directory.Size == 0) return samples;
        var block = pe.GetSectionData(directory.RelativeVirtualAddress);

        foreach (var handle in metadata.ManifestResources)
        {
            var resource = metadata.GetManifestResource(handle);
            var name = metadata.GetString(resource.Name);
            var ext = Path.GetExtension(name);
            if (!resource.Implementation.IsNil || !name.StartsWith(Prefix, StringComparison.Ordinal) || ext is not (".wav" or ".ogg" or ".mp3"))
                continue;
            if (resource.Offset < 0 || resource.Offset + 4 > directory.Size) continue;

            // Each embedded resource is a 4-byte length followed by the bytes.
            var reader = block.GetReader((int)resource.Offset, directory.Size - (int)resource.Offset);
            int length = reader.ReadInt32();
            if (length < 0 || length > reader.RemainingBytes) continue;
            samples[name[Prefix.Length..^ext.Length]] = reader.ReadBytes(length);
        }

        return samples;
    }
}

using System.IO.Compression;

namespace OsuHitsound.Lanes;

/// <summary>
/// File → Import .osz (docs/spec-wave2.md, part 8): unpacks a beatmap archive into a new folder in the Songs folder.
/// Every entry must land inside that folder; anything else stops the import and removes what it created.
/// </summary>
public static class OszImport
{
    private static readonly uint[] CrcTable = MakeCrcTable();

    /// <summary>The most an archive may unpack to (a defence against zip bombs): 2 GB.</summary>
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Unpacks <paramref name="oszPath"/> into songs/&lt;archive name&gt;, or "&lt;name&gt; (2)" and so on when that exists,
    /// never into an existing folder. Throws <see cref="InvalidDataException"/> for an unsafe or oversized archive.
    /// Returns the new folder.
    /// </summary>
    public static string Extract(string oszPath, string songsFolder, long maxBytes = MaxBytes)
    {
        using var zip = ZipFile.OpenRead(oszPath);
        var songs = Path.GetFullPath(songsFolder);
        if (!Directory.Exists(songs)) throw new DirectoryNotFoundException("The Songs folder does not exist.");
        var folderName = SafeFile.Name(Path.GetFileNameWithoutExtension(oszPath)).TrimEnd(' ', '.');
        if (folderName.Length == 0) throw new InvalidDataException("The archive needs a valid name.");
        // Validate using a placeholder root; relative paths are applied to the claimed folder below.
        var root = Path.Combine(songs, "import-placeholder") + Path.DirectorySeparatorChar;

        // Check every entry before writing anything.
        var files = new List<(ZipArchiveEntry Entry, string Relative)>();
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            bool directory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            var name = directory ? entry.FullName[..^1] : entry.FullName;
            var path = SafePath(root, name) ?? throw new InvalidDataException($"The archive has an unsafe path: \"{entry.FullName}\".");
            if (directory) continue;
            if (entry.Length > maxBytes - total) throw new InvalidDataException("The archive unpacks to more than the import allows.");
            total += entry.Length;
            files.Add((entry, Path.GetRelativePath(root, path)));
        }

        // Move an empty, uniquely named staging directory into place. Directory.Move fails if a competing
        // import has already claimed that name, so we never extract into someone else's existing folder.
        var staging = Path.Combine(songs, ".dawsu-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string target;
        try
        {
            while (true)
            {
                target = FreeFolder(songs, folderName);
                try { Directory.Move(staging, target); break; }
                catch (IOException) when (Directory.Exists(target) || File.Exists(target)) { }
            }
        }
        catch
        {
            Directory.Delete(staging, recursive: true);
            throw;
        }
        try
        {
            long extracted = 0;
            var buffer = new byte[81920];
            foreach (var (entry, relative) in files)
            {
                var path = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var input = entry.Open();
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                uint crc = uint.MaxValue;
                int count;
                while ((count = input.Read(buffer)) != 0)
                {
                    if (count > maxBytes - extracted) throw new InvalidDataException("The archive unpacks to more than the import allows.");
                    for (int i = 0; i < count; i++) crc = CrcTable[(crc ^ buffer[i]) & 0xff] ^ (crc >> 8);
                    output.Write(buffer, 0, count);
                    extracted += count;
                }
                if (~crc != entry.Crc32) throw new InvalidDataException($"The archive entry \"{entry.FullName}\" is corrupt.");
            }
        }
        catch
        {
            Directory.Delete(target, recursive: true);
            throw;
        }

        return target;
    }

    /// <summary>The .osu files at the top of an unpacked folder, sorted by name.</summary>
    public static IReadOnlyList<string> Difficulties(string folder) => [.. Directory.EnumerateFiles(folder, "*.osu").Order(StringComparer.OrdinalIgnoreCase)];

    // songs/name, or songs/name (2), (3)... the first that doesn't exist yet.
    private static string FreeFolder(string songs, string name)
    {
        var path = Path.Combine(songs, name);
        for (int n = 2; Directory.Exists(path) || File.Exists(path); n++) path = Path.Combine(songs, $"{name} ({n})");
        return path;
    }

    // Where an entry unpacks, or null when it would leave the folder or Windows can't store its name: absolute paths,
    // drive letters, "..", alternate data streams (':') and other characters invalid in file names.
    private static string? SafePath(string root, string name)
    {
        var parts = name.Split('/', '\\');
        if (parts.Length == 0 || name.StartsWith('/') || name.StartsWith('\\')) return null;
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var part in parts)
            if (part.Length == 0 || part is "." or ".." || part.IndexOfAny(invalid) >= 0 || part.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0 || part.Any(c => c < 32) || part.EndsWith('.') || part.EndsWith(' ') || IsDeviceName(part))
                return null;

        var full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static bool IsDeviceName(string part)
    {
        var stem = part.Split('.')[0].TrimEnd(' ');
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³');
    }

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
            table[i] = crc;
        }
        return table;
    }
}

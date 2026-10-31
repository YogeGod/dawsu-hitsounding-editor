using System.IO.Compression;

namespace OsuHitsound.Lanes;

/// <summary>
/// File → Import .osz (docs/spec-wave2.md, part 8): unpacks a beatmap archive into a new folder in the Songs folder.
/// Every entry must land inside that folder; anything else stops the import and removes what it created.
/// </summary>
public static class OszImport
{
    /// <summary>The most an archive may unpack to (a defence against zip bombs): 2 GB.</summary>
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>The most entries an archive may have (a beatmap folder has a few hundred at most).</summary>
    public const int MaxEntries = 20000;

    private static readonly HashSet<string> DeviceNames = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(0, 10).SelectMany(i => new[] { $"COM{i}", $"LPT{i}" })], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Unpacks <paramref name="oszPath"/> into songs/&lt;archive name&gt;, or "&lt;name&gt; (2)" and so on when that exists,
    /// never into an existing folder. Throws <see cref="InvalidDataException"/> for an unsafe or oversized archive.
    /// Returns the new folder.
    /// </summary>
    public static string Extract(string oszPath, string songsFolder, long maxBytes = MaxBytes)
    {
        using var zip = ZipFile.OpenRead(oszPath);
        if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("The archive has more files than a beatmap would.");
        var songs = Path.GetFullPath(songsFolder);
        // Windows drops trailing dots and spaces from folder names ("Hello..osz" -> "Hello."), so they go first.
        var name = SafeFile.Name(Path.GetFileNameWithoutExtension(oszPath)).TrimEnd('.', ' ') is { Length: > 0 } n && !IsDevice(n) ? n : "Imported beatmap";

        // Unpacked into a fresh hidden folder first, then moved into place: the move never merges into a folder that
        // appeared meanwhile, and a failure removes only the folder this import made.
        var staging = Path.Combine(songs, $".importing-{Guid.NewGuid():N}");
        var root = staging + Path.DirectorySeparatorChar;

        // Check every entry before writing anything.
        var files = new List<(ZipArchiveEntry Entry, string Path)>();
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue; // a folder
            var path = SafePath(root, entry.FullName) ?? throw new InvalidDataException($"The archive has an unsafe path: \"{entry.FullName}\".");
            total += entry.Length;
            if (total > maxBytes) throw new InvalidDataException("The archive unpacks to more than the import allows.");
            files.Add((entry, path));
        }

        Directory.CreateDirectory(staging);
        try
        {
            foreach (var (entry, path) in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: false); // two entries for one file (case) fail here instead of overwriting
            }

            for (int tries = 0; ; tries++)
            {
                var target = FreeFolder(songs, name);
                try
                {
                    Directory.Move(staging, target);
                    return target;
                }
                catch (IOException) when (tries < 20 && Directory.Exists(target))
                {
                    // Taken between the check and the move: try the next number.
                }
            }
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    /// <summary>The .osu files at the top of an unpacked folder, sorted by name.</summary>
    public static IReadOnlyList<string> Difficulties(string folder) => [.. Directory.EnumerateFiles(folder, "*.osu").Order(StringComparer.OrdinalIgnoreCase)];

    // Windows device names (CON, NUL, COM1...) open the device instead of a file, with or without an extension.
    private static bool IsDevice(string part) => DeviceNames.Contains(part.Split('.')[0].TrimEnd(' '));

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
            if (part.Length == 0 || part is "." or ".." || part.IndexOfAny(invalid) >= 0 || part.Contains(':') || part.EndsWith('.') || part.EndsWith(' ') || IsDevice(part))
                return null;

        var full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}

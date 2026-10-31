namespace OsuHitsound.Lanes;

/// <summary>
/// The files of one mapset folder, looked up the way osu! does on Windows (case-insensitive). Names that would leave
/// the folder (absolute, UNC, or "..\") are rejected by string checks alone, before any file-system call: a map from
/// someone else must never make the editor read, or even probe, a file outside its own folder.
/// </summary>
public sealed class MapFolder
{
    // Relative path with '/' separators → the name as it is on disk.
    private readonly Dictionary<string, string> files;

    /// <summary>Every file in the folder, as named on disk ('/' separators).</summary>
    public IEnumerable<string> Files => files.Values;
    private readonly Func<string, byte[]>? readBytes;

    public MapFolder(IEnumerable<string> relativePaths, Func<string, byte[]>? readBytes = null)
    {
        files = new(StringComparer.OrdinalIgnoreCase);
        foreach (var path in relativePaths)
            files.TryAdd(Normalize(path), path.Replace('\\', '/'));
        this.readBytes = readBytes;
    }

    /// <summary>
    /// This folder with more file names (files about to be written, such as Generate's mixes). It reads files the same
    /// way, so the files already there (a storyboard .osb, say) can still be read.
    /// </summary>
    public MapFolder With(IEnumerable<string> moreFiles) => new(Files.Concat(moreFiles), readBytes);

    public static MapFolder FromDirectory(string directory)
    {
        var root = Path.GetFullPath(directory);
        // Junctions and symlinks are skipped: they could lead outside the folder past the string checks.
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        var names = Directory.EnumerateFiles(root, "*", options).Select(f => Path.GetRelativePath(root, f));
        return new MapFolder(names, rel => File.ReadAllBytes(Path.Combine(root, rel)));
    }

    /// <summary>The on-disk name of a file in the folder, or null when it's missing or the name points outside the folder.</summary>
    public string? Find(string relativePath) =>
        IsInside(relativePath) && files.TryGetValue(Normalize(relativePath), out var actual) ? actual : null;

    /// <summary>The first of name.wav, name.mp3, name.ogg that exists (osu!'s lookup order).</summary>
    public string? FindSample(string nameWithoutExtension) =>
        Format.HitsoundRules.SampleExtensions.Select(ext => Find(nameWithoutExtension + ext)).FirstOrDefault(f => f is not null);

    public IEnumerable<string> FilesWithExtension(string extension) =>
        files.Values.Where(f => !f.Contains('/') && f.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>The contents of a file found by <see cref="Find"/>; throws for names outside the folder or missing.</summary>
    public byte[] ReadBytes(string relativePath) =>
        readBytes?.Invoke(Find(relativePath) ?? throw new FileNotFoundException(relativePath)) ?? throw new InvalidOperationException("This folder has no file contents.");

    /// <summary>True when the path stays inside the folder: relative, no drive, no UNC, and ".." never climbs above the root.</summary>
    public static bool IsInside(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains(':') || relativePath.StartsWith('/') || relativePath.StartsWith('\\'))
            return false;

        int depth = 0;
        foreach (var part in relativePath.Split('/', '\\'))
        {
            if (part == "..") depth--;
            else if (part is not ("" or ".")) depth++;
            if (depth < 0) return false;
        }

        return depth > 0;
    }

    private static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/', '\\'))
        {
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (part is not ("" or ".")) parts.Add(part);
        }

        return string.Join('/', parts);
    }
}

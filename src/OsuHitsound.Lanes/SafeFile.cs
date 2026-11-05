namespace OsuHitsound.Lanes;

/// <summary>File-writing rules shared by Save, Generate and the project store.</summary>
public static class SafeFile
{
    /// <summary>
    /// Writes through a temporary file next to the target and moves it into place, so a failed write never leaves a
    /// half-written file; the temporary file is always removed.
    /// </summary>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var temp = path + ".saving";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>parent/name, or parent/name (2), (3)… the first that doesn't exist yet.</summary>
    public static string FreeFolder(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        for (int n = 2; Directory.Exists(path) || File.Exists(path); n++) path = Path.Combine(parent, $"{name} ({n})");
        return path;
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or inside it (full paths, case-insensitive).</summary>
    public static bool IsInside(string path, string folder)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A name safe to use as a file or folder name: characters Windows doesn't allow become '_'.</summary>
    public static string Name(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    /// <summary>Keeps a timestamped copy of <paramref name="bytes"/> under backupRoot/&lt;map name&gt; (outside the mapset).</summary>
    public static string Backup(string backupRoot, string osuPath, byte[] bytes)
    {
        var folder = Path.Combine(backupRoot, Name(Path.GetFileNameWithoutExtension(osuPath)));
        Directory.CreateDirectory(folder);
        var backup = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.osu");
        File.WriteAllBytes(backup, bytes);
        return backup;
    }
}

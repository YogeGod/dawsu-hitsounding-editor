namespace OsuHitsound.Lanes;

/// <summary>File-writing rules shared by Save, Generate and the project store.</summary>
public static class SafeFile
{
    /// <summary>
    /// Writes through a temporary file next to the target and moves it into place, so a failed write never leaves a
    /// half-written file; the temporary file is always removed. The data reaches the disk before the move, so a crash or
    /// power cut right after can't leave the rename without its contents (an empty file).
    /// </summary>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var temp = path + ".saving";
        try
        {
            WriteTemp(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>
    /// Like <see cref="WriteAllBytes"/>, but only when <paramref name="path"/> still holds exactly <paramref name="expected"/>
    /// (null: only when it doesn't exist yet). The file is checked while it is held open against other writers, and stays
    /// held while the new bytes are prepared, so another program (osu! saving, say) can't slip a write in between the check
    /// and the replace. That leaves only the instant between letting go and the move itself.
    /// </summary>
    /// <exception cref="IOException"><paramref name="changedMessage"/> when the file isn't as expected (nothing was
    /// written), or the file couldn't be written.</exception>
    public static void ReplaceIfUnchanged(string path, byte[]? expected, byte[] bytes, string changedMessage)
    {
        var temp = path + ".saving";
        try
        {
            if (expected is null)
            {
                if (File.Exists(path)) throw new IOException(changedMessage);
                WriteTemp(temp, bytes);
                File.Move(temp, path, overwrite: false); // created meanwhile: the move fails rather than replace it
                return;
            }

            FileStream guard;
            try { guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); } // readers welcome, writers wait
            catch (FileNotFoundException) { throw new IOException(changedMessage); }
            using (guard)
            {
                if (!Holds(guard, expected)) throw new IOException(changedMessage);
                WriteTemp(temp, bytes);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    // The data reaches the disk before the caller's move, so a crash right after can't leave an empty file.
    private static void WriteTemp(string temp, byte[] bytes)
    {
        using var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static bool Holds(FileStream stream, byte[] expected)
    {
        if (stream.Length != expected.Length) return false;
        var actual = new byte[expected.Length];
        stream.ReadExactly(actual);
        return actual.AsSpan().SequenceEqual(expected);
    }

    /// <summary>
    /// Moves a file that couldn't be read aside as path.unreadable so the next save doesn't overwrite it. An earlier
    /// .unreadable copy is kept: the new one gets a timestamp. Returns where it went, or null when it couldn't be moved.
    /// </summary>
    public static string? KeepUnreadable(string path)
    {
        try
        {
            var target = path + ".unreadable";
            if (File.Exists(target)) target = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss-fff}.unreadable";
            File.Move(path, target, overwrite: false);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
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

    /// <summary>How many backups each difficulty keeps (issue #29); older ones are removed as new ones are taken.</summary>
    public const int BackupsKept = 20;

    // A backup's name: its time, then a counter for backups taken in the same millisecond ("20261112-130701-123-01").
    private static readonly System.Text.RegularExpressions.Regex BackupName = new(@"^\d{8}-\d{6}-\d{3}(-\d{2,})?$");

    /// <summary>The folder a difficulty's backups go in: backupRoot/&lt;map name&gt; (outside the mapset).</summary>
    public static string BackupFolder(string backupRoot, string osuPath) => Path.Combine(backupRoot, Name(Path.GetFileNameWithoutExtension(osuPath)));

    /// <summary>
    /// Keeps a timestamped copy of <paramref name="bytes"/> in the difficulty's backup folder, never replacing another
    /// backup (one taken in the same millisecond gets a counter), then removes the oldest beyond <see cref="BackupsKept"/>.
    /// Only files named like backups are ever removed.
    /// </summary>
    public static string Backup(string backupRoot, string osuPath, byte[] bytes)
    {
        var folder = BackupFolder(backupRoot, osuPath);
        Directory.CreateDirectory(folder);
        string stamp = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}";
        string backup = Path.Combine(folder, stamp + ".osu");
        for (int n = 1; ; n++)
        {
            try
            {
                using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes);
                break;
            }
            catch (IOException) when (File.Exists(backup))
            {
                backup = Path.Combine(folder, $"{stamp}-{n:00}.osu");
            }
        }

        Prune(folder);
        return backup;
    }

    // Oldest first: by time, then counter (a name without a counter is the first of its millisecond).
    private static void Prune(string folder)
    {
        var backups = Directory.EnumerateFiles(folder, "*.osu")
            .Where(f => BackupName.IsMatch(Path.GetFileNameWithoutExtension(f)))
            .OrderBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal)
            .ToList();
        foreach (var old in backups.Take(Math.Max(0, backups.Count - BackupsKept)))
        {
            try { File.Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // kept; tried again next time
        }
    }
}

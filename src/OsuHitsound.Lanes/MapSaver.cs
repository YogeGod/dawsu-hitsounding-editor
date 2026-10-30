using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>What <see cref="MapSaver.Save"/> did: the compile result, and the backup path (null when nothing was written).</summary>
public sealed record SaveResult(CompileResult Compile, string? Backup, bool Written);

/// <summary>
/// Saves wanted sounds into a .osu: compiles a fresh copy of the map as it was opened, refuses when the file changed on
/// disk since, keeps a timestamped backup under <c>backupRoot</c> (outside the mapset), and replaces the .osu through a
/// temporary file next to it. Only the .osu is written; nothing else in the mapset folder is touched.
/// </summary>
public static class MapSaver
{
    public static string DefaultBackupRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuHitsoundEditor", "backups");

    /// <exception cref="IOException">The file changed on disk since it was opened, or couldn't be written.</exception>
    public static SaveResult Save(string osuPath, byte[] opened, MapFolder folder, IReadOnlyDictionary<MomentKey, WantedSound> wanted, string backupRoot)
    {
        if (!File.ReadAllBytes(osuPath).AsSpan().SequenceEqual(opened))
            throw new IOException("The .osu changed on disk since it was opened (another program?). Reopen it to edit the new version.");

        var copy = OsuFile.Parse(opened);
        var result = Compiler.Apply(copy, folder, wanted);
        if (!copy.IsEdited) return new SaveResult(result, null, false);

        var invalid = Path.GetInvalidFileNameChars();
        var name = string.Concat(Path.GetFileNameWithoutExtension(osuPath).Select(c => invalid.Contains(c) ? '_' : c));
        var backups = Path.Combine(backupRoot, name);
        Directory.CreateDirectory(backups);
        var backup = Path.Combine(backups, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.osu");
        File.WriteAllBytes(backup, opened);

        // Through a temporary file, so a failed write never leaves a half-written .osu; never leave the temp file behind.
        var temp = osuPath + ".saving";
        try
        {
            File.WriteAllBytes(temp, copy.ToBytes());
            File.Move(temp, osuPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return new SaveResult(result, backup, true);
    }
}

namespace OsuHitsound.Lanes;

/// <summary>
/// Writes a <see cref="GeneratePlan"/> into the mapset, all or nothing: every mix is rendered first (a mix none of
/// whose sounds can be read stops it), no existing file is ever overwritten except the [Hitsounds] diff itself (backed
/// up first, outside the mapset), and if anything fails the mixes already written are removed again.
/// </summary>
public static class GenerateWriter
{
    /// <param name="render">Renders a mix to WAV bytes; null when none of its sounds can be read.</param>
    /// <returns>The backup of the replaced [Hitsounds] diff, or null when there was none.</returns>
    /// <exception cref="IOException">Nothing (or nothing lasting) was written.</exception>
    public static string? Write(GeneratePlan plan, Func<MixFile, byte[]?> render, string backupRoot)
    {
        var dir = Path.GetDirectoryName(plan.OsuPath)!;
        var mixes = plan.Mixes.Select(m => (Path: Path.Combine(dir, m.FileName),
            Bytes: render(m) ?? throw new IOException($"Couldn't make {m.FileName}: none of its sounds could be read."))).ToList();
        if (mixes.FirstOrDefault(m => File.Exists(m.Path)) is { Path: { } taken })
            throw new IOException($"{Path.GetFileName(taken)} already exists in the mapset; not overwriting it.");

        var written = new List<string>();
        try
        {
            foreach (var (path, bytes) in mixes)
            {
                SafeFile.WriteAllBytes(path, bytes);
                written.Add(path);
            }

            string? backup = File.Exists(plan.OsuPath) ? SafeFile.Backup(backupRoot, plan.OsuPath, File.ReadAllBytes(plan.OsuPath)) : null;
            SafeFile.WriteAllBytes(plan.OsuPath, plan.Diff.ToBytes());
            return backup;
        }
        catch
        {
            foreach (var path in written)
                if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }
}

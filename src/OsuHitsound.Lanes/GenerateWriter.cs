namespace OsuHitsound.Lanes;

/// <summary>What Generate wrote: the backup of a replaced [Hitsounds] diff (or null), and the record of its mixes for next time.</summary>
public sealed record GenerateResult(string? Backup, GenerateManifest Manifest);

/// <summary>
/// Writes a <see cref="GeneratePlan"/> into the mapset, all or nothing: every mix is rendered first (a mix none of
/// whose sounds can be read stops it), no existing file is ever overwritten except the [Hitsounds] diff itself (backed
/// up first, outside the mapset) and the previous Generate's own unneeded mixes, and if anything fails the files
/// already written are removed or put back. Unneeded mixes are removed only after the new diff is in place.
/// </summary>
public static class GenerateWriter
{
    /// <param name="render">Renders a mix to WAV bytes; null when none of its sounds can be read.</param>
    /// <exception cref="IOException">Nothing (or nothing lasting) was written.</exception>
    public static GenerateResult Write(GeneratePlan plan, Func<MixFile, byte[]?> render, string backupRoot)
    {
        var dir = Path.GetDirectoryName(plan.OsuPath)!;
        var removable = plan.Removes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mixes = plan.Mixes.Select(m => (Mix: m, Path: Path.Combine(dir, m.FileName),
            Bytes: render(m) ?? throw new IOException($"Couldn't make {m.FileName}: none of its sounds could be read."))).ToList();
        if (mixes.FirstOrDefault(m => File.Exists(m.Path) && !removable.Contains(m.Mix.FileName)) is { Path: { } taken })
            throw new IOException($"{Path.GetFileName(taken)} already exists in the mapset; not overwriting it.");

        var written = new List<(string Path, byte[]? Before)>();
        try
        {
            foreach (var (_, path, bytes) in mixes)
            {
                byte[]? before = File.Exists(path) ? File.ReadAllBytes(path) : null; // one of our unneeded mixes
                SafeFile.WriteAllBytes(path, bytes);
                written.Add((path, before));
            }

            string? backup = File.Exists(plan.OsuPath) ? SafeFile.Backup(backupRoot, plan.OsuPath, File.ReadAllBytes(plan.OsuPath)) : null;
            SafeFile.WriteAllBytes(plan.OsuPath, plan.Diff.ToBytes());

            var overwritten = mixes.Select(m => m.Mix.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in removable.Where(n => !overwritten.Contains(n)))
            {
                try
                {
                    File.Delete(Path.Combine(dir, name));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Left behind: harmless (nothing uses it), and it is no longer recorded as ours.
                }
            }

            var manifest = new GenerateManifest([.. plan.Reused, .. mixes.Select(m => new GeneratedMix(m.Mix.FileName, m.Mix.Signature, GenerateManifest.Hash(m.Bytes)))]);
            return new GenerateResult(backup, manifest);
        }
        catch
        {
            foreach (var (path, before) in written)
            {
                if (before is not null) SafeFile.WriteAllBytes(path, before);
                else if (File.Exists(path)) File.Delete(path);
            }

            throw;
        }
    }
}

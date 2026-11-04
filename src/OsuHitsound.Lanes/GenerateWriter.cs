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
    public static GenerateResult Write(GeneratePlan plan, Func<MixFile, byte[]?> render, string backupRoot, byte[]? expectedOriginal = null)
    {
        if (plan.Conflicts.Count > 0) throw new IOException($"Nothing was written: {plan.Conflicts[0].Reason}");
        foreach (string name in plan.Mixes.Select(m => m.FileName).Concat(plan.Reused.Select(m => m.FileName)).Concat(plan.Removes))
            if (!MapFolder.IsInside(name) || Path.GetFileName(name) != name) throw new IOException("An output sample path is outside the mapset.");
        var dir = Path.GetDirectoryName(plan.OsuPath)!;
        var removable = plan.Removes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mixes = plan.Mixes.Select(m => (Mix: m, Path: Path.Combine(dir, m.FileName),
            Bytes: Prepare(m))).ToList();
        byte[] Prepare(MixFile mix)
        {
            var rendered = render(mix) ?? throw new IOException($"Couldn't make {mix.FileName}: its sounds could not be read.");
            return mix.CopyBytes ?? rendered;
        }
        void CheckOriginal()
        {
            if (plan.ExpectedOutputHash is { } expectedHash && (File.Exists(plan.OsuPath) ? GenerateManifest.Hash(File.ReadAllBytes(plan.OsuPath)) : "missing") != expectedHash)
                throw new IOException("The output difficulty changed while export was being prepared. Review and retry.");
            foreach (var (name, hash) in plan.InputFiles)
            {
                if (!MapFolder.IsInside(name)) throw new IOException("A source sample path is outside the mapset.");
                var staged = mixes.FirstOrDefault(m => string.Equals(m.Mix.FileName, name, StringComparison.OrdinalIgnoreCase));
                var actual = staged.Bytes is not null ? staged.Bytes : File.ReadAllBytes(Path.Combine(dir, name));
                if (GenerateManifest.Hash(actual) != hash) throw new IOException($"{name} changed while export was being prepared. Review and retry.");
            }
            if (expectedOriginal is not null && !File.ReadAllBytes(plan.OsuPath).AsSpan().SequenceEqual(expectedOriginal))
                throw new IOException("The .osu changed on disk. Reopen it before saving; no map edits were overwritten.");
            foreach (var mix in plan.Reused)
                if (!File.Exists(Path.Combine(dir, mix.FileName)) || GenerateManifest.Hash(File.ReadAllBytes(Path.Combine(dir, mix.FileName))) != mix.Sha256)
                    throw new IOException("A reused generated sample changed on disk. Prepare export again before writing the map.");
        }
        CheckOriginal();
        if (expectedOriginal is not null && mixes.Count == 0 && removable.Count == 0 && plan.Diff.ToBytes().AsSpan().SequenceEqual(expectedOriginal))
            return new GenerateResult(null, new GenerateManifest(plan.Reused) { LastIndex = plan.LastMixIndex });
        if (mixes.FirstOrDefault(m => File.Exists(m.Path) && !removable.Contains(m.Mix.FileName)) is { Path: { } taken })
            throw new IOException($"{Path.GetFileName(taken)} already exists in the mapset; not overwriting it.");

        var written = new List<(string Path, byte[]? Before)>();
        try
        {
            foreach (var (_, path, bytes) in mixes)
            {
                if (File.Exists(path) && !removable.Contains(Path.GetFileName(path))) throw new IOException($"{Path.GetFileName(path)} was created while export was being prepared; it was not overwritten.");
                byte[]? before = File.Exists(path) ? File.ReadAllBytes(path) : null; // one of our unneeded mixes
                if (before is null)
                {
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                    written.Add((path, null));
                    stream.Write(bytes);
                }
                else
                {
                    written.Add((path, before));
                    SafeFile.WriteAllBytes(path, bytes);
                }
            }

            CheckOriginal();
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

            var manifest = new GenerateManifest([.. plan.Reused, .. mixes.Select(m => new GeneratedMix(m.Mix.FileName, m.Mix.Signature, GenerateManifest.Hash(m.Bytes)) { InputHash = plan.InputHashes.GetValueOrDefault(m.Mix.Signature) })])
            {
                LastIndex = plan.LastMixIndex,
            };
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

using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>
/// One target of a planned Copy: what it would write (its compiled .osu and new samples), its conflicts and the change
/// summary against the bytes it was read from, or why it can't be copied into. Nothing has been written.
/// </summary>
public sealed record CopyTargetPlan(string Path, string? Error)
{
    /// <summary>The bytes the target was read from; the write is refused if the file no longer has them.</summary>
    public byte[] Opened { get; init; } = [];

    /// <summary>The compiled target, its new samples and its conflicts, written with <see cref="GenerateWriter"/>.</summary>
    public GeneratePlan? Storage { get; init; }

    /// <summary>What the copy found (matches, changes, mutes, silent hitnormals kept).</summary>
    public HitsoundCopyPlan? Copy { get; init; }

    /// <summary>What writing it changes in the .osu.</summary>
    public WriteSummary? Changes { get; init; }

    /// <summary>Sounds were paired (Save's rules: a conflict can't be written as it is).</summary>
    public bool Paired { get; init; }

    /// <summary>Moments whose sounds can't be written; accepting the target writes the rest and these keep their old sounds.</summary>
    public IReadOnlyList<Conflict> Conflicts => Storage?.Conflicts ?? [];

    /// <summary>Planned without an error or a conflict: written unless the mapper unticks it.</summary>
    public bool Clean => Error is null && Conflicts.Count == 0;
}

/// <summary>Every target of one Copy, planned before anything is written (issue #28).</summary>
public sealed record CopyBatch(IReadOnlyList<CopyTargetPlan> Targets);

public static partial class HitsoundCopier
{
    /// <summary>
    /// Plans the copy into every target without writing anything: each target is read from disk, compiled, and compared
    /// with what it was. A target that can't be read or planned gets an <see cref="CopyTargetPlan.Error"/>.
    /// </summary>
    /// <param name="canRender">A renderer will be given to <see cref="WriteCopy"/>: needed when new mixed samples are.</param>
    public static CopyBatch PlanCopy(OsuFile source, MapFolder sourceFolder, IEnumerable<string> targetPaths, HitsoundCopyOptions options, bool canRender,
        ExportMergeSettings? mergeSettings = null, IReadOnlyDictionary<string, byte[]>? skin = null,
        IReadOnlyList<MixFile>? sourceSamples = null, IReadOnlyDictionary<string, string>? sourceInputs = null)
    {
        var targets = new List<CopyTargetPlan>();
        CustomSampleIndexes? indexes = null;
        var pairings = new Dictionary<string, PairingPlanner>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in targetPaths)
        {
            try
            {
                targets.Add(PlanTarget(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
            {
                targets.Add(new CopyTargetPlan(path, ex.Message));
            }
        }

        return new CopyBatch(targets);

        CopyTargetPlan PlanTarget(string path)
        {
            var doc = MapDocument.Load(path);
            var sourceAssets = sourceSamples ?? [];
            var stagedSource = sourceAssets.ToDictionary(m => m.FileName, m => m.CopyBytes ?? throw new IOException("A source sample must be prepared before Copy."), StringComparer.OrdinalIgnoreCase);
            var workingFolder = new MapFolder(doc.Folder.Files.Concat(stagedSource.Keys), name => stagedSource.TryGetValue(name, out var raw) ? raw : doc.Folder.ReadBytes(name));
            if (canRender) indexes ??= new CustomSampleIndexes(doc.Folder, options.StartIndex); // one set of indexes for the mapset
            bool paired = mergeSettings?.PairSamples == true;
            PairingPlanner? pairing = null;
            if (paired)
            {
                string directory = Path.GetDirectoryName(doc.Path)!;
                if (!pairings.TryGetValue(directory, out pairing))
                    pairings[directory] = pairing = new PairingPlanner(new LaneAudio(workingFolder, skin), mergeSettings!);
            }

            var plan = Plan(source, sourceFolder, doc.File, workingFolder, options, indexes, mergeSettings, skin, pairing);
            if (!canRender && plan.Mixes.Count > 0)
                throw new IOException("Simultaneous source sounds require a sample renderer; the target was not changed.");

            var withMixes = workingFolder.With(plan.Mixes.Select(m => m.FileName));
            var compiledFile = OsuFile.Parse(doc.Opened);
            var compiled = Compiler.Apply(compiledFile, withMixes, plan.Wanted, mergeSettings?.Style ?? ExportStyle.PerObject);
            GeneratePlan storage;
            if (paired)
            {
                foreach (var asset in sourceAssets.Where(m => doc.Folder.Find(m.FileName) is not null))
                    if (!doc.Folder.ReadBytes(asset.FileName).AsSpan().SequenceEqual(asset.CopyBytes))
                        throw new IOException($"{asset.FileName} changed while Copy was being prepared.");
                if (pairing is not null) compiled = compiled with { Conflicts = pairing.Conflicts.Concat(compiled.Conflicts).DistinctBy(c => c.Moment).ToList() };
                if (compiled.Conflicts.FirstOrDefault(c => c.Unverified) is { } unverified) throw new IOException($"Nothing was saved: {unverified.Reason}");
                plan.ApplyExtras(compiledFile);
                // Only the new files the compiled map refers to are written.
                var referenced = Decompiler.Decompile(compiledFile, withMixes);
                var usedFiles = referenced.Moments.SelectMany(m => m.Hits.Select(h => h.Lane)).Concat(referenced.Passive.Select(p => p.Lane))
                    .Where(l => l.Source == LaneSource.BeatmapFile).Select(l => withMixes.Find(l.Name) ?? l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var toWrite = sourceAssets.Concat(plan.Mixes).DistinctBy(m => m.FileName)
                    .Where(m => doc.Folder.Find(m.FileName) is null && usedFiles.Contains(m.FileName)).ToList();
                var writingNames = toWrite.Select(m => m.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var physicalInputs = (pairing?.Audio.FilesRead ?? new Dictionary<string, string>())
                    .Where(p => !stagedSource.ContainsKey(p.Key) || writingNames.Contains(p.Key) || doc.Folder.Find(p.Key) is not null);
                // Source files are checked where they are in this target's folder; one elsewhere can't be read from here.
                var sourceInFolder = (sourceInputs ?? new Dictionary<string, string>()).Where(p => doc.Folder.Find(p.Key) is not null);
                storage = new GeneratePlan(doc.Path, compiledFile, toWrite, 0, compiled.Conflicts)
                { InputFiles = sourceInFolder.Concat(physicalInputs).GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Value) };
            }
            else
            {
                // This conflict means the compiled map would play something nobody asked for: never written.
                if (compiled.Conflicts.FirstOrDefault(c => c.Unverified) is { } wrong) throw new IOException($"Nothing was saved: {wrong.Reason}");
                plan.ApplyExtras(compiledFile);
                // New tick and slide samples (and hit mixes) that the mapset doesn't have yet.
                var toWrite = plan.Mixes.Where(m => doc.Folder.Find(m.FileName) is null).ToList();
                storage = new GeneratePlan(doc.Path, compiledFile, toWrite, 0, compiled.Conflicts);
            }

            return new CopyTargetPlan(doc.Path, null)
            {
                Opened = doc.Opened, Storage = storage, Copy = plan, Paired = paired,
                Changes = WriteSummary.Compare(OsuFile.Parse(doc.Opened), compiledFile),
            };
        }
    }

    /// <summary>
    /// Writes the ticked targets of a planned Copy, all or nothing. Each target is written as Save writes (refused if it
    /// changed on disk since it was planned, with a backup outside the mapset); ticking a target with conflicts accepts
    /// them (those moments keep their old sounds). If any write fails, every target already written is put back to the
    /// bytes it was planned from and the sample files this Copy created are removed. Targets not ticked are skipped.
    /// </summary>
    /// <returns>One result per target in the batch, in order.</returns>
    public static IReadOnlyList<HitsoundCopyTarget> WriteCopy(CopyBatch batch, IEnumerable<string> tickedPaths, string backupRoot, Func<MixFile, byte[]?>? render)
    {
        var ticked = tickedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var done = new List<(CopyTargetPlan Target, string? Backup, int Generated)>();
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // sample files this Copy made
        string? failure = null;
        CopyTargetPlan? failed = null;

        foreach (var target in batch.Targets.Where(t => t.Error is null && t.Storage is not null && ticked.Contains(t.Path)))
        {
            try
            {
                var dir = Path.GetDirectoryName(target.Path)!;
                // A sample another target of this Copy already made is the same file (one set of indexes per mapset).
                var mixes = target.Storage!.Mixes.Where(m => !created.Contains(Path.Combine(dir, m.FileName))).ToList();
                var before = mixes.Select(m => Path.Combine(dir, m.FileName)).Where(p => !File.Exists(p)).ToList();
                var storage = target.Storage with { Mixes = mixes, Conflicts = [] }; // ticked: its conflicts are accepted
                var written = GenerateWriter.Write(storage, render ?? (_ => throw new IOException("Sample rendering is unavailable.")), backupRoot, target.Opened);
                foreach (var path in before.Where(File.Exists)) created.Add(path);
                done.Add((target, written.Backup, mixes.Count));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
            {
                failure = ex.Message;
                failed = target;
                break;
            }
        }

        if (failed is not null) RollBack(done.Select(d => d.Target), created);

        return batch.Targets.Select(t =>
        {
            var copy = t.Copy;
            if (t.Error is not null) return new HitsoundCopyTarget(t.Path, 0, 0, 0, null, t.Error);
            if (ReferenceEquals(t, failed)) return Result(t, null, 0, failure);
            if (failed is not null && done.Any(d => ReferenceEquals(d.Target, t)))
                return Result(t, null, 0, $"Put back as it was, because {Path.GetFileName(failed.Path)} couldn't be written.");
            if (done.FirstOrDefault(d => ReferenceEquals(d.Target, t)) is { Target: not null } ok) return Result(t, ok.Backup, ok.Generated, null);
            if (failed is not null && ticked.Contains(t.Path)) return Result(t, null, 0, "Not written, because another difficulty couldn't be written.");
            return Result(t, null, 0, "Skipped (not ticked).");
        }).ToList();

        static HitsoundCopyTarget Result(CopyTargetPlan t, string? backup, int generated, string? error) =>
            new(t.Path, t.Copy!.Matched, t.Copy.Changed, t.Conflicts.Count, backup, error, t.Copy.Muted)
            { Generated = generated, OnTicksAndSlides = t.Copy.GreenLines.Count, KeptFromSilent = t.Copy.KeptFromSilent };
    }

    // Puts written targets back to the bytes they were planned from and removes the samples this Copy made.
    private static void RollBack(IEnumerable<CopyTargetPlan> written, IEnumerable<string> created)
    {
        foreach (var target in written)
        {
            try { SafeFile.WriteAllBytes(target.Path, target.Opened); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // its backup is still in the app data
        }

        foreach (var path in created)
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

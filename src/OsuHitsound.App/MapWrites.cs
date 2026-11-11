using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>What <see cref="MapWrites"/> needs from the window: its questions, the editor project, the status bar.</summary>
internal interface IMapWriteHost
{
    /// <summary>Asks before a save that writes samples or leaves hits out; false cancels it.</summary>
    Task<bool> ConfirmSave(string title, string text);

    /// <summary>Shows what Generate will write; <paramref name="replan"/> plans again with or without slider audio. Null cancels.</summary>
    Task<(GeneratePlan Plan, LaneAudio Audio)?> ConfirmGenerate((GeneratePlan Plan, LaneAudio Audio) initial,
        Func<GeneratePlan, string> summary, Func<bool, Task<(GeneratePlan Plan, LaneAudio Audio)>> replan);

    /// <summary>Saves the editor project (and drum maps); false when it couldn't be written.</summary>
    bool SaveProject();

    /// <summary>Shows progress while a write runs.</summary>
    void ShowStatus(string text);
}

/// <summary>
/// How a save or Generate ended. <see cref="Status"/> is for the status bar (null: leave it as it is). After a save that
/// wrote, the window reopens the map (<see cref="Reopen"/>); <see cref="MapWritten"/> means the .osu itself changed.
/// </summary>
internal sealed record WriteOutcome(bool Done, string? Status)
{
    public bool MapWritten { get; init; }
    public bool Reopen { get; init; }
    /// <summary>The [Hitsounds] diff Generate wrote.</summary>
    public string? GeneratedPath { get; init; }
}

/// <summary>
/// Save and Generate for one open map: plan, ask, write, in that order, one at a time. Pairing decides how sounds osu!
/// can't play together are written. Planning and writing run off the UI thread; questions go to the
/// <see cref="IMapWriteHost"/>. Nothing is written once the edits changed meanwhile or the map was <see cref="Close"/>d.
/// </summary>
internal sealed class MapWrites(MapDocument document, string backupRoot, string manifestRoot)
{
    /// <summary>The outcome of a write asked for while another runs: nothing happens.</summary>
    public static readonly WriteOutcome Busy = new(false, null);

    private bool closed;

    /// <summary>A save or Generate is running.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>The map was closed or reopened: a write in progress stops before writing anything.</summary>
    public void Close() => closed = true;

    private EditSession Session => document.Session;

    /// <summary>Saves the edits into the difficulty, with a backup of the .osu (in the editor's app data).</summary>
    public Task<WriteOutcome> SaveAsync(IMapWriteHost host, ExportMergeSettings settings, Lazy<IReadOnlyDictionary<string, byte[]>> skin) => Run(async () =>
    {
        try
        {
            int loose = Session.Unanchored.Count();
            bool hs = Session.State.HsMode || Session.State.HsCircles.Count > 0;
            if (!host.SaveProject()) throw new IOException("The editor project could not be written. Fix its folder permissions before saving.");
            if (loose > 0 && !Session.State.Hits.Any(h => h.Anchor is not null))
                return new(false, $"{loose} hitsound(s) have no hit object and cannot be saved into this difficulty. Use HS mode or Generate. Their editor project is kept.");
            var (plan, audio) = await Task.Run(() => document.PlanHsSave(manifestRoot, skin.Value, settings));
            if (plan.Storage.Conflicts.Count > 0) throw new IOException(plan.Storage.Conflicts[0].Reason);
            if ((plan.Storage.Mixes.Count > 0 || plan.Storage.Removes.Count > 0 || plan.Notes.Count > 0 || loose > 0)
                && !await host.ConfirmSave(hs ? "Save HS difficulty" : "Save difficulty", SaveQuestion(plan, loose)))
                return new(false, null);
            if (closed) return new(false, null);
            var written = await Task.Run(() => document.WriteHsSave(plan, new LaneMixer(audio).Mix, backupRoot, manifestRoot));
            bool projectSaved = host.SaveProject();
            // Reopened even if the editor project failed, to keep the editable layers in this window.
            return new(projectSaved, $"Saved this difficulty. Backup: {written.Backup ?? "no map changes"}"
                + (loose > 0 ? $" {loose} loose hitsound(s) remain in the editor project only." : "")
                + (plan.Notes.Count > 0 ? " " + string.Join(" ", plan.Notes) : "")
                + (projectSaved ? "" : " Editor state could not be persisted. Keep this window open and retry Save before closing."))
            {
                MapWritten = written.Backup is not null, // the .osu itself changed (not only its samples)
                Reopen = true,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException)
        {
            return new(false, $"Save stopped: {ex.Message}");
        }
    });

    private static string SaveQuestion(HsSavePlan plan, int loose)
    {
        var mixes = plan.Storage.Mixes;
        return (mixes.Count == 0 ? "Save this difficulty?"
                : $"Save this difficulty? Some objects keep sounds osu! can't play together from the map's files (different indexes, sets or volumes), so Save writes {mixes.Count} new sample file(s) that combine them: "
                  + string.Join(", ", mixes.Take(6).Select(m => m.FileName)) + (mixes.Count > 6 ? ", …" : "")
                  + ". Your lanes stay as they are; existing samples are not changed.")
            + (loose > 0 ? $"\n\n{loose} hitsound(s) have no hit object and will remain in the editor project only. Use HS mode or Generate to write them." : "")
            + (plan.Notes.Count > 0 ? "\n\n" + string.Join("\n", plan.Notes) : "");
    }

    /// <summary>Writes the [Hitsounds] difficulty and its samples beside the map; the open map is not changed.</summary>
    public Task<WriteOutcome> GenerateAsync(IMapWriteHost host, ExportMergeSettings settings, Lazy<IReadOnlyDictionary<string, byte[]>> skin) => Run(async () =>
    {
        try
        {
            if (Session.State.Hits.Count == 0) return new(false, "Nothing to generate: there are no hits.");
            var state = Session.State;
            Task<(GeneratePlan Plan, LaneAudio Audio)> Plan(bool includeSliderAudio) =>
                Task.Run(() => document.PlanGenerate(manifestRoot, skin.Value, settings, includeSliderAudio));
            bool Changed() => !ReferenceEquals(state, Session.State);

            (GeneratePlan Plan, LaneAudio Audio) planned;
            try
            {
                host.ShowStatus("Planning the hitsound diff…");
                planned = await Plan(includeSliderAudio: false);
            }
            catch (Exception ex)
            {
                // Planning only reads, so any failure here leaves everything as it was: say so instead of closing the editor.
                return new(false, $"Generate stopped, nothing was written: {ex.Message}");
            }

            if (string.Equals(Path.GetFullPath(planned.Plan.OsuPath), Path.GetFullPath(document.Path), StringComparison.OrdinalIgnoreCase))
                return new(false, "This map is the [Hitsounds] diff itself: open the difficulty you hitsound in, then Generate.");
            if (closed) return new(false, null); // another map is open now: nothing to say about this one
            if (Changed()) return new(false, "The edits changed while Generate was being prepared. Generate again; nothing was written.");
            if (await host.ConfirmGenerate(planned, Summary, Plan) is not { } confirmed) return new(false, "Generate cancelled; nothing was written.");
            if (closed) return new(false, null);
            if (Changed()) return new(false, "The edits changed before Generate was confirmed. Generate again; nothing was written.");

            var (plan, audio) = confirmed;
            string? note = null;
            await Task.Run(() =>
            {
                var written = GenerateWriter.Write(plan, new LaneMixer(audio).Mix, backupRoot);
                try { written.Manifest.Save(manifestRoot, plan.OsuPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The mapset is written; only reuse next time is lost (the next Generate takes fresh indexes).
                    note = $" (Couldn't record its mixes for next time: {ex.Message})";
                }
            });
            return new(true, $"Generated {Path.GetFileName(plan.OsuPath)}: {plan.Circles} circles, {plan.Mixes.Count} new and {plan.Reused.Count} reused mixed sample(s)."
                + (plan.Conflicts.Count > 0 ? $" {plan.Conflicts.Count} moment(s) couldn't be written." : "") + note) { GeneratedPath = plan.OsuPath };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return new(false, $"Generate stopped, nothing was written: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Unexpected: report it rather than close the editor (the old diff, if any, has a backup in the editor's app data).
            return new(false, $"Generate failed: {ex.Message}");
        }
    });

    // One write at a time, and none once the map is closed.
    private async Task<WriteOutcome> Run(Func<Task<WriteOutcome>> write)
    {
        if (IsBusy || closed) return Busy;
        IsBusy = true;
        try { return await write(); }
        finally { IsBusy = false; }
    }

    private static string Summary(GeneratePlan plan) =>
        $"Write {Path.GetFileName(plan.OsuPath)}{(File.Exists(plan.OsuPath) ? " (replacing the existing one; a backup is kept in the editor's app data)" : "")} " +
        $"with {plan.Circles} circle{(plan.Circles == 1 ? "" : "s")}.\n\n" +
        (plan.Mixes.Count > 0
            ? $"{plan.Mixes.Count} mixed sample{(plan.Mixes.Count == 1 ? "" : "s")} will be written to the mapset ({string.Join(", ", plan.Mixes.Take(4).Select(m => m.FileName))}{(plan.Mixes.Count > 4 ? ", …" : "")}). " +
              "Mixes that include default skin sounds contain that audio (non-commercial licence).\n\n"
            : "No new mixed samples are needed.\n\n") +
        (plan.Reused.Count > 0 ? $"{plan.Reused.Count} mix(es) from the last Generate are used again as they are (same files, same indexes).\n\n" : "") +
        (plan.Removes.Count > 0
            ? $"{plan.Removes.Count} mix(es) from the last Generate that nothing uses any more will be removed ({string.Join(", ", plan.Removes.Take(4))}{(plan.Removes.Count > 4 ? ", …" : "")}).\n\n"
            : "") +
        (plan.Conflicts.Count > 0 ? $"{plan.Conflicts.Count} moment(s) still can't be written: {plan.Conflicts[0].Reason}\n\n" : "") +
        "Your opened map is not changed.";
}

using Avalonia.Controls;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private bool fillingHs, savingHs;

    private void SetupHsMode()
    {
        HsModeButton.IsCheckedChanged += (_, _) =>
        {
            if (fillingHs || session is null) return;
            session.SetHsMode(HsModeButton.IsChecked == true);
            RefreshHsMode();
            ScheduleProjectSave();
        };
    }

    private void RefreshHsMode()
    {
        fillingHs = true;
        try
        {
            HsModeButton.IsEnabled = session?.HsEligible == true && !savingHs;
            HsModeButton.IsChecked = session?.State.HsMode == true;
            ToolTip.SetTip(HsModeButton, session?.HsEligible == true
                ? "Create centred circles for hitsounds. Shared times use one circle; removing its last sound removes an owned circle. Off keeps circles; enabling resumes maintenance. Saved per difficulty."
                : "Available for difficulties that started empty or whose difficulty name contains the word HS or hitsounds.");
        }
        finally { fillingHs = false; }
    }

    private async Task<bool> SaveHs()
    {
        if (doc is not { } document || session is null || savingHs) return false;
        savingHs = true;
        bool wasEnabled = Root.IsEnabled;
        Root.IsEnabled = false;
        audio?.Pause();
        var view = model?.CaptureViewState();
        double left = Lanes.Axis.Left, zoom = Lanes.Axis.Zoom, position = audio?.Position ?? Lanes.Playhead;
        var decoded = new Dictionary<Lane, float[]?>();
        int loose = session.Unanchored.Count();
        bool hs = session.State.HsMode || session.State.HsCircles.Count > 0;
        try
        {
            if (!SaveProject()) throw new IOException("The editor project could not be written. Fix its folder permissions before saving.");
            if (loose > 0 && !session.State.Hits.Any(h => h.Anchor is not null))
            { flash = $"{loose} hitsound(s) have no hit object and cannot be saved into this difficulty. Use HS mode or Generate. Their editor project is kept."; UpdateStatus(); return false; }
            string manifests = GenerateManifest.DefaultRoot();
            var defaults = skin.Value;
            var (plan, fresh) = await Task.Run(() => document.PlanHsSave(manifests, defaults, exportSettings));
            float[]? Decode(Lane lane)
            {
                if (decoded.TryGetValue(lane, out var samples)) return samples;
                var bytes = lane.Source == LaneSource.Skin ? defaults.GetValueOrDefault(lane.Name)
                    : fresh.Find(lane.Name) is { } file ? fresh.ReadBytes(file) : null;
                return decoded[lane] = bytes is null ? null : SampleMixer.Decode(bytes);
            }
            if (!exportSettings.PairSamples && plan.Storage.MergeCombinations.Count > 0)
            {
                ExportMergeSettings? settings;
                try
                {
                    settings = await Dialogs.MergePriorities(this, plan.Storage.MergeCombinations, mix =>
                    {
                        if (audio is null) throw new IOException("Audio output is unavailable. Check Preferences → Audio output.");
                        audio.PreviewMix(SampleMixer.Mix(mix, Decode));
                    });
                }
                finally { audio?.StopAudition(); }
                if (settings is null) return false;
                decoded.Clear();
                (plan, fresh) = await Task.Run(() => document.PlanHsSave(manifests, defaults, settings));
            }
            if (plan.Storage.Conflicts.Count > 0)
                throw new IOException(plan.Storage.Conflicts[0].Reason);
            if ((plan.Storage.Mixes.Count > 0 || plan.Storage.Removes.Count > 0 || plan.Notes.Count > 0 || loose > 0) && !await Dialogs.Confirm(this, hs ? "Save HS difficulty" : "Save difficulty",
                (plan.Storage.Mixes.Count == 0 ? "Save this difficulty?"
                    : $"Save this difficulty? Some objects keep sounds osu! can't play together from the map's files (different indexes, sets or volumes), so Save writes {plan.Storage.Mixes.Count} new sample file(s) that combine them: "
                      + string.Join(", ", plan.Storage.Mixes.Take(6).Select(m => m.FileName)) + (plan.Storage.Mixes.Count > 6 ? ", …" : "")
                      + ". Your lanes stay as they are; existing samples are not changed.")
                    + (loose > 0 ? $"\n\n{loose} hitsound(s) have no hit object and will remain in the editor project only. Use HS mode or Generate to write them." : "")
                    + (plan.Notes.Count > 0 ? "\n\n" + string.Join("\n", plan.Notes) : ""), "Save")) return false;
            var written = await Task.Run(() => document.WriteHsSave(plan, mix => SampleMixer.Mix(mix, Decode), MapSaver.DefaultBackupRoot(), manifests));
            bool projectSaved = SaveProject();
            // Preserve editable layers in this window even if a sidecar write unexpectedly fails after the .osu save.
            var reload = OpenAsync(document.Path, hsOverride: document.HsProject);
            Loading = reload;
            if (!await reload) return false;
            if (view is not null) model?.RestoreViewState(view);
            (Lanes.Axis.Zoom, Lanes.Axis.Left) = (zoom, left);
            Seek(position);
            flash = $"Saved this difficulty. Backup: {written.Backup ?? "no map changes"}"
                + (loose > 0 ? $" {loose} loose hitsound(s) remain in the editor project only." : "")
                + (plan.Notes.Count > 0 ? " " + string.Join(" ", plan.Notes) : "")
                + (projectSaved ? "" : " Editor state could not be persisted. Keep this window open and retry Save before closing.");
            UpdateStatus();
            return projectSaved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException)
        {
            flash = $"Save stopped: {ex.Message}";
            UpdateStatus();
            return false;
        }
        finally
        {
            savingHs = false;
            Root.IsEnabled = wasEnabled;
            RefreshHsMode();
        }
    }
}

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

    private async Task SaveHs()
    {
        if (doc is not { } document || session is null || savingHs) return;
        savingHs = true;
        bool wasEnabled = Root.IsEnabled;
        Root.IsEnabled = false;
        audio?.Pause();
        var view = model?.CaptureViewState();
        double left = Lanes.Axis.Left, zoom = Lanes.Axis.Zoom, position = audio?.Position ?? Lanes.Playhead;
        var decoded = new Dictionary<Lane, float[]?>();
        try
        {
            if (!SaveProject()) throw new IOException("The editor project could not be written. Fix its folder permissions before saving HS objects.");
            string manifests = GenerateManifest.DefaultRoot();
            var defaults = skin.Value;
            var (plan, fresh) = await Task.Run(() => document.PlanHsSave(manifests, defaults));
            float[]? Decode(Lane lane)
            {
                if (decoded.TryGetValue(lane, out var samples)) return samples;
                var bytes = lane.Source == LaneSource.Skin ? defaults.GetValueOrDefault(lane.Name)
                    : fresh.Find(lane.Name) is { } file ? fresh.ReadBytes(file) : null;
                return decoded[lane] = bytes is null ? null : SampleMixer.Decode(bytes);
            }
            if (plan.Storage.MergeCombinations.Count > 0)
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
                if (settings is null) return;
                decoded.Clear();
                (plan, fresh) = await Task.Run(() => document.PlanHsSave(manifests, defaults, settings));
            }
            if (plan.Storage.Conflicts.Count > 0)
                throw new IOException(plan.Storage.Conflicts[0].Reason);
            if ((plan.Storage.Mixes.Count > 0 || plan.Storage.Removes.Count > 0 || plan.Notes.Count > 0) && !await Dialogs.Confirm(this, "Save HS difficulty",
                $"Save this difficulty with {plan.Storage.Circles} HS circle(s), {plan.Storage.Mixes.Count} new mixed sample(s), and {plan.Storage.Removes.Count} unused generated sample(s) removed? All input sounds are kept. Existing shared samples are protected."
                    + (plan.Notes.Count > 0 ? "\n\n" + string.Join("\n", plan.Notes) : ""), "Save")) return;
            var written = await Task.Run(() => document.WriteHsSave(plan, mix => SampleMixer.Mix(mix, Decode), MapSaver.DefaultBackupRoot(), manifests));
            bool projectSaved = SaveProject();
            // Preserve editable layers in this window even if a sidecar write unexpectedly fails after the .osu save.
            await (Loading = OpenAsync(document.Path, hsOverride: document.HsProject));
            if (view is not null) model?.RestoreViewState(view);
            (Lanes.Axis.Zoom, Lanes.Axis.Left) = (zoom, left);
            Seek(position);
            flash = $"Saved this HS difficulty: {plan.Storage.Circles} managed circle(s). Backup: {written.Backup}"
                + (plan.Notes.Count > 0 ? " " + string.Join(" ", plan.Notes) : "")
                + (projectSaved ? "" : " Editor state could not be persisted. Keep this window open and retry Save before closing.");
            UpdateStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException)
        {
            flash = $"HS Save stopped: {ex.Message}";
            UpdateStatus();
        }
        finally
        {
            savingHs = false;
            Root.IsEnabled = wasEnabled;
            RefreshHsMode();
        }
    }
}

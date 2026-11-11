using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private ExportMergeSettings exportSettings = new() { PairSamples = true };

    private void SetupOutput()
    {
        exportSettings.Style = Enum.IsDefined(prefs.ExportStyle) ? prefs.ExportStyle : ExportStyle.GreenLines;
        GreenLinesItem.Click += (_, _) => SetOutputStyle(ExportStyle.GreenLines);
        PerObjectItem.Click += (_, _) => SetOutputStyle(ExportStyle.PerObject);
        MissingWarningsItem.Click += (_, _) =>
        {
            prefs = prefs with { ShowMissingObjectWarnings = MissingWarningsItem.IsChecked };
            RefreshOutputSettings(); SavePrefs();
        };
        KeepMixedIndexesItem.Click += (_, _) =>
        {
            prefs = prefs with { KeepMixedIndexes = KeepMixedIndexesItem.IsChecked };
            RefreshOutputSettings(); SavePrefs();
        };
        ReloadOsuItem.Click += (_, _) => { prefs = prefs with { ReloadOsuAfterSave = ReloadOsuItem.IsChecked }; SavePrefs(); };
        ConflictWarningsItem.Click += (_, _) =>
        {
            prefs = prefs with { ShowExportWarnings = ConflictWarningsItem.IsChecked };
            RefreshOutputSettings(); SavePrefs();
        };
        RefreshOutputSettings();
    }

    private void SetOutputStyle(ExportStyle style)
    {
        exportSettings = exportSettings.Copy();
        exportSettings.Style = style;
        prefs = prefs with { ExportStyle = style };
        RefreshOutputSettings(); SavePrefs(); ScheduleProjectSave(); ScheduleCompile();
    }

    private void RefreshOutputSettings()
    {
        GreenLinesItem.IsChecked = exportSettings.Style == ExportStyle.GreenLines;
        PerObjectItem.IsChecked = exportSettings.Style == ExportStyle.PerObject;
        MissingWarningsItem.IsChecked = prefs.ShowMissingObjectWarnings;
        ConflictWarningsItem.IsChecked = prefs.ShowExportWarnings;
        ReloadOsuItem.IsChecked = prefs.ReloadOsuAfterSave;
        KeepMixedIndexesItem.IsChecked = prefs.KeepMixedIndexes;
        if (session is not null) session.KeepMixedIndexes = prefs.KeepMixedIndexes;
        Lanes.ShowMissingObjectWarnings = prefs.ShowMissingObjectWarnings;
        Lanes.ShowExportWarnings = prefs.ShowExportWarnings;
        Volume.ShowExportWarnings = prefs.ShowExportWarnings;
        Volume.InvalidateVisual();
        Lanes.InvalidateVisual();
        Inspector.Browser.SetShortcuts(mapPath is null ? null : Path.GetDirectoryName(mapPath), prefs.HitsoundFolder);
    }

    private async Task ReviewOutput()
    {
        if (doc is not { } document || savingHs || generating || !Loading.IsCompleted) return;
        try
        {
            var settings = exportSettings.Copy();
            var state = document.Session.State;
            var (plan, laneAudio) = session!.Unanchored.Any()
                ? await Task.Run(() => document.PlanGenerate(GenerateManifest.DefaultRoot(), skin.Value, settings, includeSliderAudio: true))
                : await Task.Run(() => { var result = document.PlanHsSave(GenerateManifest.DefaultRoot(), skin.Value, settings); return (result.Plan.Storage, result.Audio); });
            var mixer = new LaneMixer(laneAudio);
            void Preview(MixFile mix)
            {
                if (audio is null) throw new IOException("Audio output is unavailable. Check Preferences.");
                audio.PreviewMix(mixer.Mix(mix));
            }
            ExportMergeSettings? edited;
            if (!ReferenceEquals(document, doc) || !ReferenceEquals(state, document.Session.State))
            { flash = "The edits changed while output was being prepared. Review output again."; UpdateStatus(); return; }
            try { edited = await Dialogs.ReviewOutput(this, plan, settings, Preview); }
            finally { audio?.StopAudition(); }
            if (edited is null || !ReferenceEquals(document, doc)) return;
            exportSettings = edited;
            prefs = prefs with { ExportStyle = edited.Style };
            RefreshOutputSettings(); SavePrefs(); ScheduleProjectSave(); ScheduleCompile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or ArgumentException)
        { flash = $"Could not review output: {ex.Message}"; UpdateStatus(); }
    }

    internal static string? SampleStartFolder(string? last, string? map) => Directory.Exists(last) ? last : Directory.Exists(map) ? map : null;

    private async Task<IStorageFolder?> SampleStartLocation()
    {
        var path = SampleStartFolder(prefs.LastSampleFolder, mapPath is null ? null : Path.GetDirectoryName(mapPath));
        return path is null ? null : await StorageProvider.TryGetFolderFromPathAsync(path);
    }

    private void RememberSampleFolder(string path)
    {
        prefs = prefs with { LastSampleFolder = Path.GetDirectoryName(path) };
        SavePrefs();
    }

    private void OpenHitsoundFolder()
    {
        if (!Directory.Exists(prefs.HitsoundFolder))
        { flash = "Choose an existing hitsound folder in Preferences."; UpdateStatus(); return; }
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("explorer.exe");
            start.ArgumentList.Add(prefs.HitsoundFolder!);
            System.Diagnostics.Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { flash = $"Could not open the hitsound folder: {ex.Message}"; UpdateStatus(); }
    }
}

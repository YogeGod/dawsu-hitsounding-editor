using Avalonia.Platform.Storage;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private async Task<bool> CanLeaveEditedMap() => session is not { IsEdited: true }
        || await Dialogs.Confirm(this, "Unsaved edits", "Opening another map discards unsaved edits. Save them first if you want to keep them.", "Discard edits");

    private async Task NewHitsoundDiff()
    {
        if (doc is not { } document || mapPath is not { } path)
        {
            flash = "Open a map before creating a hitsound diff.";
            UpdateStatus();
            return;
        }
        var name = await Dialogs.Prompt(this, "New hitsound diff", "Difficulty name", "Hitsounds");
        if (name is null || !await CanLeaveEditedMap()) return;
        try
        {
            var created = HitsoundDiff.CreateEmpty(document.File, path, name);
            await Open(created);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            flash = $"Couldn't create the difficulty: {ex.Message}";
            UpdateStatus();
        }
    }

    private async Task ImportOsz()
    {
        if (prefs.SongsFolder is not { } songs || !Directory.Exists(songs))
        {
            flash = "Set an existing osu! Songs folder in Preferences before importing an .osz.";
            UpdateStatus();
            await ShowPreferences();
            return;
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import .osz",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("osu! beatmap archive") { Patterns = ["*.osz"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } archive) return;
        try
        {
            var imported = await Task.Run(() => OszImport.Extract(archive, songs));
            var diffs = OszImport.Difficulties(imported);
            string? selected = diffs.Count switch
            {
                0 => null,
                1 => diffs[0],
                _ => await Dialogs.Choose(this, "Choose a difficulty", "Open a difficulty from the imported mapset:", diffs),
            };
            if (selected is not null && await CanLeaveEditedMap()) await Open(selected);
            flash = $"Imported {Path.GetFileName(imported)}. Press F5 in osu!stable's song select to refresh.";
            UpdateStatus();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            flash = $"Couldn't import the .osz: {ex.Message}";
            UpdateStatus();
        }
    }
}

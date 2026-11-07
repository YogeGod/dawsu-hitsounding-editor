using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OsuHitsound.Audio;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

/// <summary>Choosing a SoundFont (.sf2) and one of its presets, for a Keysound track or the Hitsound editor's drums.</summary>
internal static class SoundFontPicker
{
    public static async Task<string?> Browse(Window owner, string title = "Choose a SoundFont")
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("SoundFont") { Patterns = ["*.sf2"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Reads the file and lets the user pick a preset (kits first when <paramref name="drums"/>); <paramref name="keep"/>'s
    /// preset is preselected and its effects setting kept. Null when cancelled or the file can't be read.
    /// </summary>
    public static async Task<KeyInstrument?> ChoosePreset(Window owner, string path, KeyInstrument? keep, Action<string> status, bool drums = false)
    {
        IReadOnlyList<SoundFontPreset> presets;
        try { presets = await Task.Run(() => SoundFonts.Presets(SoundFonts.Load(path))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            status($"Couldn't read {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }

        if (presets.Count == 0)
        {
            status($"{Path.GetFileName(path)} has no presets.");
            return null;
        }

        var ordered = drums ? [.. presets.Where(p => p.IsDrumKit), .. presets.Where(p => !p.IsDrumKit)] : presets;
        var labels = ordered.Select(p => p.Label + (p.IsDrumKit ? " (drum kit)" : "")).ToList();
        int current = keep is null ? 0 : Math.Max(0, ordered.ToList().FindIndex(p => p.Bank == keep.Bank && p.Program == keep.Program));
        var label = await Dialogs.Choose(owner, "Choose preset", $"Presets in {Path.GetFileName(path)}:", labels, labels[current], accept: "Use");
        if (label is null) return null;
        var preset = ordered[labels.IndexOf(label)];
        return KeyInstrument.SoundFontPreset(path, preset.Bank, preset.Program, preset.Name, keep?.Effects ?? true);
    }
}

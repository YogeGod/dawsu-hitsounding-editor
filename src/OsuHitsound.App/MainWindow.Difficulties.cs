using Avalonia.Controls;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// The ribbon's Difficulty box: the open mapset's .osu files by their osu! difficulty name. Picking one opens it, after
/// the same unsaved-edits question as any other Open. The list is read again each time the box opens, so a difficulty
/// made since (New hitsound diff, Generate, osu!) shows up.
/// </summary>
public partial class MainWindow
{
    private List<(string Path, string Name)> difficultyChoices = [];
    private bool fillingDifficulties; // setting the selection from code must not open anything

    private void SetUpDifficultyBox()
    {
        DifficultyBox.DropDownOpened += (_, _) => RefreshDifficulties();
        DifficultyBox.SelectionChanged += async (_, _) =>
        {
            if (fillingDifficulties || DifficultyBox.SelectedIndex is not (>= 0 and var index) || index >= difficultyChoices.Count) return;
            var path = difficultyChoices[index].Path;
            if (mapPath is null || string.Equals(path, mapPath, StringComparison.OrdinalIgnoreCase)) return;
            if (await MayLeaveMap()) await Open(path); // a successful open refreshes the box itself
            if (!string.Equals(mapPath, path, StringComparison.OrdinalIgnoreCase)) RefreshDifficulties(); // refused or failed: show the open map again
        };
    }

    /// <summary>
    /// Open's key (Ctrl+O) with a map open in the Hitsound editor: drops down the Difficulty list instead of the file
    /// picker. False with no map, on the start screen, in Keysound or when the box is hidden, so the key opens a file.
    /// </summary>
    private bool OpenDifficultyList()
    {
        if (mapPath is null || keysoundChrome || Start.IsVisible || !DifficultyBox.IsEffectivelyVisible) return false;
        RefreshDifficulties();
        if (!DifficultyBox.IsEnabled) return false;
        DifficultyBox.Focus();
        DifficultyBox.IsDropDownOpen = true;
        return true;
    }

    /// <summary>The .osu files beside a map, by difficulty name ([Version], else the file name), sorted by name.</summary>
    internal static List<(string Path, string Name)> MapsetDifficulties(string osuPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(osuPath))!;
        try
        {
            return Directory.EnumerateFiles(dir, "*.osu")
                .Select(p => (Path: p, Name: HitsoundDiff.VersionOf(p) ?? Path.GetFileNameWithoutExtension(p)))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [(osuPath, HitsoundDiff.VersionOf(osuPath) ?? Path.GetFileNameWithoutExtension(osuPath))];
        }
    }

    /// <summary>Fills the box from the open map's folder and selects the open map. Empty and off with no map, or in Keysound.</summary>
    private void RefreshDifficulties()
    {
        fillingDifficulties = true;
        try
        {
            difficultyChoices = mapPath is { } open && !keysoundChrome ? MapsetDifficulties(open) : [];
            DifficultyBox.ItemsSource = difficultyChoices.Select(d => d.Name).ToList();
            DifficultyBox.SelectedIndex = difficultyChoices.FindIndex(d => string.Equals(d.Path, mapPath, StringComparison.OrdinalIgnoreCase));
            DifficultyBox.IsEnabled = difficultyChoices.Count > 0;
        }
        finally
        {
            fillingDifficulties = false;
        }
    }
}

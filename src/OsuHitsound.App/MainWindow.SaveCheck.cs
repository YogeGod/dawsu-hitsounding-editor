using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// The ribbon's Save button and its problem badge (layout D2): the badge counts what Save .osu can't write as drawn.
/// With no problems a click saves at once; with problems it lists them first, then "Continue to Save" runs the normal
/// save, which still asks or refuses as before. Ctrl+S and the File menu skip the list.
/// </summary>
public partial class MainWindow
{
    private const string SaveTip = "Save the .osu (Ctrl+S). A backup of the previous version is kept outside the mapset.";
    private int saveProblems;
    private (int Conflicts, Conflict? First, int Unanchored)? saveCheckShown;

    private void SetupSaveCheck()
    {
        SaveContinueButton.Click += (_, _) => { FlyoutBase.GetAttachedFlyout(SaveButton)?.Hide(); _ = Save(); };
        SaveCheckShowButton.Click += (_, _) => { FlyoutBase.GetAttachedFlyout(SaveButton)?.Hide(); ShowNextOutputProblem(); };
    }

    private void SaveClicked()
    {
        if (saveProblems == 0 || WriteBusy) { _ = Save(); return; } // Save() does nothing while a write runs
        FlyoutBase.ShowAttachedFlyout(SaveButton);
    }

    /// <summary>The status line's and the Save list's wording for moments Save can't write as drawn.</summary>
    private string ConflictNote(IReadOnlyList<Conflict> conflicts) =>
        $"{conflicts.Count} moment{(conflicts.Count == 1 ? "" : "s")} can't be saved as drawn (first at {FormatTime(conflicts[0].Time)}: {conflicts[0].Reason})";

    /// <summary>The status line's and the Save list's wording for hits with no gameplay object.</summary>
    private static string UnanchoredNote(int unanchored) =>
        $"{unanchored} hit{(unanchored == 1 ? " has" : "s have")} no gameplay object and won't be included by Save .osu. Use HS mode or Generate to include them.";

    /// <summary>
    /// Updates the badge and the list behind it from the latest compile and the hits with no object. The status runs on
    /// every edit and selection change, so the list is rebuilt only when what it says changes.
    /// </summary>
    private void ShowSaveState()
    {
        if (keysoundChrome) return; // the button is the key editor's "Save project" for now
        SaveBadge.IsVisible = saveProblems > 0;
        ToolTip.SetTip(SaveButton, saveProblems == 0 ? SaveTip
            : $"Save the .osu: {saveProblems} problem{(saveProblems == 1 ? "" : "s")} to check first. Ctrl+S skips this list.");
    }

    /// <summary>The title bar's Save button in Hitsound mode: the badge and the tooltip follow the number of problems.</summary>
    private void UpdateSaveCheck(IReadOnlyList<Conflict> conflicts, int unanchored)
    {
        var shown = (conflicts.Count, conflicts.Count > 0 ? conflicts[0] : null, unanchored);
        if (shown == saveCheckShown) return;
        saveCheckShown = shown;

        saveProblems = conflicts.Count + unanchored;
        SaveBadgeText.Text = saveProblems.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ShowSaveState();

        SaveCheckList.Children.Clear();
        if (conflicts.Count > 0) SaveCheckList.Children.Add(Row("TriangleAlert", ConflictNote(conflicts)));
        if (unanchored > 0) SaveCheckList.Children.Add(Row("TriangleAlert", UnanchoredNote(unanchored)));
        SaveCheckList.Children.Add(Row("Info", "A backup of the previous .osu is kept outside the mapset."));
        SaveCheckShowButton.IsVisible = saveProblems > 0;

        static Control Row(string glyph, string text) => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new AppIcon { Glyph = glyph, Size = 14, VerticalAlignment = VerticalAlignment.Top },
                new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 310 },
            },
        };
    }
}

using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;

namespace OsuHitsound.App;

/// <summary>
/// The Hitsound menus' look (menu layout D, decided 2026-11-12): each menu name and item gets an icon, on/off settings
/// show a switch instead of a checkmark, and choice rows behave as one-of-several buttons. Keysound mode's menus get the
/// same look.
/// </summary>
public partial class MainWindow
{
    private static readonly Dictionary<string, string> MenuIcons = new()
    {
        ["FileMenu"] = "FolderOpen", ["EditMenu"] = "Pencil", ["PlaybackMenu"] = "Play", ["MapMenu"] = "Columns3", ["ViewMenu"] = "Scan",
        // File
        ["StartScreenItem"] = "House", ["OpenItem"] = "FolderOpen", ["OpenFromOsuItem"] = "Link2", ["ImportOszItem"] = "Download",
        ["RecentItem"] = "History", ["OpenFolderItem"] = "FolderOpen", ["OpenHitsoundFolderItem"] = "Volume2", ["SaveItem"] = "Save",
        ["RevertItem"] = "Undo2", ["NewDiffItem"] = "CirclePlus", ["GenerateFileItem"] = "AudioWaveform", ["ReviewOutputItem"] = "Scan",
        ["ReloadOsuItem"] = "RotateCw", ["ExportStyleRow"] = "SlidersHorizontal", ["KeepMixedIndexesItem"] = "Columns3",
        ["MissingWarningsItem"] = "TriangleAlert", ["ConflictWarningsItem"] = "TriangleAlert", ["PreferencesItem"] = "Settings",
        ["QuickStartItem"] = "Play", ["TutorialItem"] = "Info", ["CheckUpdatesItem"] = "Download",
        // Edit
        ["UndoItem"] = "Undo2", ["RedoItem"] = "Redo2", ["CutItem"] = "Scissors", ["CopyItem"] = "Copy", ["PasteItem"] = "Copy",
        ["DuplicateItem"] = "Copy", ["DeleteItem"] = "X", ["SelectAllItem"] = "MousePointer2", ["QuantizeItem"] = "Magnet",
        ["RepeatItem"] = "Repeat2", ["EditRangeItem"] = "UnfoldVertical", ["ScaleVolumeItem"] = "TrendingUp",
        ["ProtectHitnormalsItem"] = "CircleCheck", ["DrumAutoFillItem"] = "Drumstick",
        // Playback
        ["DrumVstItem"] = "Piano", ["DrumKitMapPlaybackItem"] = "KeyboardMusic", ["StartFromRow"] = "Play",
        ["ReturnOnStopItem"] = "Undo2", ["FollowItem"] = "LocateFixed", ["SpeedRow"] = "Gauge",
        // Map
        ["AddLaneItem"] = "CirclePlus", ["FilterLanesItem"] = "ListFilter", ["GenerateItem"] = "AudioWaveform", ["CopyHitsoundsItem"] = "Copy",
        ["CleanGreenLinesItem"] = "Eraser", ["OpenBackupsItem"] = "FolderOpen",
        // View
        ["ZoomInItem"] = "ZoomIn", ["ZoomOutItem"] = "Minus", ["CollapseAllItem"] = "FoldVertical", ["ObjectLinesItem"] = "Columns3",
        ["UsedLanesItem"] = "ListFilter", ["InspectorPanelItem"] = "PanelLeft", ["BrowserPanelItem"] = "PanelRight",
        ["HistoryPanelItem"] = "History", ["SoundSetsPanelItem"] = "Volume2", ["RangeToolItem"] = "UnfoldVertical",
        ["TimeSignaturesItem"] = "Metronome", ["InspectorItem"] = "Ghost", ["HelpItem"] = "CircleHelp",
        // Keysound mode
        ["KsFileMenu"] = "FolderOpen", ["KsEditMenu"] = "Pencil", ["KsPlaybackMenu"] = "Play", ["KsTrackMenu"] = "Piano", ["KsViewMenu"] = "Scan",
        ["KsStartScreenItem"] = "House", ["KsOpenItem"] = "FolderOpen", ["KsSaveItem"] = "Save", ["KsCloseItem"] = "X",
        ["KsImportMidiItem"] = "Download", ["KsGenerateItem"] = "AudioWaveform", ["KsProjectFolderItem"] = "FolderOpen",
        ["KsPreferencesItem"] = "Settings", ["KsCheckUpdatesItem"] = "Download",
        ["KsUndoItem"] = "Undo2", ["KsRedoItem"] = "Redo2", ["KsCutItem"] = "Scissors", ["KsCopyItem"] = "Copy", ["KsPasteItem"] = "Copy",
        ["KsDuplicateItem"] = "Copy", ["KsDeleteItem"] = "X", ["KsSelectAllItem"] = "MousePointer2", ["KsQuantizeItem"] = "Magnet",
        ["KsRepeatItem"] = "Repeat2", ["KsSyncItem"] = "Link2", ["KsEditRangeItem"] = "UnfoldVertical", ["KsScaleVelocityItem"] = "TrendingUp",
        ["KsRangeToolItem"] = "UnfoldVertical",
        ["KsStartFromRow"] = "Play", ["KsReturnOnStopItem"] = "Undo2", ["KsFollowItem"] = "LocateFixed",
        ["KsAddTrackItem"] = "CirclePlus", ["KsRenameTrackItem"] = "Pencil", ["KsDeleteTrackItem"] = "X",
        ["KsInstrumentItem"] = "Piano", ["KsInstrumentEditorItem"] = "SlidersHorizontal",
        ["KsZoomInItem"] = "ZoomIn", ["KsZoomOutItem"] = "Minus", ["KsObjectLinesItem"] = "Columns3", ["KsGhostsItem"] = "Ghost",
        ["KsInspectorPanelItem"] = "PanelLeft", ["KsControllerItem"] = "SlidersHorizontal", ["KsUiInspectorItem"] = "Ghost",
        ["KsHelpItem"] = "CircleHelp",
    };

    private void SetupMenuLook()
    {
        foreach (var menu in new[] { HeaderMenu, KeysoundMenu })
            foreach (var item in menu.GetLogicalDescendants().OfType<MenuItem>().ToList())
                Look(item, item.Parent == menu);

        Choices(GreenLinesChoice, PerObjectChoice);
        Choices(StartCursorChoice, StartLeftChoice, StartSelectionChoice, StartSelectionOrLeftChoice);
        Choices(KsStartCursorChoice, KsStartLeftChoice, KsStartSelectionChoice, KsStartSelectionOrLeftChoice);
    }

    private static void Look(MenuItem item, bool top)
    {
        if (item.Name is { } name && MenuIcons.TryGetValue(name, out var glyph) && !top)
            item.Icon = new AppIcon { Glyph = glyph, Size = 14 };
        if (top && item.Header is string menu)
        {
            AutomationProperties.SetName(item, menu);
            item.Header = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5,
                Children =
                {
                    new AppIcon { Glyph = MenuIcons[item.Name!], Size = 13 },
                    new TextBlock { Text = menu, VerticalAlignment = VerticalAlignment.Center },
                    new AppIcon { Glyph = "ChevronDown", Size = 9, Opacity = 0.6 },
                },
            };
        }
        else if (item.ToggleType == MenuItemToggleType.CheckBox) AsSwitch(item);
    }

    /// <summary>Shows an on/off item's state as a switch on its right instead of a checkmark (the style hides that).</summary>
    private static void AsSwitch(MenuItem item)
    {
        item.Classes.Add("switch");
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        if (item.Header is string text)
        {
            label.Text = text;
            AutomationProperties.SetName(item, text);
        }
        var track = new Border { Child = new Border() };
        track.Classes.Add("switchTrack");
        ((Border)track.Child).Classes.Add("switchKnob");
        DockPanel.SetDock(track, Dock.Right);
        item.Header = new DockPanel { Children = { track, label } };
    }

    /// <summary>Makes toggle buttons one-of-several: clicking one checks it and unchecks the rest.</summary>
    private static void Choices(params ToggleButton[] buttons)
    {
        foreach (var button in buttons)
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true) foreach (var other in buttons.Where(b => b != button)) other.IsChecked = false;
                else if (!buttons.Any(b => b.IsChecked == true)) button.IsChecked = true; // one always stays chosen
            };
    }
}

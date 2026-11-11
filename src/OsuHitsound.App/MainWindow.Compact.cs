using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private bool narrowLayout, wideInspectorOpen, compactInspectorOpen;
    private bool SelectionBarCollapsed => prefs.SelectionBarCollapsed;

    private void SetupCompactLayout()
    {
        // Keep the real controls in the layout, preserving their bindings and handlers.
        Icon(OpenButton, "FolderOpen", "Open a map (Ctrl+O)");
        Icon(SaveButton, "Save", "Save .osu (Ctrl+S)");
        Icon(HsModeButton, "CirclePlus", "HS mode: create circles when placing sounds");
        Icon(SnapToggle, "Magnet", "Snap to the beat grid (J)");
        Icon(AuditionPlacementButton, "Headphones", "Audition newly drawn hits");
        Icon(InspectorToggle, "PanelLeft", "Show inspector");
        Icon(LaneDetailsToggle, "Info", "Show sample indexes");
        Icon(UsedLanesToggle, "ListFilter", "Show only lanes with hits");
        Icon(ObjectLinesToggle, "Columns3", "Show gameplay-object lines");
        Icon(VolumeToggle, "SlidersHorizontal", "Show volume controller");
        Icon(FitButton, "Scan", "Fit the whole map");
        Icon(VolumeApplyButton, "Check", "Apply selection volume");
        Icon(SongMuteButton, "VolumeX", "Mute the song");
        SongMuteButton.Width = 22;
        Icon(ReturnOnStopButton, "Undo2", "Return to playback start when stopping");
        Icon(FollowPlaybackButton, "LocateFixed", "Follow the playhead during playback");
        Icon(LoopButton, "Repeat2", "Cycle between the L and R locators (L)");
        Icon(TripletSnapToggle, "Triplet", "Triplet snap cycling");
        foreach (var (control, name) in new (Control, string)[]
        {
            (SnapModeBox, "Grid mode"), (SnapBox, "Beat division"), (ZoomSlider, "Timeline zoom"),
            (GlobalVolumeSlider, "Global volume"), (SongVolumeSlider, "Song volume"),
            (SongOffsetBox, "Song offset in milliseconds"), (LeftLocatorBox, "Left loop locator in milliseconds"),
            (RightLocatorBox, "Right loop locator in milliseconds"),
        }) AutomationProperties.SetName(control, name);

        ThemeToggle.IsChecked = prefs.DarkMode;
        UiTheme.Current.SetDarkMode(prefs.DarkMode);
        UpdateThemeToggle();
        ThemeToggle.IsCheckedChanged += (_, _) =>
        {
            prefs = prefs with { DarkMode = ThemeToggle.IsChecked == true };
            UiTheme.Current.SetDarkMode(prefs.DarkMode);
            UpdateThemeToggle();
            SavePrefs();
        };
        SelectionDisclosure.Click += (_, _) => SetSelectionCollapsed(!SelectionBarCollapsed);
        Keys.SelectionCollapsed = SelectionBarCollapsed;
        Keys.SelectionCollapseRequested += SetSelectionCollapsed;
        ShowSelectionDetails();
        Lanes.LaneListResizing += AlignToLaneList;
        Lanes.LaneListResized += () =>
        {
            AlignToLaneList();
            prefs = prefs with { LaneListWidth = ArrangeView.UserListWidth, LaneNamesCollapsed = ArrangeView.NamesCollapsed };
            SavePrefs();
        };
        ControlBar.SizeChanged += (_, _) => UpdateVolumePanelHeight();
        EditBar.SizeChanged += (_, _) => UpdateVolumePanelHeight();
        StatusBar.SizeChanged += (_, _) => UpdateVolumePanelHeight();

        static void Icon(Button button, string glyph, string name)
        {
            button.Content = new AppIcon { Glyph = glyph };
            button.Classes.Add("icon");
            button.Classes.Remove("wide");
            button.Width = 30;
            button.Padding = default;
            AutomationProperties.SetName(button, name);
            var tip = ToolTip.GetTip(button)?.ToString();
            ToolTip.SetTip(button, string.IsNullOrEmpty(tip) ? name : name + "\n" + tip);
        }
    }

    // One choice for both editors' selection bars.
    private void SetSelectionCollapsed(bool collapsed)
    {
        prefs = prefs with { SelectionBarCollapsed = collapsed };
        ShowSelectionDetails();
        Keys.SelectionCollapsed = collapsed;
        SavePrefs();
    }

    private void UpdateThemeToggle()
    {
        ThemeIcon.Glyph = prefs.DarkMode ? "Moon" : "Sun";
        var name = prefs.DarkMode ? "Dark mode · switch to Not-as-dark" : "Not-as-dark mode · switch to Dark";
        ToolTip.SetTip(ThemeToggle, name);
        AutomationProperties.SetName(ThemeToggle, name);
    }

    private void ShowSelectionDetails()
    {
        InfoBarLeft.IsVisible = !SelectionBarCollapsed;
        SelectionDisclosureIcon.Glyph = SelectionBarCollapsed ? "ChevronDown" : "ChevronUp";
        var label = SelectionBarCollapsed ? "Expand selection details" : "Collapse selection details";
        ToolTip.SetTip(SelectionDisclosure, label);
        AutomationProperties.SetName(SelectionDisclosure, label);
        UpdateChromeHeights();
    }

    private void UpdateCompactLayout(double width)
    {
        bool browserNarrow = !prefs.ClassicBrowser && width < 1200;
        if (browserNarrow != compactBrowser)
        {
            if (browserNarrow) wideBrowserOpen = BrowserPanelItem.IsChecked;
            compactBrowser = browserNarrow;
            BrowserPanelItem.IsChecked = browserNarrow ? false : wideBrowserOpen;
            ShowBrowser(BrowserPanelItem.IsChecked);
        }
        bool narrow = width < 1100;
        if (narrow != narrowLayout)
        {
            if (narrow) wideInspectorOpen = InspectorToggle.IsChecked == true;
            else compactInspectorOpen = InspectorToggle.IsChecked == true;
            narrowLayout = narrow;
            InspectorToggle.IsChecked = narrow ? compactInspectorOpen : wideInspectorOpen;
        }
        AppTitle.IsVisible = width >= 1100;
        FileTitle.IsVisible = width >= 1250;
        TitleBarContent.MinWidth = ControlBarContent.MinWidth = EditBarContent.MinWidth = StatusBarContent.MinWidth = 0;
        // No sideways scrolling: the bars fit the window, and the map name shortens with "…" instead of growing the bar.
        TitleBarScroller.HorizontalScrollBarVisibility = ControlBarScroller.HorizontalScrollBarVisibility =
            EditBarScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        SelectionField.MinWidth = narrow ? 100 : 130;
        StartField.MinWidth = narrow ? 140 : 150;
        ObjectField.MinWidth = narrow ? 150 : 200;
        StartField.Width = narrow ? 140 : double.NaN;
        ObjectField.Width = narrow ? 150 : double.NaN;
        Lcd.Margin = default;
        Lcd.Padding = new Thickness(12, 2);
        MixControls.Width = width < 1000 ? 268 : 308;
        TransportLeft.Spacing = width < 1000 ? 2 : 4;
        TransportButtons.Spacing = width < 1000 ? 2 : 4;
        PlaybackOptionsPanel.Spacing = width < 1000 ? 2 : 4;
        if (LcdColumns.ColumnDefinitions[0].Width.Value != (width < 1000 ? 84 : 96))
            LcdColumns.ColumnDefinitions = new ColumnDefinitions(width < 1000 ? "84,66,44,46" : "96,80,48,70");
        SongControls.ColumnDefinitions[4].Width = new GridLength(width < 1000 ? 0 : 32);
        // Six direct speed choices stay in the transport. Compact widths use two rows inside its existing height.
        bool compactSpeed = width < 1400;
        SpeedPanel.ItemWidth = width < 1000 ? 30 : compactSpeed ? 38 : 42;
        SpeedPanel.ItemHeight = compactSpeed ? 24 : 28;
        SpeedPanel.Width = SpeedPanel.ItemWidth * (compactSpeed ? 3 : 6);
        SpeedPanel.Height = compactSpeed ? 48 : 28;
        foreach (var button in SpeedPanel.Children.OfType<Button>())
        {
            button.Width = SpeedPanel.ItemWidth - 2;
            button.Height = button.MinHeight = compactSpeed ? 22 : 28;
            button.FontSize = width < 1000 ? 9 : 11;
        }
        // Compact windows use a second toolbar row; playback behavior never leaves the bottom bar.
        bool twoRows = width < 1400;
        ControlBarContent.RowDefinitions[1].Height = new GridLength(twoRows ? 44 : 0);
        Grid.SetColumn(PlaybackDetailsPanel, twoRows ? 0 : 1);
        Grid.SetRow(PlaybackDetailsPanel, twoRows ? 1 : 0);
        PlaybackDetailsPanel.Margin = new Thickness(twoRows ? 0 : 12, 0, 0, 0);
        Panel zoomParent = twoRows ? ControlBarContent : ToolbarEnd;
        if (ZoomPanel.Parent != zoomParent)
        {
            if (ZoomPanel.Parent is Panel previous) previous.Children.Remove(ZoomPanel);
            zoomParent.Children.Add(ZoomPanel);
        }
        Grid.SetColumn(ZoomPanel, 2);
        Grid.SetRow(ZoomPanel, 1);
        ZoomPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
    }
}

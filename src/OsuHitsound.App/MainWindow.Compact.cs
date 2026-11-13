using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private bool narrowLayout, wideInspectorOpen, compactInspectorOpen;

    private void SetupCompactLayout()
    {
        // The ribbon's buttons carry their own icons and labels (MainWindow.axaml); these transport buttons become icons.
        Icon(SongMuteButton, "VolumeX", "Mute the song");
        SongMuteButton.Width = 22;
        Icon(ReturnOnStopButton, "Undo2", "Return to playback start when stopping");
        Icon(FollowPlaybackButton, "LocateFixed", "Follow the playhead during playback");
        Icon(LoopButton, "Repeat2", "Cycle between the L and R locators (L)");
        foreach (var (control, name) in new (Control, string)[]
        {
            (SnapModeBox, "Snap mode"), (SnapBox, "Beat division"), (ZoomSlider, "Timeline zoom"),
            (GlobalVolumeSlider, "Global volume"), (SongVolumeSlider, "Song volume"),
            (SongOffsetBox, "Song offset in milliseconds"), (LeftLocatorBox, "Left loop locator in milliseconds"),
            (RightLocatorBox, "Right loop locator in milliseconds"),
        }) AutomationProperties.SetName(control, name);

        // The colour scheme and the user's colours (Preferences, Colours); the old dark-mode switch picks the scheme until then.
        ThemeColors.Apply(UiTheme.Current, prefs.Colors, prefs.DarkMode);
        Lanes.LaneListResizing += AlignToLaneList;
        Lanes.LaneListResized += () =>
        {
            AlignToLaneList();
            prefs = prefs with { LaneListWidth = ArrangeView.UserListWidth, LaneNamesCollapsed = ArrangeView.NamesCollapsed };
            SavePrefs();
        };
        ControlBar.SizeChanged += (_, _) => UpdateVolumePanelHeight();
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
        TitleBarContent.MinWidth = StatusBarContent.MinWidth = 0;
        // No sideways scrolling: the bars fit the window, and the map name shortens with "…" instead of growing the bar.
        TitleBarScroller.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        UpdateRibbonFit(width);
        Keys.FitWidth(width);
        Lcd.Margin = default;
        Lcd.Padding = new Thickness(12, 2);
        MixControls.Width = width < 1000 ? 268 : 308;
        // The transport's tight spacing and speed buttons start below 1100 px, which leaves room for the L/R locator
        // button beside Cycle. Below 1000 px even that is not enough: the ruler still sets L (Ctrl+click) and R (Alt+click).
        bool tightTransport = width < 1100;
        LocatorsButton.IsVisible = width >= LocatorsMinWidth;
        TransportLeft.Spacing = tightTransport ? 2 : 4;
        TransportButtons.Spacing = tightTransport ? 2 : 4;
        PlaybackOptionsPanel.Spacing = tightTransport ? 2 : 4;
        if (LcdColumns.ColumnDefinitions[0].Width.Value != (width < 1000 ? 84 : 96))
            LcdColumns.ColumnDefinitions = new ColumnDefinitions(width < 1000 ? "84,66,44,46" : "96,80,48,70");
        SongControls.ColumnDefinitions[4].Width = new GridLength(width < 1000 ? 0 : 32);
        // Six direct speed choices stay in the transport. Compact widths use two rows inside its existing height.
        bool compactSpeed = width < 1400;
        SpeedPanel.ItemWidth = tightTransport ? 30 : compactSpeed ? 38 : 42;
        SpeedPanel.ItemHeight = compactSpeed ? 24 : 28;
        SpeedPanel.Width = SpeedPanel.ItemWidth * (compactSpeed ? 3 : 6);
        SpeedPanel.Height = compactSpeed ? 48 : 28;
        foreach (var button in SpeedPanel.Children.OfType<Button>())
        {
            button.Width = SpeedPanel.ItemWidth - 2;
            button.Height = button.MinHeight = compactSpeed ? 22 : 28;
            button.FontSize = tightTransport ? 9 : 11;
        }
    }

    /// <summary>The narrowest window that shows the L/R locator button beside Cycle.</summary>
    public const double LocatorsMinWidth = 1000;

    private RibbonFit? ribbonFit;

    /// <summary>Fits the ribbon in one row beside Save (<see cref="App.RibbonFit"/>).</summary>
    private void UpdateRibbonFit(double width) => (ribbonFit ??= new RibbonFit(ControlBar, ControlBarContent, RibbonGroups, SaveButton)).Update(width);
}

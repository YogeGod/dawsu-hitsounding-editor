using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private bool narrowLayout, wideInspectorOpen, compactInspectorOpen;
    private bool SelectionBarCollapsed => prefs.SelectionBarCollapsed;

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
        // Only Keysound mode retains a collapsible selection bar.
        Keys.SelectionCollapsed = SelectionBarCollapsed;
        Keys.SelectionCollapseRequested += SetSelectionCollapsed;
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

    // Keysound mode's selection bar remembers whether it is collapsed.
    private void SetSelectionCollapsed(bool collapsed)
    {
        prefs = prefs with { SelectionBarCollapsed = collapsed };
        Keys.SelectionCollapsed = collapsed;
        SavePrefs();
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

    private static readonly string[] RibbonSteps = ["", "narrow", "compact", "iconsOnly"];
    private double ribbonFitWidth = double.NaN;

    /// <summary>
    /// Fits the ribbon in one row: the first step whose groups fit beside Save, from everything labelled to icons only
    /// (Save keeps its label). Each step hides more: group captions, then secondary labels, then all labels.
    /// </summary>
    private void UpdateRibbonFit(double width)
    {
        // Resizes and theme changes call this often and the fit only depends on width. Before the window is shown its
        // styles aren't applied yet, so that fit is redone.
        if (width == ribbonFitWidth) return;
        if (RibbonGroups.GetVisualRoot() is not null) ribbonFitWidth = width;
        var margin = ControlBarContent.Margin;
        double available = width - margin.Left - margin.Right - Remeasure(SaveButton).Width - SaveButton.Margin.Left;
        foreach (var step in RibbonSteps)
        {
            foreach (var other in RibbonSteps.Where(s => s.Length > 0)) ControlBar.Classes.Set(other, other == step);
            if (Remeasure(RibbonGroups).Width <= available) break;
        }
    }

    // A label or badge shown or hidden inside leaves the button's measure cached, so measure every part afresh.
    private static Size Remeasure(Layoutable control)
    {
        foreach (var part in control.GetVisualDescendants().OfType<Layoutable>()) part.InvalidateMeasure();
        control.InvalidateMeasure();
        control.Measure(Size.Infinity);
        return control.DesiredSize;
    }
}

using Avalonia;
using Avalonia.Controls;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private bool narrowLayout, wideInspectorOpen, compactInspectorOpen;
    private bool shortLayout, tallControllerOpen, shortControllerOpen;

    private void SetupCompactLayout()
    {
        Add(GridOptionsButton, SnapModeBox, TripletSnapToggle);
        Add(ViewOptionsButton, ViewExtras);
        Add(SongOptionsButton, SongControls);
        Add(PlaybackOptionsButton, LocatorPanel, PlaybackOptionsPanel, SpeedPanel);
        // Both summaries already appear in the selection bar, timeline and output footer.
        StatusLeft.IsVisible = StatusRight.IsVisible = false;

        void Add(Button button, params Control[] controls)
        {
            var popup = new StackPanel { Spacing = 12, Margin = new Thickness(4) };
            // A flyout routes keys inside its own pop-up, past the window's handler: Space must still play or stop
            // here instead of toggling Mute or moving a slider.
            popup.AddHandler(KeyDownEvent, OnPlayKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            popup.AddHandler(KeyUpEvent, OnPlayKeyUp, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            button.Flyout = new Flyout
            {
                Content = new ScrollViewer { Content = popup, MaxHeight = 360, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
                Placement = PlacementMode.BottomEdgeAlignedRight,
            };
            // Move the existing controls once: values, event handlers and keyboard access stay intact.
            foreach (var control in controls)
            {
                ((Panel)control.Parent!).Children.Remove(control);
                control.Margin = default;
                string? heading = control == LocatorPanel ? "Loop locators · ms"
                    : control == PlaybackOptionsPanel ? "Playback behavior"
                    : control == SpeedPanel ? "Speed · pitch preserved" : null;
                if (heading is not null) popup.Children.Add(new TextBlock { Text = heading, FontSize = 11, Foreground = Paint.Brush(UiTheme.Current.Color("lcd.caption")) });
                popup.Children.Add(control);
            }
            button.IsVisible = true;
        }
    }

    private void UpdateCompactLayout(double width)
    {
        bool browserNarrow = !prefs.ClassicBrowser && width < 1400;
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
        double height = Bounds.Height > 0 ? Bounds.Height : Height;
        bool shortWindow = height < 650;
        if (shortWindow != shortLayout)
        {
            if (shortWindow) tallControllerOpen = VolumeToggle.IsChecked == true;
            else shortControllerOpen = VolumeToggle.IsChecked == true;
            shortLayout = shortWindow;
            VolumeToggle.IsChecked = shortWindow ? shortControllerOpen : tallControllerOpen;
        }
        AppTitle.IsVisible = width >= 1100;
        ViewExtras.Orientation = Avalonia.Layout.Orientation.Vertical;
        FileTitle.IsVisible = width >= 1250;
        TitleBarContent.MinWidth = ControlBarContent.MinWidth = EditBarContent.MinWidth = StatusBarContent.MinWidth = 0;
        TitleBarScroller.HorizontalScrollBarVisibility = ControlBarScroller.HorizontalScrollBarVisibility =
            EditBarScroller.HorizontalScrollBarVisibility = StatusBarScroller.HorizontalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        SelectionField.MinWidth = narrow ? 100 : 130;
        StartField.MinWidth = narrow ? 140 : 150;
        ObjectField.MinWidth = narrow ? 150 : 200;
        StartField.Width = narrow ? 140 : double.NaN;
        ObjectField.Width = narrow ? 150 : double.NaN;
        Lcd.Margin = new Thickness(width < 1550 ? 8 : 16, 0, 0, 0);
        Lcd.Padding = new Thickness(width < 1550 ? 8 : 12, 2);
    }
}

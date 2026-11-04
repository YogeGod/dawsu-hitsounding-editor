using Avalonia;
using Avalonia.Controls;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private readonly List<(Button Button, double Breakpoint, StackPanel Popup,
        (Control Control, Panel Parent, int Index)[] Items)> compactGroups = [];
    private bool narrowLayout, wideInspectorOpen, compactInspectorOpen;
    private bool shortLayout, tallControllerOpen, shortControllerOpen;

    private void SetupCompactLayout()
    {
        Add(GridOptionsButton, 1100, SnapModeBox, TripletSnapToggle);
        Add(ViewOptionsButton, 1450, ViewExtras);
        Add(SongOptionsButton, 1250, SongControls);
        Add(PlaybackOptionsButton, 1550, LocatorPanel, PlaybackOptionsPanel, SpeedPanel, StatusLeft, StatusRight);

        void Add(Button button, double breakpoint, params Control[] controls)
        {
            var popup = new StackPanel { Spacing = 12, Margin = new Thickness(4) };
            button.Flyout = new Flyout { Content = popup, Placement = PlacementMode.BottomEdgeAlignedRight };
            compactGroups.Add((button, breakpoint, popup, controls.Select(c =>
                (c, (Panel)c.Parent!, ((Panel)c.Parent!).Children.IndexOf(c))).ToArray()));
        }
    }

    private void UpdateCompactLayout(double width)
    {
        if (compactGroups.Count == 0) return;
        // Reuse the actual controls so popovers retain the same values, handlers and shortcuts.
        foreach (var group in compactGroups)
        {
            bool compact = width < group.Breakpoint;
            if (compact == group.Button.IsVisible) continue;
            group.Button.Flyout!.Hide();
            foreach (var item in group.Items)
            {
                ((Panel)item.Control.Parent!).Children.Remove(item.Control);
                if (compact) group.Popup.Children.Add(item.Control);
                else item.Parent.Children.Insert(item.Index, item.Control);
            }
            group.Button.IsVisible = compact;
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
        ViewExtras.Orientation = width < 1450 ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;
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

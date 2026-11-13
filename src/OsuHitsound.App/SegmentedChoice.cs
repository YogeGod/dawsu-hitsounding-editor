using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace OsuHitsound.App;

/// <summary>
/// Shows a short drop-down (two or three choices) as a row of buttons, so the choices are all in view (UI refresh,
/// 2026-11-13). The drop-down stays in place, hidden, as the owner of the value: code and tests keep reading and
/// setting its SelectedIndex, and the buttons follow it.
/// </summary>
internal static class SegmentedChoice
{
    public static StackPanel Attach(ComboBox box)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Classes.Add("segment");
        panel.Classes.Add("quiet");
        var buttons = new List<ToggleButton>();
        foreach (var item in box.Items)
        {
            int index = buttons.Count;
            var button = new ToggleButton { Content = item?.ToString(), Focusable = true };
            button.Click += (_, _) => { box.SelectedIndex = index; Sync(); };
            buttons.Add(button);
            panel.Children.Add(button);
        }

        void Sync()
        {
            for (int i = 0; i < buttons.Count; i++) buttons[i].IsChecked = i == box.SelectedIndex;
        }

        // Equal parts across the row: the design's segmented controls fill their card.
        panel.SizeChanged += (_, e) =>
        {
            if (buttons.Count == 0 || e.NewSize.Width < 40) return;
            // The parts overlap by 1 px (their shared border), so the row has that much more to share out.
            double room = e.NewSize.Width + buttons.Count - 1, each = Math.Floor(room / buttons.Count);
            foreach (var b in buttons) { b.Width = double.NaN; b.Measure(Avalonia.Size.Infinity); b.HorizontalContentAlignment = HorizontalAlignment.Center; }
            double natural = buttons.Sum(b => b.DesiredSize.Width);
            if (natural > room) return; // the labels need more than the row has: keep their own widths
            // Equal parts when every label fits one; otherwise each part grows in proportion, so the row is still filled.
            bool equal = buttons.All(b => b.DesiredSize.Width <= each);
            foreach (var b in buttons) b.Width = equal ? each : Math.Floor(b.DesiredSize.Width * room / natural);
        };
        box.SelectionChanged += (_, _) => Sync();
        box.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Input.InputElement.IsEnabledProperty) panel.IsEnabled = box.IsEnabled; };
        panel.IsEnabled = box.IsEnabled;
        Sync();
        if (ToolTip.GetTip(box) is { } tip) ToolTip.SetTip(panel, tip);
        if (AutomationProperties.GetName(box) is { Length: > 0 } name) AutomationProperties.SetName(panel, name);
        if (box.Parent is Panel parent)
        {
            Grid.SetColumn(panel, Grid.GetColumn(box));
            Grid.SetRow(panel, Grid.GetRow(box));
            DockPanel.SetDock(panel, DockPanel.GetDock(box));
            parent.Children.Insert(parent.Children.IndexOf(box) + 1, panel);
        }

        box.IsVisible = false;
        return panel;
    }
}

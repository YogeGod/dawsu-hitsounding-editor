using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace OsuHitsound.App;

/// <summary>
/// The right-click tool picker in the Hitsound grid, as in Cubase's editors: a row of the tools at the pointer. Picking
/// one switches to it and closes the picker; Esc or a click elsewhere closes it. It never edits a hit.
/// </summary>
public partial class MainWindow
{
    private static readonly (Tool Tool, string Glyph, string Name)[] PickerTools =
    [
        (Tool.Select, "MousePointer2", "Select"),
        (Tool.Grab, "Hand", "Grab"),
        (Tool.Range, "Scan", "Range"),
        (Tool.Draw, "Drumstick", "Draw"),
        (Tool.Erase, "Eraser", "Erase"),
        (Tool.Mute, "VolumeX", "Mute"),
        (Tool.Ramp, "TrendingUp", "Ramp"),
    ];

    /// <summary>The open (or last) picker; the headless checks read it.</summary>
    internal Flyout? ToolPicker { get; private set; }

    private void ShowToolPicker()
    {
        var picker = new Flyout { Placement = PlacementMode.Pointer, ShowMode = FlyoutShowMode.Standard };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (tool, glyph, name) in PickerTools)
        {
            var button = new ToggleButton
            {
                Content = new AppIcon { Glyph = glyph },
                IsChecked = Lanes.Tool == tool,
            };
            button.Classes.Add("tool");
            ToolTip.SetTip(button, name);
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => { SetTool(tool); picker.Hide(); };
            row.Children.Add(button);
        }
        picker.Content = row;
        ToolPicker = picker;
        picker.ShowAt(Lanes, showAtPointer: true);
    }
}

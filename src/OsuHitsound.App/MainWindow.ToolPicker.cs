using Avalonia.Controls;

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

    private void ShowToolPicker() =>
        ToolPicker = global::OsuHitsound.App.ToolPicker.Show(Lanes, PickerTools.Select(t => (t.Glyph, t.Name, Lanes.Tool == t.Tool, (Action)(() => SetTool(t.Tool)))));
}

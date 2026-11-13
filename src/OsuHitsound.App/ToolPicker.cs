using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace OsuHitsound.App;

/// <summary>
/// The right-click tool picker of both editors, as in Cubase's editors: a row of the tools at the pointer. Picking one
/// switches to it and closes the picker; Esc or a click elsewhere closes it. It never edits anything.
/// </summary>
internal static class ToolPicker
{
    public static Flyout Show(Control target, IEnumerable<(string Glyph, string Name, bool Active, Action Pick)> tools)
    {
        var picker = new Flyout { Placement = PlacementMode.Pointer, ShowMode = FlyoutShowMode.Standard };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (glyph, name, active, pick) in tools)
        {
            var button = new ToggleButton { Content = new AppIcon { Glyph = glyph }, IsChecked = active };
            button.Classes.Add("tool");
            ToolTip.SetTip(button, name);
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => { pick(); picker.Hide(); };
            row.Children.Add(button);
        }

        picker.Content = row;
        picker.ShowAt(target, showAtPointer: true);
        return picker;
    }
}

/// <summary>
/// Fits a ribbon in one row (both editors): the first step whose groups fit beside the Save button, from everything
/// labelled to icons only (Save keeps its label). Each step hides more: group captions, then secondary labels, then all.
/// </summary>
internal sealed class RibbonFit(Border bar, Grid content, Panel groups, Button save)
{
    private static readonly string[] Steps = ["", "narrow", "compact", "iconsOnly"];
    private double fitWidth = double.NaN;

    public void Update(double width)
    {
        // Resizes and theme changes call this often and the fit only depends on width. Before the window is shown its
        // styles aren't applied yet, so that fit is redone.
        if (width == fitWidth) return;
        if (Avalonia.VisualTree.VisualExtensions.GetVisualRoot(groups) is not null) fitWidth = width;
        var margin = content.Margin;
        double available = width - margin.Left - margin.Right - Remeasure(save).Width - save.Margin.Left;
        foreach (var step in Steps)
        {
            foreach (var other in Steps.Where(s => s.Length > 0)) bar.Classes.Set(other, other == step);
            if (Remeasure(groups).Width <= available) break;
        }
    }

    /// <summary>Forget the last fit (the window wasn't shown yet, or the labels changed).</summary>
    public void Reset() => fitWidth = double.NaN;

    // A label or badge shown or hidden inside leaves the button's measure cached, so measure every part afresh.
    internal static Avalonia.Size Remeasure(Layoutable control)
    {
        foreach (var part in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(control).OfType<Layoutable>()) part.InvalidateMeasure();
        control.InvalidateMeasure();
        control.Measure(Avalonia.Size.Infinity);
        return control.DesiredSize;
    }
}

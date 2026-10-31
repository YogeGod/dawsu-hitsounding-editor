using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>The whole song as small bars in the group colours, with the visible range outlined. Clicking jumps there.</summary>
internal sealed class OverviewStrip : Control
{
    private static readonly LaneGroup[] Groups = Enum.GetValues<LaneGroup>();

    private ArrangeModel? model;
    private TimeAxis? axis;
    private double length = 1;

    // Which groups play in each pixel column, rebuilt when the width or map changes rather than every frame.
    private (int Column, int Group)[] marks = [];
    private double marksWidth = -1;

    public double Playhead { get; set; }

    public void Attach(ArrangeModel? model, TimeAxis axis, double length)
    {
        this.model = model;
        this.axis = axis;
        this.length = Math.Max(length, 1);
        marksWidth = -1;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        ctx.FillRectangle(Paint.OverviewBg, new Rect(size));
        if (model is null || axis is null) return;

        double scale = size.Width / length;
        if (marksWidth != size.Width)
        {
            marks = [.. model.Map.Moments
                .SelectMany(m => m.Hits.Select(h => ((int)(m.Time * scale), Array.IndexOf(Groups, h.Lane.Group))))
                .Distinct()];
            marksWidth = size.Width;
        }

        double bandHeight = (size.Height - 6) / Groups.Length;
        var brushes = Groups.Select(g => Paint.Brush(ArrangeModel.GroupColor(g))).ToArray();
        foreach (var (column, group) in marks)
            ctx.FillRectangle(brushes[group], new Rect(column, 3 + group * bandHeight, 1.2, Math.Max(1, bandHeight - 1)));

        var visible = new Rect(axis.Left * scale, 1, Math.Max(3, axis.VisibleDuration * scale), size.Height - 2);
        ctx.DrawRectangle(null, Paint.OutlinePen, visible);
        double px = Playhead * scale;
        ctx.DrawLine(Paint.PlayheadPen, new Point(px, 0), new Point(px, size.Height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) => Jump(e.GetPosition(this).X);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Jump(e.GetPosition(this).X);
    }

    private void Jump(double x)
    {
        if (axis is null || Bounds.Width <= 0) return;
        axis.Left = x / Bounds.Width * length - axis.VisibleDuration / 2;
        InvalidateVisual();
    }
}

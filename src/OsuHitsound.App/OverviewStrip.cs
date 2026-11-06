using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Color = Avalonia.Media.Color;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>The whole song as small bars in the group colours, with the visible range outlined. Clicking jumps there.</summary>
internal sealed class OverviewStrip : Control
{
    private static readonly LaneGroup[] Groups = Enum.GetValues<LaneGroup>();
    private static readonly Dictionary<Color, IBrush> GroupBrushes = [];

    private readonly DrawLayer marksLayer, viewportLayer, playheadLayer;
    private ArrangeModel? model;
    private TimeAxis? axis;
    private double length = 1;
    private Size arrangedSize;

    // The hit marks for the whole map, rebuilt when the strip's size or the map changes, not when the playhead or the visible range moves.
    private (int Column, int Group)[] marks = [];
    private (double Width, double Height, double Length) markSize;
    private object? markState;
    private bool marksReady;

    /// <summary>How many times the hit marks were invalidated. Playhead and viewport ticks do not increment this.</summary>
    internal int SceneInvalidations { get; private set; }

    public double Playhead { get; set; }

    public OverviewStrip()
    {
        marksLayer = new DrawLayer(DrawMarks);
        viewportLayer = new DrawLayer(DrawViewport);
        playheadLayer = new DrawLayer(DrawPlayhead);
        VisualChildren.Add(marksLayer);
        VisualChildren.Add(viewportLayer);
        VisualChildren.Add(playheadLayer);
    }

    /// <summary>Redraws the hit marks. The visible-range outline and the playhead are separate layers.</summary>
    public new void InvalidateVisual()
    {
        SceneInvalidations++;
        marksReady = false; // theme colours and any other full refresh must rebuild the cached marks
        base.InvalidateVisual();
        marksLayer.Redraw();
        viewportLayer.Redraw();
        playheadLayer.Redraw();
    }

    /// <summary>Moves the visible-range outline (and the playhead, whose pixel position depends on the axis) without redrawing every hit.</summary>
    internal void InvalidateViewport()
    {
        viewportLayer.Redraw();
        playheadLayer.Redraw();
    }

    /// <summary>Moves the playhead without redrawing the hits or the visible range.</summary>
    internal void InvalidatePlayhead() => playheadLayer.Redraw();

    public void Attach(ArrangeModel? model, TimeAxis axis, double length)
    {
        this.model = model;
        this.axis = axis;
        this.length = Math.Max(length, 1);
        markState = null;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        marksLayer.Measure(availableSize);
        viewportLayer.Measure(availableSize);
        playheadLayer.Measure(availableSize);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rect = new Rect(finalSize);
        marksLayer.Arrange(rect);
        viewportLayer.Arrange(rect);
        playheadLayer.Arrange(rect);
        if (arrangedSize != finalSize)
        {
            arrangedSize = finalSize;
            marksLayer.Redraw();
            viewportLayer.Redraw();
            playheadLayer.Redraw();
        }

        return base.ArrangeOverride(finalSize);
    }

    public override void Render(DrawingContext ctx) => ctx.FillRectangle(Paint.OverviewBg, new Rect(Bounds.Size));

    private static IBrush GroupBrush(LaneGroup group)
    {
        // Lanes have no colours of their own: every mark is the dim text grey.
        var color = UiTheme.Current.Color("text.dim");
        return GroupBrushes.TryGetValue(color, out var brush) ? brush : GroupBrushes[color] = Paint.Brush(color);
    }

    private void DrawMarks(DrawingContext ctx, Rect bounds)
    {
        if (model is null || bounds.Width <= 0 || bounds.Height <= 0) return;
        var state = model.Session.State;
        var size = (bounds.Width, bounds.Height, length);
        if (!marksReady || markSize != size || !ReferenceEquals(markState, state))
        {
            double scale = bounds.Width / length;
            marks = [.. model.Session.State.Hits
                .Select(h => ((int)(h.Time * scale), Array.IndexOf(Groups, ArrangeModel.DisplayGroup(h.Lane))))
                .Distinct()];
            (markSize, markState, marksReady) = (size, state, true);
        }

        double bandHeight = (bounds.Height - 6) / Groups.Length;
        foreach (var (column, group) in marks)
            ctx.FillRectangle(GroupBrush(Groups[group]), new Rect(column, 3 + group * bandHeight, 1.2, Math.Max(1, bandHeight - 1)));
    }

    private void DrawViewport(DrawingContext ctx, Rect bounds)
    {
        if (model is null || axis is null || bounds.Width <= 0 || bounds.Height <= 0) return;
        double scale = bounds.Width / length;
        var visible = new Rect(axis.Left * scale, 1, Math.Max(3, axis.VisibleDuration * scale), bounds.Height - 2);
        ctx.DrawRectangle(null, Paint.OutlinePen, visible);
    }

    private void DrawPlayhead(DrawingContext ctx, Rect bounds)
    {
        if (model is null || bounds.Height <= 0) return;
        double px = Playhead * bounds.Width / length;
        ctx.DrawLine(Paint.PlayheadPen, new Point(px, 0), new Point(px, bounds.Height));
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
        InvalidateViewport();
    }
}

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
    private static readonly Dictionary<Color, IBrush> MarkBrushes = [];

    private readonly DrawLayer marksLayer, viewportLayer, playheadLayer;
    // Where the marks come from: the editor's state (a new object after each edit) and, for a pixel scale, the marks as
    // (column, band). The Hitsound editor bands lanes by group, the key editor notes by track.
    private Func<object?>? stateOf;
    private Func<double, IEnumerable<(int Column, int Band)>>? markSource;
    private int bands = 1;
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

    public void Attach(ArrangeModel? model, TimeAxis axis, double length) =>
        Attach(model is null ? null : () => model.Session.State,
            model is null ? null : scale => model.Session.State.Hits.Select(h => ((int)(h.Time * scale), Array.IndexOf(Groups, ArrangeModel.DisplayGroup(h.Lane)))),
            Groups.Length, axis, length);

    /// <summary>Any editor's marks: <paramref name="marks"/> gives (column, band) for a pixel scale; null shows nothing.</summary>
    public void Attach(Func<object?>? state, Func<double, IEnumerable<(int Column, int Band)>>? marks, int bandCount, TimeAxis axis, double length)
    {
        (stateOf, markSource, bands) = (state, marks, Math.Max(1, bandCount));
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

    private static IBrush MarkBrush()
    {
        // Lanes have no colours of their own: every mark is the dim text grey.
        var color = UiTheme.Current.Color("text.dim");
        return MarkBrushes.TryGetValue(color, out var brush) ? brush : MarkBrushes[color] = Paint.Brush(color);
    }

    private void DrawMarks(DrawingContext ctx, Rect bounds)
    {
        if (markSource is null || bounds.Width <= 0 || bounds.Height <= 0) return;
        var state = stateOf?.Invoke();
        var size = (bounds.Width, bounds.Height, length);
        if (!marksReady || markSize != size || !ReferenceEquals(markState, state))
        {
            double scale = bounds.Width / length;
            marks = [.. markSource(scale).Distinct()];
            (markSize, markState, marksReady) = (size, state, true);
        }

        double bandHeight = (bounds.Height - 6) / bands;
        var brush = MarkBrush();
        foreach (var (column, band) in marks)
            ctx.FillRectangle(brush, new Rect(column, 3 + band * bandHeight, 1.2, Math.Max(1, bandHeight - 1)));
    }

    private void DrawViewport(DrawingContext ctx, Rect bounds)
    {
        if (markSource is null || axis is null || bounds.Width <= 0 || bounds.Height <= 0) return;
        double scale = bounds.Width / length;
        var visible = new Rect(axis.Left * scale, 1, Math.Max(3, axis.VisibleDuration * scale), bounds.Height - 2);
        ctx.DrawRectangle(null, Paint.OutlinePen, visible);
    }

    private void DrawPlayhead(DrawingContext ctx, Rect bounds)
    {
        if (markSource is null || bounds.Height <= 0) return;
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media;
using Color = Avalonia.Media.Color;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// The Volume tab: one bar per moment with hits, height = volume 0–100, lined up with the grid above. The volume is
/// shared by every lane that plays at that moment (osu! has one volume per object).
/// <list type="bullet">
/// <item>Drag a bar: change it; when its moment is selected, every selected moment takes the same value.</item>
/// <item>Alt+drag: paint values across the bars the pointer passes.</item>
/// <item>Shift+drag, or any drag with the Ramp tool: a ramp over the selected moments in that range, or all of them
/// when none is selected, in the chosen shape.</item>
/// </list>
/// Each drag is one undo step.
/// </summary>
internal sealed class VolumeLane : Control
{
    public bool ShowExportWarnings { get; set; } = true;
    private static readonly double[] GuideLines = [0.25, 0.5, 0.75, 1.0];
    private static readonly IBrush ConflictBar = Paint.Brush("#ff453a");
    private static readonly IPen RampPen = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#ffffff"), 1.5, new Avalonia.Media.Immutable.ImmutableDashStyle([4, 3], 0));
    private static readonly Dictionary<Color, IBrush> StemBrushes = [];
    private static readonly Dictionary<Color, IBrush> CapBrushes = [];

    private enum Gesture
    {
        None,
        Bar,
        Paint,
        Ramp,
    }

    private readonly DrawLayer playheadLayer;
    private ArrangeModel? model;
    private TimeAxis? axis;
    private Gesture gesture;
    private List<MomentKey>? dragging;
    // Dragging one of several selected bars moves them all by the same amount (as Cubase's velocity lane):
    // each hit's volume when the drag began, and the pointer's level at the press.
    private Dictionary<EditHit, int>? dragStart;
    private int grabStart;
    private bool compressing; // Ctrl+drag on one of several selected bars: spread them apart (up) or together (down)
    private double grabY;
    private Point last, rampStart, rampEnd;

    /// <summary>How many times the bars were invalidated. A playhead tick does not increment this.</summary>
    internal int SceneInvalidations { get; private set; }

    public VolumeLane()
    {
        playheadLayer = new DrawLayer(DrawPlayhead);
        VisualChildren.Add(playheadLayer);
    }

    /// <summary>Redraws the bars. The playhead layer is separate.</summary>
    public new void InvalidateVisual()
    {
        SceneInvalidations++;
        base.InvalidateVisual();
        playheadLayer.Redraw();
    }

    /// <summary>Moves the playhead without rebuilding the bars.</summary>
    internal void InvalidatePlayhead() => playheadLayer.Redraw();

    public double Playhead { get; set; }

    /// <summary>The Ramp tool (6) is active: every drag draws a ramp.</summary>
    public bool RampTool { get; set; }
    /// <summary>The scope box says "Selected lanes": the Value box and Alt+Up/Down change only those lanes' sounds.</summary>
    public bool SelectedLanesOnly { get; set; }

    /// <summary>The selected hits (the window keeps it current); their lanes decide which volumes the bars show.</summary>
    public IReadOnlyCollection<EditHit> SelectedHits { get; set; } = [];

    /// <summary>
    /// The lanes whose volumes the bars show and edit: the lanes of the selected hits, or the active lane when nothing is
    /// selected (as Cubase's drum editor shows one drum's velocities). Null (every lane) only before any lane is active.
    /// </summary>
    internal IReadOnlySet<Lane>? ShownLanes => SelectedHits.Count > 0 ? SelectedHits.Select(h => h.Lane).ToHashSet()
        : model?.ActiveLane is { } active ? new HashSet<Lane> { active } : null;

    /// <summary>The editing tool, for the cursor over the bars (V6.39 shows the same cursors there as over the grid).</summary>
    public Tool Tool { get; set; } = Tool.Select;

    private Point? pointer;

    /// <summary>Shows the cursor for the tool at the pointer; the window calls it when Alt goes down or up.</summary>
    public void UpdateCursor(KeyModifiers modifiers) =>
        Cursor = ToolCursors.Get(ToolCursors.For(Tool, modifiers, pointer is { } p && p.X >= ArrangeView.GridLeft));

    protected override void OnPointerExited(PointerEventArgs e)
    {
        pointer = null;
        base.OnPointerExited(e);
    }

    public RampShape Shape { get; set; } = RampShape.Linear;

    public IReadOnlySet<MomentKey> SelectedMoments { get; set; } = new HashSet<MomentKey>();

    public void Attach(ArrangeModel? model, TimeAxis axis)
    {
        this.model = model;
        this.axis = axis;
        InvalidateVisual();
    }

    private (double Top, double Bottom) Range => (8, Bounds.Height - 6);

    protected override Size MeasureOverride(Size availableSize)
    {
        playheadLayer.Measure(availableSize);
        return default;
    }

    private Size arrangedSize;

    protected override Size ArrangeOverride(Size finalSize)
    {
        playheadLayer.Arrange(new Rect(finalSize));
        if (arrangedSize != finalSize)
        {
            arrangedSize = finalSize;
            playheadLayer.Redraw();
        }

        return base.ArrangeOverride(finalSize);
    }

    private static IBrush StemBrush(Color color) =>
        StemBrushes.TryGetValue(color, out var brush) ? brush : StemBrushes[color] = Paint.Brush(color, 0.75);

    private static IBrush CapBrush(Color color)
    {
        var bright = Paint.Brighten(color, 0.45);
        return CapBrushes.TryGetValue(bright, out var brush) ? brush : CapBrushes[bright] = Paint.Brush(bright);
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        ctx.FillRectangle(Paint.PanelLow, new Rect(size));
        // The left side holds the volume controls (laid over this control in the window).
        ctx.FillRectangle(Paint.VolumeSide, new Rect(0, 0, ArrangeView.GridLeft, size.Height));
        ctx.FillRectangle(Paint.Divider, new Rect(ArrangeView.GridLeft - 1, 0, 1, size.Height));
        if (model is null || axis is null) return;

        var (top, bottom) = Range;
        var shown = ShownLanes;
        var scoped = model.Session.State.Hits.Where(h => h.Anchor is not null && (shown is null || shown.Contains(h.Lane))).ToLookup(h => h.Anchor!.Value);
        double height = bottom - top;
        double from = axis.Left - 50, to = axis.Time(size.Width - ArrangeView.GridLeft) + 50;
        using (ctx.PushClip(new Rect(ArrangeView.GridLeft, 0, size.Width - ArrangeView.GridLeft, size.Height)))
        {
            foreach (double pct in GuideLines)
            {
                double y = bottom - height * pct;
                ctx.DrawLine(Paint.BeatPen, new Point(ArrangeView.GridLeft, y), new Point(size.Width, y));
            }

            foreach (var (pct, label) in new[] { (1.0, "100"), (0.5, "50"), (0.0, "1") })
                Paint.DrawText(ctx, label, new Point(ArrangeView.GridLeft + 4, bottom - height * pct - (pct > 0 ? 0 : 14)), 10, Paint.Dim, 0, Paint.Mono);

            var session = model.Session;
            var groups = model.MomentGroups;
            var moments = session.Moments;
            for (int i = Sorted.LowerBound(moments, from, x => x.Moment.Time); i < moments.Count; i++)
            {
                var (m, key) = moments[i];
                if (m.Time > to) break;
                if (!groups.TryGetValue(key, out var group)) continue; // no hits: nothing plays here
                double x = ArrangeView.GridLeft + axis.X(m.Time);
                if (!scoped.Contains(key)) continue;
                var levels = scoped[key].Select(session.VolumeOf).Distinct().ToArray();
                int volume = levels.Max();
                double h = height * Math.Clamp(volume, 0, 100) / 100.0;
                var color = ArrangeView.VolumeColor(volume); // blue (quiet) to red (loud), as the hits
                bool selected = SelectedMoments.Contains(key);
                bool conflict = ShowExportWarnings && model.Conflicted.Contains(key);
                var stem = selected ? Paint.White : conflict ? ConflictBar : StemBrush(color);
                ctx.FillRectangle(stem, new Rect(x - 2.5, bottom - h, 5, h));
                ctx.FillRectangle(selected ? Paint.Selection : CapBrush(color), new Rect(x - 2.5, bottom - h - 2, 5, 5));
                if (levels.Length > 1)
                    foreach (int level in levels)
                        ctx.FillRectangle(Paint.White, new Rect(x - 3, bottom - height * level / 100.0 - 1, 6, 2));
            }


            if (gesture == Gesture.Ramp) ctx.DrawLine(RampPen, rampStart, rampEnd);
        }
    }

    private void DrawPlayhead(DrawingContext ctx, Rect bounds)
    {
        if (model is null || axis is null || bounds.Width <= 0 || bounds.Height <= 0) return;
        double px = ArrangeView.GridLeft + axis.X(Playhead);
        double width = Math.Max(0, bounds.Width - ArrangeView.GridLeft);
        using (ctx.PushClip(new Rect(ArrangeView.GridLeft, 0, width, bounds.Height)))
            ctx.DrawLine(Paint.PlayheadPen, new Point(px, 0), new Point(px, bounds.Height));
    }

    private int VolumeAt(double y)
    {
        var (top, bottom) = Range;
        return Math.Max((int)Math.Round(Math.Clamp((bottom - y) / (bottom - top), 0, 1) * 100), 1);
    }

    private double TimeAt(double x) => axis!.Time(x - ArrangeView.GridLeft);

    private HashSet<MomentKey>? ScopedMoments() => ShownLanes is { } shown && model is not null
        ? model.Session.State.Hits.Where(h => h.Anchor is not null && shown.Contains(h.Lane)).Select(h => h.Anchor!.Value).ToHashSet()
        : null;

    /// <summary>
    /// Sets these moments' volumes for the shown lanes only. A moment whose sounds are all in shown lanes keeps one object
    /// volume; where other lanes also play, only the shown lanes' sounds change.
    /// </summary>
    internal void ApplyVolumes(IReadOnlyDictionary<MomentKey, int> values)
    {
        if (model is null) return;
        if (ShownLanes is not { } shown) { model.Session.SetVolumes(values); return; }
        var atMoments = model.Session.State.Hits.Where(h => h.Anchor is { } key && values.ContainsKey(key)).GroupBy(h => h.Anchor!.Value).ToList();
        var whole = atMoments.Where(g => g.All(h => shown.Contains(h.Lane))).ToDictionary(g => g.Key, g => values[g.Key]);
        var layers = atMoments.Where(g => !whole.ContainsKey(g.Key)).SelectMany(g => g.Where(h => shown.Contains(h.Lane)))
            .ToDictionary(h => h, h => values[h.Anchor!.Value]);
        if (whole.Count > 0) model.Session.SetVolumes(whole);
        if (layers.Count > 0) model.Session.SetLayerVolumes(layers);
    }

    /// <summary>
    /// Moves every dragged hit by <paramref name="delta"/> from its volume when the drag began, each kept within 1–100,
    /// so their differences stay (dragging back restores them). Objects whose sounds all share one volume keep a single
    /// object volume; the others change per sound.
    /// </summary>
    private void ApplyRelative(int delta) =>
        ApplyTargets(dragStart?.ToDictionary(kv => kv.Key, kv => Math.Clamp(kv.Value + delta, 1, 100)));

    /// <summary>Scales the dragged hits' distances from their average volume by <paramref name="factor"/> (0 = all the same).</summary>
    private void ApplyScaled(double factor)
    {
        if (dragStart is not { Count: > 0 } start) return;
        double mean = start.Values.Average();
        ApplyTargets(start.ToDictionary(kv => kv.Key, kv => Math.Clamp((int)Math.Round(mean + (kv.Value - mean) * factor), 1, 100)));
    }

    // New volumes per dragged hit: one object volume where an object's sounds all started equal, per sound otherwise.
    private void ApplyTargets(Dictionary<EditHit, int>? target)
    {
        if (model is null || dragStart is null || target is null) return;
        // A moment keeps one object volume only when all its sounds are dragged (none in a hidden lane) and started equal.
        var soundsAt = model.Session.State.Hits.Where(h => h.Anchor is not null).GroupBy(h => h.Anchor!.Value).ToDictionary(g => g.Key, g => g.Count());
        var byMoment = target.GroupBy(kv => kv.Key.Anchor!.Value).ToList();
        var whole = byMoment.Where(g => g.Count() == soundsAt.GetValueOrDefault(g.Key) && g.Select(kv => dragStart[kv.Key]).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Value);
        var layers = byMoment.Where(g => !whole.ContainsKey(g.Key)).SelectMany(g => g).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (whole.Count > 0) model.Session.SetVolumes(whole);
        if (layers.Count > 0) model.Session.SetLayerVolumes(layers);
    }

    /// <summary>Moments with hits between two times (the bars that are drawn).</summary>
    private IEnumerable<(double Time, MomentKey Key)> BarsBetween(double from, double to)
    {
        if (model is null) yield break;
        if (to < from) (from, to) = (to, from);
        var moments = model.Session.Moments;
        var scope = ScopedMoments();
        for (int i = Sorted.LowerBound(moments, from, x => x.Moment.Time); i < moments.Count && moments[i].Moment.Time <= to; i++)
            if (model.MomentGroups.ContainsKey(moments[i].Key) && (scope is null || scope.Contains(moments[i].Key))) yield return (moments[i].Moment.Time, moments[i].Key);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (model is null || axis is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        if (p.X < ArrangeView.GridLeft) return;
        var session = model.Session;

        if (RampTool || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            gesture = Gesture.Ramp;
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            gesture = Gesture.Paint;
        else
        {
            // The nearest bar within 6 px; with it, every selected moment when it is selected.
            var scope = ScopedMoments();
            int i = Sorted.Nearest(session.Moments, TimeAt(p.X), 6 / axis.Zoom, x => x.Moment.Time, x => model.MomentGroups.ContainsKey(x.Key) && (scope is null || scope.Contains(x.Key)));
            if (i < 0) return;
            var k = session.Moments[i].Key;
            dragging = SelectedMoments.Contains(k) ? [.. SelectedMoments] : [k];
            gesture = Gesture.Bar;
            if (dragging.Count > 1)
            {
                var keys = dragging.ToHashSet();
                var shown = ShownLanes;
                dragStart = session.State.Hits
                    .Where(h => h.Anchor is { } a && keys.Contains(a) && (shown is null || shown.Contains(h.Lane)))
                    .ToDictionary(h => h, session.VolumeOf);
                grabStart = VolumeAt(p.Y); // relative to the press, so pressing anywhere on the bar doesn't jump them
                compressing = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                grabY = p.Y;
            }
        }

        session.BeginGesture(gesture switch { Gesture.Ramp => "Ramp", Gesture.Paint => "Paint volumes", _ => "Volume" });
        e.Pointer.Capture(this);
        last = rampStart = rampEnd = p;
        Apply(p);
    }

    // Mouse moves arrive faster than frames: only the latest point is applied, once per frame.
    private Point? pending;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        pointer = e.GetPosition(this);
        UpdateCursor(e.KeyModifiers);
        if (gesture == Gesture.None) return;
        bool scheduled = pending is not null;
        pending = e.GetPosition(this);
        if (!scheduled) Dispatcher.UIThread.Post(ApplyPending, DispatcherPriority.Render);
    }

    private void ApplyPending()
    {
        if (pending is { } p && gesture != Gesture.None) Apply(p);
        pending = null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (gesture == Gesture.None) return;
        ApplyPending();
        model?.Session.EndGesture();
        gesture = Gesture.None;
        dragging = null;
        dragStart = null;
        compressing = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    private void Apply(Point p)
    {
        if (model is null || axis is null) return;
        var session = model.Session;
        switch (gesture)
        {
            case Gesture.Bar when dragStart is not null && compressing:
                // Cubase's compress/expand: the volumes move away from (drag up) or towards (down) their average.
                ApplyScaled(Math.Clamp(1 + (grabY - p.Y) / 60, 0, 4));
                break;

            case Gesture.Bar when dragStart is not null:
                ApplyRelative(VolumeAt(p.Y) - grabStart);
                break;

            case Gesture.Bar when dragging is not null:
                ApplyVolumes(dragging.ToDictionary(k => k, _ => VolumeAt(p.Y)));
                break;

            case Gesture.Paint:
            {
                // Every bar the pointer swept over since the last move takes the value along the stroke.
                double t0 = TimeAt(last.X), t1 = TimeAt(p.X);
                ApplyVolumes(VolumeRamp.Ramp(BarsBetween(t0, t1), t0, VolumeAt(last.Y), t1, VolumeAt(p.Y), RampShape.Linear));
                last = p;
                break;
            }

            case Gesture.Ramp:
            {
                rampEnd = p;
                double t0 = TimeAt(rampStart.X), t1 = TimeAt(p.X);
                var bars = BarsBetween(t0, t1).ToList();
                if (bars.Any(b => SelectedMoments.Contains(b.Key))) bars = [.. bars.Where(b => SelectedMoments.Contains(b.Key))];
                session.ResetToGestureStart();
                ApplyVolumes(VolumeRamp.Ramp(bars, t0, VolumeAt(rampStart.Y), t1, VolumeAt(p.Y), Shape));
                break;
            }
        }

        InvalidateVisual();
    }
}

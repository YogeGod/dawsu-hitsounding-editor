using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media;
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
    private static readonly double[] GuideLines = [0.25, 0.5, 0.75, 1.0];
    private static readonly IBrush ConflictBar = Paint.Brush("#ff453a");
    private static readonly IPen RampPen = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#ffffff"), 1.5, new Avalonia.Media.Immutable.ImmutableDashStyle([4, 3], 0));

    private enum Gesture
    {
        None,
        Bar,
        Paint,
        Ramp,
    }

    private ArrangeModel? model;
    private TimeAxis? axis;
    private Gesture gesture;
    private List<MomentKey>? dragging;
    private Point last, rampStart, rampEnd;

    public double Playhead { get; set; }

    /// <summary>The Ramp tool (6) is active: every drag draws a ramp.</summary>
    public bool RampTool { get; set; }

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

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        ctx.FillRectangle(Paint.PanelLow, new Rect(size));
        // The left side holds the volume controls (laid over this control in the window).
        ctx.FillRectangle(Paint.VolumeSide, new Rect(0, 0, ArrangeView.GridLeft, size.Height));
        ctx.FillRectangle(Paint.Divider, new Rect(ArrangeView.GridLeft - 1, 0, 1, size.Height));
        if (model is null || axis is null) return;

        var (top, bottom) = Range;
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
                int volume = session.State.Volumes[key];
                double h = height * Math.Clamp(volume, 0, 100) / 100.0;
                var color = ArrangeModel.GroupColor(group);
                bool selected = SelectedMoments.Contains(key);
                bool conflict = model.Conflicted.Contains(key);
                var stem = selected ? Paint.White : conflict ? ConflictBar : Paint.Brush(color, 0.75);
                ctx.FillRectangle(stem, new Rect(x - 2.5, bottom - h, 5, h));
                ctx.FillRectangle(selected ? Paint.Selection : Paint.Brush(Paint.Brighten(color, 0.45)), new Rect(x - 2.5, bottom - h - 2, 5, 5));
            }


            if (gesture == Gesture.Ramp) ctx.DrawLine(RampPen, rampStart, rampEnd);

            double px = ArrangeView.GridLeft + axis.X(Playhead);
            ctx.DrawLine(Paint.PlayheadPen, new Point(px, 0), new Point(px, size.Height));
        }
    }

    private int VolumeAt(double y)
    {
        var (top, bottom) = Range;
        return Math.Max((int)Math.Round(Math.Clamp((bottom - y) / (bottom - top), 0, 1) * 100), 1);
    }

    private double TimeAt(double x) => axis!.Time(x - ArrangeView.GridLeft);

    /// <summary>Moments with hits between two times (the bars that are drawn).</summary>
    private IEnumerable<(double Time, MomentKey Key)> BarsBetween(double from, double to)
    {
        if (model is null) yield break;
        if (to < from) (from, to) = (to, from);
        var moments = model.Session.Moments;
        for (int i = Sorted.LowerBound(moments, from, x => x.Moment.Time); i < moments.Count && moments[i].Moment.Time <= to; i++)
            if (model.MomentGroups.ContainsKey(moments[i].Key)) yield return (moments[i].Moment.Time, moments[i].Key);
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
            int i = Sorted.Nearest(session.Moments, TimeAt(p.X), 6 / axis.Zoom, x => x.Moment.Time, x => model.MomentGroups.ContainsKey(x.Key));
            if (i < 0) return;
            var k = session.Moments[i].Key;
            dragging = SelectedMoments.Contains(k) ? [.. SelectedMoments] : [k];
            gesture = Gesture.Bar;
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
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    private void Apply(Point p)
    {
        if (model is null || axis is null) return;
        var session = model.Session;
        switch (gesture)
        {
            case Gesture.Bar when dragging is not null:
                session.SetVolume(dragging, VolumeAt(p.Y));
                break;

            case Gesture.Paint:
            {
                // Every bar the pointer swept over since the last move takes the value along the stroke.
                double t0 = TimeAt(last.X), t1 = TimeAt(p.X);
                session.SetVolumes(VolumeRamp.Ramp(BarsBetween(t0, t1), t0, VolumeAt(last.Y), t1, VolumeAt(p.Y), RampShape.Linear));
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
                session.SetVolumes(VolumeRamp.Ramp(bars, t0, VolumeAt(rampStart.Y), t1, VolumeAt(p.Y), Shape));
                break;
            }
        }

        InvalidateVisual();
    }
}

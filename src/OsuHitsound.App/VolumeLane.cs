using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// The Volume tab: one bar per moment with hits, height = volume 0–100, lined up with the grid above. Drag a bar's cap
/// to change it; when the bar's moment is selected, every selected moment takes the same value. The volume is shared by
/// every lane that plays at that moment (osu! has one volume per object).
/// </summary>
internal sealed class VolumeLane : Control
{
    private static readonly double[] GuideLines = [0.25, 0.5, 0.75, 1.0];
    private static readonly IBrush ConflictBar = Paint.Brush("#ff453a");

    private ArrangeModel? model;
    private TimeAxis? axis;
    private List<MomentKey>? dragging;

    public double Playhead { get; set; }

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
        ctx.FillRectangle(Paint.VolumeSide, new Rect(0, 0, ArrangeView.GridLeft, size.Height));
        Paint.DrawText(ctx, "Volume", new Point(10, 8), 12, Paint.Text, 0, Paint.UiBold);
        Paint.DrawText(ctx, "One value per moment, shared by every lane that plays there (osu! has one volume per object). Drag a bar to change it.",
            new Point(10, 28), 11, Paint.Dim, ArrangeView.GridLeft - 20);
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

            double px = ArrangeView.GridLeft + axis.X(Playhead);
            ctx.DrawLine(Paint.PlayheadPen, new Point(px, 0), new Point(px, size.Height));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (model is null || axis is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        if (p.X < ArrangeView.GridLeft) return;

        // The nearest bar (a moment with hits) within 6 px.
        var session = model.Session;
        int i = Sorted.Nearest(session.Moments, axis.Time(p.X - ArrangeView.GridLeft), 6 / axis.Zoom, x => x.Moment.Time,
            x => model.MomentGroups.ContainsKey(x.Key));
        if (i < 0) return;
        var k = session.Moments[i].Key;
        dragging = SelectedMoments.Contains(k) ? [.. SelectedMoments] : [k];
        session.BeginGesture();
        e.Pointer.Capture(this);
        SetFrom(p.Y);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (dragging is not null) SetFrom(e.GetPosition(this).Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (dragging is null) return;
        model?.Session.EndGesture();
        dragging = null;
        e.Pointer.Capture(null);
    }

    private void SetFrom(double y)
    {
        if (model is null || dragging is null) return;
        var (top, bottom) = Range;
        int volume = (int)Math.Round(Math.Clamp((bottom - y) / (bottom - top), 0, 1) * 100);
        model.Session.SetVolume(dragging, Math.Max(volume, 1));
        InvalidateVisual();
    }
}

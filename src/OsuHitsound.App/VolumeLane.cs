using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OsuHitsound.App;

/// <summary>
/// The Volume tab: one bar per moment with hits, height = volume 0–100, lined up with the grid above. Read-only until
/// editing arrives. The volume is shared by every lane that plays at that moment (osu! has one volume per object).
/// </summary>
internal sealed class VolumeLane : Control
{
    private ArrangeModel? model;
    private TimeAxis? axis;

    public double Playhead { get; set; }

    private static readonly double[] GuideLines = [0.25, 0.5, 0.75, 1.0];

    public int SelectedMoment { get; set; } = -1;

    public void Attach(ArrangeModel? model, TimeAxis axis)
    {
        this.model = model;
        this.axis = axis;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        ctx.FillRectangle(Paint.PanelLow, new Rect(size));
        ctx.FillRectangle(Paint.VolumeSide, new Rect(0, 0, ArrangeView.GridLeft, size.Height));
        Paint.DrawText(ctx, "Volume", new Point(10, 8), 12, Paint.Text, 0, Paint.UiBold);
        Paint.DrawText(ctx, "One value per moment, shared by every lane that plays there (osu! has one volume per object).",
            new Point(10, 28), 11, Paint.Dim, ArrangeView.GridLeft - 20);
        ctx.FillRectangle(Paint.Divider, new Rect(ArrangeView.GridLeft - 1, 0, 1, size.Height));
        if (model is null || axis is null) return;

        double top = 8, bottom = size.Height - 6, height = bottom - top;
        double from = axis.Left - 50, to = axis.Time(size.Width - ArrangeView.GridLeft) + 50;
        using (ctx.PushClip(new Rect(ArrangeView.GridLeft, 0, size.Width - ArrangeView.GridLeft, size.Height)))
        {
            foreach (double pct in GuideLines)
            {
                double y = bottom - height * pct;
                ctx.DrawLine(Paint.BeatPen, new Point(ArrangeView.GridLeft, y), new Point(size.Width, y));
            }

            var moments = model.Map.Moments;
            for (int i = FirstAtOrAfter(moments, from); i < moments.Count; i++)
            {
                var m = moments[i];
                if (m.Time < from || m.Hits.Count == 0) continue;
                if (m.Time > to) break;
                double x = ArrangeView.GridLeft + axis.X(m.Time);
                double h = height * Math.Clamp(m.Volume, 0, 100) / 100.0;
                var color = ArrangeModel.GroupColor(m.Hits[0].Lane.Group);
                var stem = i == SelectedMoment ? Paint.White : Paint.Brush(color, 0.75);
                ctx.FillRectangle(stem, new Rect(x - 2.5, bottom - h, 5, h));
                ctx.FillRectangle(i == SelectedMoment ? Paint.Selection : Paint.Brush(Paint.Brighten(color, 0.45)), new Rect(x - 2.5, bottom - h - 2, 5, 5));
            }

            double px = ArrangeView.GridLeft + axis.X(Playhead);
            ctx.DrawLine(Paint.PlayheadPen, new Point(px, 0), new Point(px, size.Height));
        }
    }

    // Moments are in time order, so the first visible one is found by bisection.
    private static int FirstAtOrAfter(IReadOnlyList<Lanes.Moment> moments, double time)
    {
        int lo = 0, hi = moments.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (moments[mid].Time < time) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }
}

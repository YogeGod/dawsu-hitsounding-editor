using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// The arrange area, drawn in one pass so maps with thousands of notes stay smooth: a header row (lane count, song
/// sections, ruler), then per row a number strip, the lane list (group rows with collapse and M/S; lane rows with M, S,
/// file name and index) and the tinted grid with diamond notes. View only: clicking a note selects it, clicking a lane
/// name plays its sample, clicking the ruler moves the playhead.
/// </summary>
internal sealed class ArrangeView : Control
{
    private static UiTheme Theme_ => UiTheme.Current;

    public static double NumberWidth => Theme_.Number("numbers.width");
    public static double ListWidth => Theme_.Number("list.width");
    public static double GridLeft => NumberWidth + ListWidth;
    public static double SectionsHeight => Theme_.Number("header.sections");
    public const double RulerHeight = 24;
    public static double HeaderHeight => SectionsHeight + RulerHeight;
    public const double MinRowHeight = 16, MaxRowHeight = 64;

    private double? rowHeight; // set by Ctrl+Alt+wheel; null follows the theme

    /// <summary>Row height in px (vertical zoom, Ctrl+Alt+wheel). Design G's compact rows are 22 px.</summary>
    public double RowHeight
    {
        get => rowHeight ?? Theme_.Number("row.height");
        set
        {
            rowHeight = Math.Clamp(value, MinRowHeight, MaxRowHeight);
            ScrollY = scrollY;
            InvalidateVisual();
        }
    }

    // Row contents are laid out for 22 px and kept centred in taller rows.
    private double Pad => (RowHeight - 22) / 2;
    private static double NoteSize => Theme_.Number("note.size");
    private static double LaneFont => Theme_.Number("lane.fontSize");
    private static double RulerFont => Theme_.Number("ruler.fontSize");

    private double scrollY;

    public ArrangeView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public ArrangeModel? Model { get; private set; }

    public TimeAxis Axis { get; } = new();

    /// <summary>The playhead in ms (set by the window's frame timer).</summary>
    public double Playhead { get; set; }

    /// <summary>The selected moment's index in the map, or -1.</summary>
    public int SelectedMoment { get; private set; } = -1;

    /// <summary>The "Lane details" toggle: show each lane's file index.</summary>
    public bool ShowIndexes { get; set; } = true;

    public event Action<Lane>? PreviewRequested;

    public event Action<double>? SeekRequested;

    public event Action? SelectionChanged;

    /// <summary>The lanes scrolled up or down (wheel), so the window can move its scroll bar.</summary>
    public event Action? ScrollChanged;

    public double ContentHeight => (Model?.Rows.Count ?? 0) * RowHeight;

    public void SetModel(ArrangeModel? model)
    {
        if (Model is not null) Model.Changed -= InvalidateVisual;
        Model = model;
        if (model is not null) model.Changed += InvalidateVisual;
        scrollY = 0;
        SelectedMoment = -1;
        Axis.End = model?.LastTime ?? 0;
        InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Axis.Width = Math.Max(1, finalSize.Width - GridLeft);
        return base.ArrangeOverride(finalSize);
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        ctx.FillRectangle(Paint.Bg, new Rect(size));
        if (Model is not { } model)
        {
            Paint.DrawText(ctx, "Open a .osu file to see its hitsounds.", new Point(GridLeft + 20, HeaderHeight + 20), Paint.BaseSize, Paint.Dim, 0);
            return;
        }

        double gridWidth = size.Width - GridLeft;
        double from = Axis.Time(0), to = Axis.Time(gridWidth);
        var lines = model.GridLines(from, to).ToList();

        // Rows.
        using (ctx.PushClip(new Rect(0, HeaderHeight, size.Width, size.Height - HeaderHeight)))
        {
            int first = Math.Max(0, (int)(scrollY / RowHeight));
            for (int i = first; i < model.Rows.Count; i++)
            {
                double y = HeaderHeight + i * RowHeight - scrollY;
                if (y > size.Height) break;
                DrawRow(ctx, model, model.Rows[i], i, y, gridWidth, from, to);
            }

            // Grid lines over the tints, under the notes would need two passes; the lines are thin enough on top.
            using (ctx.PushClip(new Rect(GridLeft, HeaderHeight, gridWidth, size.Height - HeaderHeight)))
            {
                foreach (var line in lines)
                {
                    double x = GridLeft + Axis.X(line.Time);
                    ctx.DrawLine(line.IsBar ? Paint.BarPen : Paint.BeatPen, new Point(x, HeaderHeight), new Point(x, size.Height));
                }

                for (int i = first; i < model.Rows.Count; i++)
                {
                    double y = HeaderHeight + i * RowHeight - scrollY;
                    if (y > size.Height) break;
                    if (model.Rows[i] is LaneRow lane) DrawNotes(ctx, model, lane, y, from, to);
                }

                double px = GridLeft + Axis.X(Playhead);
                ctx.DrawLine(Paint.PlayheadPen, new Point(px, HeaderHeight), new Point(px, size.Height));
            }
        }

        DrawHeader(ctx, model, lines, size.Width, gridWidth);
        ctx.DrawLine(Paint.DividerPen, new Point(GridLeft - 0.5, 0), new Point(GridLeft - 0.5, size.Height));
    }

    private void DrawHeader(DrawingContext ctx, ArrangeModel model, List<GridLine> lines, double width, double gridWidth)
    {
        ctx.FillRectangle(Paint.Chrome2, new Rect(0, 0, width, HeaderHeight));
        Paint.DrawText(ctx, "+", new Point(10, 10), 18, Paint.Dim, 0);
        Paint.DrawText(ctx, $"{model.LaneRows.Count} lanes · {model.GroupCount} groups", new Point(34, 14), 12, Paint.Dim, ListWidth - 20);

        using (ctx.PushClip(new Rect(GridLeft, 0, gridWidth, HeaderHeight)))
        {
            // Sections row.
            foreach (var s in model.Sections)
            {
                double x0 = GridLeft + Axis.X(s.Start), x1 = double.IsInfinity(s.End) ? width : GridLeft + Axis.X(s.End);
                if (x1 < GridLeft || x0 > width) continue;
                var rect = new Rect(x0, 2, Math.Max(2, x1 - x0), SectionsHeight - 4);
                ctx.DrawRectangle(Paint.Brush(Theme_.Color("section.fill"), Theme_.Number("section.opacity")), null, rect, 3, 3);
                Paint.DrawText(ctx, s.Name, new Point(Math.Max(x0, GridLeft) + 5, 3), 11, Paint.Text, 0);
            }

            // Ruler.
            double rulerTop = SectionsHeight;
            ctx.DrawLine(Paint.DividerPen, new Point(GridLeft, rulerTop), new Point(width, rulerTop));
            double lastLabel = double.NegativeInfinity;
            foreach (var line in lines)
            {
                double x = GridLeft + Axis.X(line.Time);
                ctx.DrawLine(line.IsBar ? Paint.BarPen : Paint.BeatPen, new Point(x, rulerTop + (line.IsBar ? 4 : 14)), new Point(x, HeaderHeight));
                string label = line.IsBar ? line.Bar.ToString() : $"{line.Bar}.{line.Beat}";
                double labelWidth = line.IsBar ? 24 : 30;
                if (x - lastLabel < labelWidth + 6 || (!line.IsBar && Axis.Zoom * line.BeatLength < 48)) continue;
                Paint.DrawText(ctx, label, new Point(x + 3, rulerTop + 3), RulerFont, line.IsBar ? Paint.RulerBar : Paint.RulerBeat, 0, line.IsBar ? Paint.UiBold : Paint.Ui);
                lastLabel = x;
            }

            double px = GridLeft + Axis.X(Playhead);
            ctx.FillRectangle(Paint.White, new Rect(px - 2, rulerTop, 4, RulerHeight));
        }

        ctx.DrawLine(Paint.DividerPen, new Point(0, HeaderHeight - 0.5), new Point(width, HeaderHeight - 0.5));
    }

    private void DrawRow(DrawingContext ctx, ArrangeModel model, Row row, int index, double y, double gridWidth, double from, double to)
    {
        var rowRect = new Rect(0, y, GridLeft + gridWidth, RowHeight);
        switch (row)
        {
            case GroupRow group:
            {
                var color = ArrangeModel.GroupColor(group.Group);
                ctx.FillRectangle(Paint.Brush(color), new Rect(0, y, NumberWidth, RowHeight));
                ctx.FillRectangle(Paint.GroupHead, new Rect(NumberWidth, y, ListWidth, RowHeight));
                Paint.DrawText(ctx, model.IsCollapsed(group.Group) ? "▸" : "▾", new Point(NumberWidth + 6, y + Pad + 3), 12, Paint.Text, 0);
                DrawToggle(ctx, "M", NumberWidth + GroupMuteX, y, model.IsGroupMuted(group.Group) ? Paint.MuteOn : null);
                DrawToggle(ctx, "S", NumberWidth + GroupSoloX, y, model.IsGroupSoloed(group.Group) ? Paint.SoloOn : null);
                Paint.DrawText(ctx, $"{ArrangeModel.GroupName(group.Group)}  ({group.LaneCount})", new Point(NumberWidth + 76, y + Pad + 4), LaneFont, Paint.Text, ListWidth - 90, Paint.UiBold);
                ctx.FillRectangle(Paint.Brush(color, Theme_.Number("group.tint")), new Rect(GridLeft, y, gridWidth, RowHeight));
                Paint.DrawText(ctx, ArrangeModel.GroupName(group.Group), new Point(GridLeft + 6, y + Pad + 4), 11, Paint.Brush(color), 0, Paint.UiBold);
                break;
            }

            case LaneRow lane:
            {
                bool greyed = !model.IsAudible(lane.Lane);
                var color = greyed ? ArrangeModel.MutedColor : lane.Color;
                ctx.FillRectangle(Paint.Brush(color), new Rect(0, y, NumberWidth, RowHeight));
                Paint.DrawText(ctx, lane.Number.ToString(), new Point(4, y + Pad + 4), 11, Paint.White, NumberWidth - 6, Paint.Mono);
                ctx.FillRectangle(index % 2 == 0 ? Paint.LaneHeadA : Paint.LaneHeadB, new Rect(NumberWidth, y, ListWidth, RowHeight));
                ctx.DrawRectangle(Paint.Brush(color), null, new Rect(NumberWidth + 18, y + Pad + 6, 10, 10), 2, 2);
                DrawToggle(ctx, "M", NumberWidth + LaneMuteX, y, model.IsMuted(lane.Lane) ? Paint.MuteOn : null);
                DrawToggle(ctx, "S", NumberWidth + LaneSoloX, y, model.IsSoloed(lane.Lane) ? Paint.SoloOn : null);

                bool silent = lane.Lane.Missing || model.Silent.Contains(lane.Lane);
                string name = lane.Lane.Source == LaneSource.StoryboardFile ? "SB " + lane.Lane.Name : lane.Lane.Name;
                if (silent) name += lane.Lane.Missing ? "  (missing)" : "  (no sound)";
                Paint.DrawText(ctx, name, new Point(NumberWidth + 84, y + Pad + 4), LaneFont, silent ? Paint.MissingText : Paint.Text, ListWidth - 84 - 48);
                if (ShowIndexes && lane.Index is { } idx)
                    Paint.DrawText(ctx, $"idx {idx}", new Point(GridLeft - 44, y + Pad + 4), 11, Paint.Dim, 40, Paint.Mono);

                ctx.FillRectangle(Paint.Brush(color, Theme_.Number("lane.tint")), new Rect(GridLeft, y, gridWidth, RowHeight));
                break;
            }
        }

        ctx.DrawLine(Paint.DividerPen, new Point(0, y + RowHeight - 0.5), new Point(rowRect.Right, y + RowHeight - 0.5));
    }

    private void DrawToggle(DrawingContext ctx, string letter, double x, double y, IBrush? lit)
    {
        double h = Math.Min(18, RowHeight - 4);
        var rect = new Rect(x, y + (RowHeight - h) / 2, ToggleWidth, h);
        ctx.DrawRectangle(lit ?? Paint.Button, null, rect, 3, 3);
        Paint.DrawText(ctx, letter, new Point(x + 7, y + Pad + 4), 11, lit is null ? Paint.Dim : Paint.Divider, 0, Paint.UiBold);
    }

    private void DrawNotes(DrawingContext ctx, ArrangeModel model, LaneRow row, double y, double from, double to)
    {
        if (!model.Notes.TryGetValue(row.Lane, out var notes)) return;
        bool greyed = !model.IsAudible(row.Lane);
        var color = greyed ? ArrangeModel.MutedColor : Paint.Brighten(row.Color);
        var fill = Paint.Brush(color);
        var span = Paint.Brush(color, 0.45);
        double cy = y + RowHeight / 2;

        // Notes are sorted by start; slider body spans can start before the view, so look back one visible width.
        int start = LowerBound(notes, from - Axis.VisibleDuration);
        for (int i = start; i < notes.Count; i++)
        {
            var n = notes[i];
            if (n.Time > to + 50) break;
            if (n.EndTime < from - 50) continue; // spans end at EndTime; one-shot notes have EndTime == Time
            double x = GridLeft + Axis.X(n.Time);
            if (n.EndTime > n.Time)
            {
                ctx.DrawRectangle(span, null, new Rect(x, cy - 3, Math.Max(2, (n.EndTime - n.Time) * Axis.Zoom), 6), 3, 3);
            }
            else if (n.Passive)
            {
                Paint.DrawDiamond(ctx, new Point(x, cy), NoteSize - 4, fill, Paint.NoteRingPen);
            }
            else
            {
                bool selected = n.MomentIndex == SelectedMoment;
                Paint.DrawDiamond(ctx, new Point(x, cy), NoteSize, selected ? Paint.White : fill, selected ? Paint.SelectedRingPen : Paint.NoteRingPen);
            }
        }
    }

    private static int LowerBound(List<Note> notes, double time)
    {
        int lo = 0, hi = notes.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (notes[mid].Time < time) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            // Vertical zoom, keeping the row under the mouse in place.
            double rowUnder = (p.Y - HeaderHeight + scrollY) / RowHeight;
            RowHeight = Math.Round(RowHeight * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15));
            ScrollY = rowUnder * RowHeight - (p.Y - HeaderHeight);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            Axis.ZoomAt(e.Delta.Y > 0 ? 1.25 : 0.8, Math.Max(0, p.X - GridLeft));
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Delta.X != 0)
            Axis.Left -= (e.Delta.Y + e.Delta.X) * 120 / Axis.Zoom;
        else
            ScrollY -= e.Delta.Y * RowHeight * 3;
        InvalidateVisual();
        e.Handled = true;
    }

    public double ScrollY
    {
        get => scrollY;
        set
        {
            scrollY = Math.Clamp(value, 0, Math.Max(0, ContentHeight - (Bounds.Height - HeaderHeight)));
            ScrollChanged?.Invoke();
            InvalidateVisual();
        }
    }

    // Where the lane list's buttons sit, from the number column's right edge. Drawing and hit-testing both use these.
    private const double ToggleWidth = 22, GroupMuteX = 22, GroupSoloX = 48, LaneMuteX = 34, LaneSoloX = 58;

    private enum ListTarget
    {
        Collapse,
        Mute,
        Solo,
        Name,
    }

    private static ListTarget ListTargetAt(Row row, double x)
    {
        bool In(double left) => x >= left && x < left + ToggleWidth;
        return row switch
        {
            GroupRow when x < GroupMuteX => ListTarget.Collapse,
            GroupRow when In(GroupMuteX) => ListTarget.Mute,
            GroupRow when In(GroupSoloX) => ListTarget.Solo,
            LaneRow when In(LaneMuteX) => ListTarget.Mute,
            LaneRow when In(LaneSoloX) => ListTarget.Solo,
            _ => ListTarget.Name,
        };
    }

    private Row? RowAt(double y)
    {
        int i = (int)Math.Floor((y - HeaderHeight + scrollY) / RowHeight);
        return Model is { } model && y >= HeaderHeight && i >= 0 && i < model.Rows.Count ? model.Rows[i] : null;
    }

    /// <summary>The moment of the nearest note in a lane within a note's width of a grid x, or -1.</summary>
    private int NoteAt(LaneRow row, double gridX)
    {
        if (Model?.Notes.TryGetValue(row.Lane, out var notes) != true) return -1;
        double t = Axis.Time(gridX), tolerance = NoteSize / Axis.Zoom, best = tolerance;
        int hit = -1;
        for (int i = LowerBound(notes!, t - tolerance); i < notes!.Count && notes[i].Time <= t + tolerance; i++)
        {
            double d = Math.Abs(notes[i].Time - t);
            if (!notes[i].Passive && d <= best) (best, hit) = (d, notes[i].MomentIndex);
        }

        return hit;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        if (Model is not { } model || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);

        if (p.Y < HeaderHeight)
        {
            if (p.X >= GridLeft) SeekRequested?.Invoke(Math.Max(0, Axis.Time(p.X - GridLeft)));
            return;
        }

        if (RowAt(p.Y) is not { } row) return;
        if (p.X < GridLeft)
        {
            switch (row, ListTargetAt(row, p.X - NumberWidth))
            {
                case (GroupRow g, ListTarget.Collapse): model.ToggleCollapsed(g.Group); break;
                case (GroupRow g, ListTarget.Mute): model.ToggleGroupMute(g.Group); break;
                case (GroupRow g, ListTarget.Solo): model.ToggleGroupSolo(g.Group); break;
                case (LaneRow l, ListTarget.Mute): model.ToggleMute(l.Lane); break;
                case (LaneRow l, ListTarget.Solo): model.ToggleSolo(l.Lane); break;
                case (LaneRow l, _): PreviewRequested?.Invoke(l.Lane); break;
            }

            return;
        }

        SelectedMoment = row is LaneRow lane ? NoteAt(lane, p.X - GridLeft) : -1;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Lets Ctrl+Alt+wheel zoom go and follows the theme's row height again (after it's edited).</summary>
    public void FollowThemeRowHeight()
    {
        rowHeight = null;
        ScrollY = scrollY;
    }

    /// <summary>The inspector part under a point, for Pick.</summary>
    public string PartAt(Point p)
    {
        if (p.Y < HeaderHeight)
            return p.X >= GridLeft && p.Y < SectionsHeight ? "sections" : "header";
        if (Math.Abs(p.X - (GridLeft + Axis.X(Playhead))) <= 4) return "playhead";
        if (RowAt(p.Y) is not { } row) return "arrange";

        if (p.X < NumberWidth) return row is LaneRow ? "numbers" : "grouprow";
        if (p.X < GridLeft)
            return ListTargetAt(row, p.X - NumberWidth) switch
            {
                ListTarget.Mute or ListTarget.Solo => "toggles",
                _ => row is GroupRow ? "grouprow" : "lanelist",
            };

        if (row is LaneRow lane && NoteAt(lane, p.X - GridLeft) >= 0) return "note";
        return row is GroupRow ? "grouprow" : "lanerow";
    }
}

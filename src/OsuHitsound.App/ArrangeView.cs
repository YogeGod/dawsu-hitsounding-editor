using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>The editing tools (dawsu numbering: 1 Select, 3 Draw, 4 Erase, 5 Mute, 6 Ramp in the volume lane).</summary>
internal enum Tool
{
    Select,
    Draw,
    Erase,
    Mute,
    Ramp,
}

/// <summary>
/// The arrange area, drawn in one pass so maps with thousands of notes stay smooth: a header row (lane count, song
/// sections, ruler), then per row a number strip, the lane list (group rows with collapse and M/S; lane rows with M, S,
/// file name and index) and the tinted grid with diamond notes. Editing: Select (click, Ctrl/Shift+click, marquee,
/// drag to move), Draw (click or drag to paint; starting on a hit erases instead), Erase (drag across hits), right-click
/// deletes a hit, Alt draws temporarily. Hits that won't be saved are drawn white with a red ring on red stripes.
/// </summary>
internal sealed class ArrangeView : Control
{
    private static UiTheme Theme_ => UiTheme.Current;

    public static double NumberWidth => Theme_.Number("numbers.width");
    public static double ListWidth => Theme_.Number("list.width");
    public static double GridLeft => NumberWidth + ListWidth;
    public static double SectionsHeight => Theme_.Number("header.sections");
    public const double RulerHeight = 24;
    public static double HeaderHeight => RulerBottom + WaveHeight + ObjectsHeight;

    /// <summary>Bottom of the ruler: the waveform lane and the object strip sit between it and the lanes.</summary>
    public static double RulerBottom => SectionsHeight + RulerHeight;

    /// <summary>Height of the waveform lane; drag the line under it to resize, double-click to reset.</summary>
    public static double WaveHeight { get; set; } = DefaultWaveHeight;

    public const double DefaultWaveHeight = 44, ObjectsHeight = 26;
    public const double MinRowHeight = 16, MaxRowHeight = 64;

    private static readonly IBrush StripeA = Paint.Brush("#3a1517"), StripeB = Paint.Brush("#6b1f24");
    private static readonly IPen ConflictRing = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#ff453a"), 2);
    private static readonly IPen SubPen = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#323236"), 1);
    private static readonly IPen MarqueePen = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#2f6fd0"), 1);
    private static readonly IBrush MarqueeFill = Paint.Brush(Color.Parse("#2f6fd0"), 0.15);

    private double? rowHeight; // set by Ctrl+Alt+wheel; null follows the theme
    private double scrollY;
    private readonly HashSet<EditHit> selection = [];

    // The gesture in progress.
    private enum Drag
    {
        None,
        Marquee,
        Move,
        Paint,
        Wipe,
        Cycle,
        ResizeWave,
    }

    private Drag drag;
    private Point dragStart, dragNow;
    private EditHit? dragAnchor;
    private List<EditHit> dragHits = [];
    private LaneRow? paintRow;
    private double waveHeightAtStart;
    private Lane? wipeLane; // Draw strokes erase only in their own lane; null (Erase) erases in any lane
    private HashSet<EditHit> marqueeBase = [];

    public ArrangeView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

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

    public ArrangeModel? Model { get; private set; }

    private EditSession? Session => Model?.Session;

    public TimeAxis Axis { get; } = new();

    /// <summary>The playhead in ms (set by the window's frame timer).</summary>
    public double Playhead { get; set; }

    public Tool Tool { get; set; } = Tool.Select;

    /// <summary>Snap divisor (1/n of a beat) and whether snapping is on (J).</summary>
    public int Divisor { get; set; } = 4;

    public bool SnapOn { get; set; } = true;

    /// <summary>The "Lane details" toggle: show each lane's file index.</summary>
    public bool ShowIndexes { get; set; } = true;

    public IReadOnlyCollection<EditHit> Selection => selection;

    /// <summary>The moments the selected hits play on.</summary>
    public IReadOnlySet<MomentKey> SelectedMoments => selection.Where(h => h.Anchor is not null).Select(h => h.Anchor!.Value).ToHashSet();

    public event Action<Lane>? PreviewRequested;

    public event Action<double>? SeekRequested;

    /// <summary>Right-click on a lane name: the window shows the lane menu (rename, move, delete, add).</summary>
    public event Action<LaneRow>? LaneMenuRequested;

    /// <summary>The L and R cycle locators changed, or cycling was switched on or off from the ruler.</summary>
    public event Action? LocatorsChanged;

    public double? LocatorLeft { get; private set; }

    public double? LocatorRight { get; private set; }

    /// <summary>Cycle playback between the locators (R before L skips that stretch instead).</summary>
    public bool Cycle
    {
        get => cycle;
        set
        {
            if (cycle == value) return;
            cycle = value;
            LocatorsChanged?.Invoke();
            InvalidateVisual();
        }
    }

    private bool cycle;

    public void SetLocators(double left, double right)
    {
        (LocatorLeft, LocatorRight) = (left, right);
        LocatorsChanged?.Invoke();
        InvalidateVisual();
    }

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
        selection.Clear();
        drag = Drag.None;
        Axis.End = model?.LastTime ?? 0;
        InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Axis.Width = Math.Max(1, finalSize.Width - GridLeft);
        return base.ArrangeOverride(finalSize);
    }

    // ---------------------------------------------------------------- drawing

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

        using (ctx.PushClip(new Rect(0, HeaderHeight, size.Width, size.Height - HeaderHeight)))
        {
            int first = Math.Max(0, (int)(scrollY / RowHeight));
            for (int i = first; i < model.Rows.Count; i++)
            {
                double y = HeaderHeight + i * RowHeight - scrollY;
                if (y > size.Height) break;
                DrawRow(ctx, model, model.Rows[i], i, y, gridWidth);
            }

            using (ctx.PushClip(new Rect(GridLeft, HeaderHeight, gridWidth, size.Height - HeaderHeight)))
            {
                DrawConflictColumns(ctx, model, from, to, size.Height);

                // Snap lines only when they are at least 6 px apart.
                if (SnapOn && lines.Count > 0 && Axis.Zoom * lines[0].BeatLength / Divisor >= 6)
                    foreach (double t in model.SubLines(from, to, Divisor))
                    {
                        double x = GridLeft + Axis.X(t);
                        ctx.DrawLine(SubPen, new Point(x, HeaderHeight), new Point(x, size.Height));
                    }

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

                if (drag == Drag.Marquee)
                {
                    var r = new Rect(dragStart, dragNow).Normalize();
                    ctx.DrawRectangle(MarqueeFill, MarqueePen, r);
                }

                double px = GridLeft + Axis.X(Playhead);
                ctx.DrawLine(Paint.PlayheadPen, new Point(px, HeaderHeight), new Point(px, size.Height));
            }
        }

        DrawHeader(ctx, model, lines, size.Width, gridWidth);
        ctx.DrawLine(Paint.DividerPen, new Point(GridLeft - 0.5, 0), new Point(GridLeft - 0.5, size.Height));
    }

    /// <summary>The design's conflict marking: red 135° stripes down the whole column of a moment that won't save.</summary>
    private void DrawConflictColumns(DrawingContext ctx, ArrangeModel model, double from, double to, double height)
    {
        const double half = 6;
        var times = model.ProblemTimes;
        for (int i = Sorted.LowerBound(times, from - 20, t => t); i < times.Count && times[i] <= to + 20; i++)
        {
            double t = times[i];
            double x = GridLeft + Axis.X(t);
            var column = new Rect(x - half, HeaderHeight, half * 2, height - HeaderHeight);
            using (ctx.PushClip(column))
            {
                ctx.FillRectangle(StripeA, column);
                for (double y = HeaderHeight - half * 2; y < height; y += 10)
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(x - half, y), true);
                        c.LineTo(new Point(x - half, y + 5));
                        c.LineTo(new Point(x + half, y + 5 + half * 2));
                        c.LineTo(new Point(x + half, y + half * 2));
                        c.EndFigure(true);
                    }

                    ctx.DrawGeometry(StripeB, null, g);
                }
            }
        }
    }

    private void DrawHeader(DrawingContext ctx, ArrangeModel model, List<GridLine> lines, double width, double gridWidth)
    {
        ctx.FillRectangle(Paint.Chrome2, new Rect(0, 0, width, HeaderHeight));
        Paint.DrawText(ctx, "+", new Point(10, 10), 18, Paint.Dim, 0);
        Paint.DrawText(ctx, $"{model.LaneRows.Count} lanes · {model.GroupCount} groups", new Point(34, 14), 12, Paint.Dim, ListWidth - 20);

        using (ctx.PushClip(new Rect(GridLeft, 0, gridWidth, HeaderHeight)))
        {
            foreach (var s in model.Sections)
            {
                double x0 = GridLeft + Axis.X(s.Start), x1 = double.IsInfinity(s.End) ? width : GridLeft + Axis.X(s.End);
                if (x1 < GridLeft || x0 > width) continue;
                var rect = new Rect(x0, 2, Math.Max(2, x1 - x0), SectionsHeight - 4);
                ctx.DrawRectangle(Paint.Brush(Theme_.Color("section.fill"), Theme_.Number("section.opacity")), null, rect, 3, 3);
                Paint.DrawText(ctx, s.Name, new Point(Math.Max(x0, GridLeft) + 5, 3), 11, Paint.Text, 0);
            }

            double rulerTop = SectionsHeight;
            ctx.DrawLine(Paint.DividerPen, new Point(GridLeft, rulerTop), new Point(width, rulerTop));
            double lastLabel = double.NegativeInfinity;
            foreach (var line in lines)
            {
                double x = GridLeft + Axis.X(line.Time);
                ctx.DrawLine(line.IsBar ? Paint.BarPen : Paint.BeatPen, new Point(x, rulerTop + (line.IsBar ? 4 : 14)), new Point(x, RulerBottom));
                string label = line.IsBar ? line.Bar.ToString() : $"{line.Bar}.{line.Beat}";
                double labelWidth = line.IsBar ? 24 : 30;
                if (x - lastLabel < labelWidth + 6 || (!line.IsBar && Axis.Zoom * line.BeatLength < 48)) continue;
                Paint.DrawText(ctx, label, new Point(x + 3, rulerTop + 3), RulerFont, line.IsBar ? Paint.RulerBar : Paint.RulerBeat, 0, line.IsBar ? Paint.UiBold : Paint.Ui);
                lastLabel = x;
            }

            DrawLocators(ctx, width);
            DrawBookmarks(ctx);
            DrawWaveform(ctx, width);
            DrawObjects(ctx, model, width);

            double px = GridLeft + Axis.X(Playhead);
            ctx.FillRectangle(Paint.White, new Rect(px - 2, rulerTop, 4, RulerHeight));
        }

        ctx.DrawLine(Paint.DividerPen, new Point(0, HeaderHeight - 0.5), new Point(width, HeaderHeight - 0.5));
    }

    private static readonly IBrush CycleOn = Paint.Brush(Color.Parse("#2f6fd0"), 0.55), CycleOff = Paint.Brush(Color.Parse("#8e8e93"), 0.35),
        SkipOn = Paint.Brush(Color.Parse("#ff453a"), 0.45);

    private static readonly IBrush WaveFill = Paint.Brush(Color.Parse("#8fb7e8"), 0.75), StripBg = Paint.Brush("#1f1f21"),
        ObjectFill = Paint.Brush(Color.Parse("#c9c9cf"), 0.85), SliderBody = Paint.Brush(Color.Parse("#c9c9cf"), 0.35),
        SpinnerFill = Paint.Brush(Color.Parse("#c9a42a"), 0.45), BookmarkFill = Paint.Brush("#4cc3d1");
    private static readonly IPen ObjectConflictPen = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#ff453a"), 2);

    /// <summary>The song's waveform (set when decoding finishes) and its offset, so it lines up with the song as heard.</summary>
    public Audio.Waveform? Wave { get; set; }

    private StreamGeometry? waveGeometry;
    private (double, double, double, double, double, double, Audio.Waveform?) waveKey;

    public double SongOffset { get; set; }

    /// <summary>The waveform lane: one vertical line per pixel, the loudest peak in that pixel's time slice.</summary>
    private void DrawWaveform(DrawingContext ctx, double width)
    {
        double top = RulerBottom, h = WaveHeight;
        ctx.FillRectangle(StripBg, new Rect(GridLeft, top, width - GridLeft, h));
        ctx.DrawLine(Paint.DividerPen, new Point(GridLeft, top + h - 0.5), new Point(width, top + h - 0.5));
        if (Wave is not { } wave || h < 6) return;

        // The shape only changes on scroll, zoom, resize or offset: during playback the cached geometry is reused.
        var key = (Axis.Left, Axis.Zoom, width, h, SongOffset, GridLeft, wave);
        if (waveGeometry is null || !waveKey.Equals(key))
        {
            double mid = top + h / 2, half = h / 2 - 2, msPerPixel = 1 / Axis.Zoom;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                for (double x = GridLeft; x < width; x++)
                {
                    double t = Axis.Time(x - GridLeft) - SongOffset;
                    float peak = wave.Max(t, t + msPerPixel);
                    if (peak <= 0.002f) continue;
                    double a = Math.Max(0.5, peak * half);
                    g.BeginFigure(new Point(x, mid - a), true);
                    g.LineTo(new Point(x + 1, mid - a));
                    g.LineTo(new Point(x + 1, mid + a));
                    g.LineTo(new Point(x, mid + a));
                    g.EndFigure(true);
                }
            }

            (waveGeometry, waveKey) = (geometry, key);
        }

        ctx.DrawGeometry(WaveFill, null, waveGeometry);
    }

    /// <summary>
    /// The object strip: circles as dots, sliders as a body with H at the head, chevrons at repeats and E at the end,
    /// spinners as a bar. Objects with a conflict get a red outline.
    /// </summary>
    private void DrawObjects(DrawingContext ctx, ArrangeModel model, double width)
    {
        double top = RulerBottom + WaveHeight, h = ObjectsHeight, cy = top + h / 2;
        ctx.FillRectangle(Paint.Chrome2, new Rect(GridLeft, top, width - GridLeft, h));
        double from = Axis.Time(0) - 50, to = Axis.Time(width - GridLeft) + 50;
        var objects = model.Objects;
        for (int i = Sorted.LowerBound(objects, from - ObjectLookBack, o => o.Start); i < objects.Count && objects[i].Start <= to; i++)
        {
            var o = objects[i];
            if (o.End < from) continue;
            double x0 = GridLeft + Axis.X(o.Start), x1 = GridLeft + Axis.X(o.End);
            bool conflict = o.Nodes.Any(n => model.Conflicted.Contains(n.Key));
            var outline = conflict ? ObjectConflictPen : null;
            switch (o.Kind)
            {
                case HitObjectKind.Slider:
                    ctx.DrawRectangle(SliderBody, outline, new Rect(x0, cy - 6, Math.Max(2, x1 - x0), 12), 6, 6);
                    // Node letters only when there is room for them; otherwise dots.
                    bool labels = o.Nodes.Count < 2 || (x1 - x0) / (o.Nodes.Count - 1) >= 16;
                    foreach (var (time, _, position) in o.Nodes)
                    {
                        double x = GridLeft + Axis.X(time);
                        string mark = position switch { HitPosition.SliderHead => "H", HitPosition.SliderTail => "E", _ => "›" };
                        ctx.DrawEllipse(ObjectFill, null, new Point(x, cy), 5, 5);
                        if (labels) Paint.DrawText(ctx, mark, new Point(x - 3, cy - 7), 9, Paint.Divider, 0, Paint.UiBold);
                    }

                    break;

                case HitObjectKind.Spinner:
                    ctx.DrawRectangle(SpinnerFill, outline, new Rect(x0, cy - 5, Math.Max(2, x1 - x0), 10), 2, 2);
                    break;

                default:
                    ctx.DrawEllipse(ObjectFill, outline, new Point(x0, cy), 5, 5);
                    break;
            }
        }

        ctx.DrawLine(Paint.DividerPen, new Point(GridLeft, top + h - 0.5), new Point(width, top + h - 0.5));
    }

    /// <summary>Bookmark flags on the ruler (osu!'s editor bookmarks, saved in the .osu).</summary>
    private void DrawBookmarks(DrawingContext ctx)
    {
        if (Session is null) return;
        foreach (double t in Session.BookmarkTimes)
        {
            double x = GridLeft + Axis.X(t);
            var flag = new StreamGeometry();
            using (var g = flag.Open())
            {
                g.BeginFigure(new Point(x, SectionsHeight), true);
                g.LineTo(new Point(x + 7, SectionsHeight + 4));
                g.LineTo(new Point(x, SectionsHeight + 8));
                g.EndFigure(true);
            }

            ctx.DrawGeometry(BookmarkFill, null, flag);
            ctx.FillRectangle(BookmarkFill, new Rect(x, SectionsHeight, 1, RulerHeight));
        }
    }

    /// <summary>The cycle band in the upper ruler between the L and R flags: blue when cycling, red when it skips (R before L).</summary>
    private void DrawLocators(DrawingContext ctx, double width)
    {
        if (LocatorLeft is not { } l || LocatorRight is not { } r) return;
        double xl = GridLeft + Axis.X(l), xr = GridLeft + Axis.X(r);
        var band = new Rect(Math.Min(xl, xr), 1, Math.Abs(xr - xl), SectionsHeight - 2);
        ctx.FillRectangle(!Cycle ? CycleOff : r < l ? SkipOn : CycleOn, band);
        foreach (var (x, label) in new[] { (xl, "L"), (xr, "R") })
        {
            ctx.FillRectangle(Paint.White, new Rect(x - 0.5, 0, 1, HeaderHeight));
            ctx.FillRectangle(Paint.White, new Rect(x, 0, 12, 12));
            Paint.DrawText(ctx, label, new Point(x + 2, -1), 10, Paint.Divider, 0, Paint.UiBold);
        }
    }

    private void DrawRow(DrawingContext ctx, ArrangeModel model, Row row, int index, double y, double gridWidth)
    {
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
                string name = (lane.Lane.Source == LaneSource.StoryboardFile ? "SB " : "") + model.NameOf(lane.Lane);
                if (silent) name += lane.Lane.Missing ? "  (missing)" : "  (no sound)";
                Paint.DrawText(ctx, name, new Point(NumberWidth + 84, y + Pad + 4), LaneFont, silent ? Paint.MissingText : Paint.Text, ListWidth - 84 - 48);
                if (ShowIndexes && lane.Index is { } idx)
                    Paint.DrawText(ctx, $"idx {idx}", new Point(GridLeft - 44, y + Pad + 4), 11, Paint.Dim, 40, Paint.Mono);

                ctx.FillRectangle(Paint.Brush(color, Theme_.Number("lane.tint")), new Rect(GridLeft, y, gridWidth, RowHeight));
                break;
            }
        }

        ctx.DrawLine(Paint.DividerPen, new Point(0, y + RowHeight - 0.5), new Point(GridLeft + gridWidth, y + RowHeight - 0.5));
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
        for (int i = Sorted.LowerBound(notes, from - Axis.VisibleDuration, n => n.Time); i < notes.Count; i++)
        {
            var n = notes[i];
            if (n.Time > to + 50) break;
            if (n.EndTime < from - 50) continue; // spans end at EndTime; one-shot notes have EndTime == Time
            double x = GridLeft + Axis.X(n.Time);
            if (n.EndTime > n.Time)
                ctx.DrawRectangle(span, null, new Rect(x, cy - 3, Math.Max(2, (n.EndTime - n.Time) * Axis.Zoom), 6), 3, 3);
            else if (n.Passive)
                Paint.DrawDiamond(ctx, new Point(x, cy), NoteSize - 4, fill, Paint.NoteRingPen);
            else if (n.Hit is { } hit && selection.Contains(hit))
                Paint.DrawDiamond(ctx, new Point(x, cy), NoteSize, Paint.White, Paint.SelectedRingPen);
            else if (model.IsProblem(n.Hit!))
                Paint.DrawDiamond(ctx, new Point(x, cy), NoteSize, Paint.White, ConflictRing);
            else if (model.IsPreviewMuted(n.Hit!))
                Paint.DrawDiamond(ctx, new Point(x, cy), NoteSize, Paint.MutedFill, Paint.NoteRingPen);
            else
                Paint.DrawDiamond(ctx, new Point(x, cy), NoteSize, fill, Paint.NoteRingPen);
        }
    }

    // ---------------------------------------------------------------- scrolling and zoom

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
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            DivisorWheel?.Invoke(e.Delta.Y > 0 ? 1 : -1); // finer up, coarser down
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            Axis.ZoomAt(e.Delta.Y > 0 ? 1.25 : 0.8, Math.Max(0, p.X - GridLeft));
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Delta.X != 0)
            Axis.Left -= (e.Delta.Y + e.Delta.X) * 120 / Axis.Zoom;
        else
            ScrollY -= e.Delta.Y * RowHeight * 3;
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>Alt+wheel: +1 for a finer snap, -1 for a coarser one (the window owns the divisor list).</summary>
    public event Action<int>? DivisorWheel;

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

    /// <summary>Lets Ctrl+Alt+wheel zoom go and follows the theme's row height again (after it's edited).</summary>
    public void FollowThemeRowHeight()
    {
        rowHeight = null;
        ScrollY = scrollY;
    }

    // ---------------------------------------------------------------- hit-testing

    // Where the lane list's buttons sit, from the number column's right edge. Drawing and hit-testing both use these.
    // How far back to look for objects that started before the view (long sliders and spinners).
    private const double ObjectLookBack = 60000;

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

    private int RowIndexAt(double y) => (int)Math.Floor((y - HeaderHeight + scrollY) / RowHeight);

    private Row? RowAt(double y)
    {
        int i = RowIndexAt(y);
        return Model is { } model && y >= HeaderHeight && i >= 0 && i < model.Rows.Count ? model.Rows[i] : null;
    }

    /// <summary>The editable hit nearest a grid x in a lane, within a note's width.</summary>
    private EditHit? HitUnder(LaneRow row, double gridX)
    {
        if (Model?.Notes.TryGetValue(row.Lane, out var notes) != true) return null;
        int i = Sorted.Nearest(notes!, Axis.Time(gridX), NoteSize / Axis.Zoom, n => n.Time, n => n.Hit is not null);
        return i >= 0 ? notes![i].Hit : null;
    }

    private double SnapTime(double time) => SnapOn && Model is { } m ? m.Snap(time, Divisor) : time;

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

        if (row is LaneRow lane && HitUnder(lane, p.X - GridLeft) is not null) return "note";
        return row is GroupRow ? "grouprow" : "lanerow";
    }

    // ---------------------------------------------------------------- editing with the mouse

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        if (Model is not { } model || Session is not { } session) return;
        var point = e.GetCurrentPoint(this);
        var p = point.Position;
        bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (p.Y < HeaderHeight)
        {
            if (p.X < GridLeft || !point.Properties.IsLeftButtonPressed) return;
            double t = Math.Max(0, Axis.Time(p.X - GridLeft));
            double waveBottom = RulerBottom + WaveHeight;
            if (p.Y >= RulerBottom && Math.Abs(p.Y - waveBottom) <= 4)
            {
                // The line under the waveform: drag to resize it, double-click to reset.
                if (e.ClickCount == 2) WaveHeight = DefaultWaveHeight;
                else
                {
                    drag = Drag.ResizeWave;
                    waveHeightAtStart = WaveHeight;
                    dragStart = dragNow = p;
                    e.Pointer.Capture(this);
                }

                ScrollY = scrollY;
                ScrollChanged?.Invoke();
            }
            else if (p.Y >= waveBottom)
                ObjectPressed(model, session, t, e.ClickCount == 2);
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) SetLocators(SnapTime(t), LocatorRight ?? SnapTime(t));
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) SetLocators(LocatorLeft ?? SnapTime(t), SnapTime(t));
            else if (p.Y < SectionsHeight)
            {
                // The upper ruler: drag to draw a cycle range; a click inside the band toggles cycling.
                drag = Drag.Cycle;
                dragStart = dragNow = p;
                e.Pointer.Capture(this);
            }
            else
            {
                // The ruler and the waveform move the playhead; a click near a bookmark flag lands exactly on it.
                double near = 5 / Axis.Zoom;
                var bookmark = session.NearestBookmark(t, near);
                SeekRequested?.Invoke(p.Y < RulerBottom && bookmark is { } b ? b : t);
            }

            InvalidateVisual();
            return;
        }

        if (RowAt(p.Y) is not { } row) return;
        if (p.X < GridLeft)
        {
            if (point.Properties.IsRightButtonPressed && row is LaneRow menuLane)
            {
                LaneMenuRequested?.Invoke(menuLane);
                return;
            }

            if (!point.Properties.IsLeftButtonPressed) return;
            switch (row, ListTargetAt(row, p.X - NumberWidth))
            {
                case (GroupRow g, ListTarget.Collapse): model.ToggleCollapsed(g.Group); break;
                case (GroupRow g, ListTarget.Mute): model.ToggleGroupMute(g.Group); break;
                case (GroupRow g, ListTarget.Solo): model.ToggleGroupSolo(g.Group); break;
                case (LaneRow l, ListTarget.Mute): model.ToggleMute(l.Lane); break;
                case (LaneRow l, ListTarget.Solo): model.ToggleSolo(l.Lane); break;
                case (LaneRow l, _) when additive:
                    // Ctrl+click selects the lane's hits; Shift+click adds them.
                    var laneHits = session.State.Hits.Where(h => h.Lane == l.Lane);
                    SetSelection(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? selection.Concat(laneHits).ToList() : laneHits);
                    break;
                case (LaneRow l, _): PreviewRequested?.Invoke(l.Lane); break;
            }

            return;
        }

        double gridX = p.X - GridLeft;
        var under = row is LaneRow lr ? HitUnder(lr, gridX) : null;

        // Right-click deletes a hit, whatever the tool.
        if (point.Properties.IsRightButtonPressed)
        {
            if (under is not null)
            {
                EraseHit(under);
                NotifySelection();
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;
        var tool = e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? Tool.Draw : Tool;
        dragStart = dragNow = p;
        e.Pointer.Capture(this);

        switch (tool)
        {
            case Tool.Draw when row is not LaneRow:
                drag = Drag.None;
                break;

            case Tool.Draw when row is LaneRow lane && under is null:
                session.BeginGesture();
                drag = Drag.Paint;
                paintRow = lane;
                session.Add([session.HitAt(lane.Lane, SnapTime(Axis.Time(gridX)))]);
                PreviewRequested?.Invoke(lane.Lane);
                break;

            case Tool.Draw or Tool.Erase:
                // Erase strokes, and Draw strokes that start on a hit: Draw erases only in its own lane, Erase in any lane.
                session.BeginGesture();
                drag = Drag.Wipe;
                wipeLane = tool == Tool.Draw ? (row as LaneRow)?.Lane : null;
                if (under is not null) EraseHit(under);
                break;

            case Tool.Mute:
                // Preview-only: osu! can't store a muted sound, so this only silences it while listening.
                if (under is not null) model.TogglePreviewMute(under);
                drag = Drag.None;
                break;

            // The Ramp tool works in the volume lane; in the grid it selects like Select.
            case Tool.Select or Tool.Ramp when under is not null:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    if (!selection.Remove(under)) selection.Add(under);
                }
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) selection.Add(under);
                else if (!selection.Contains(under))
                {
                    selection.Clear();
                    selection.Add(under);
                }

                if (selection.Contains(under))
                {
                    drag = Drag.Move;
                    dragAnchor = under;
                    dragHits = [.. selection];
                    session.BeginGesture();
                }

                SelectionChanged?.Invoke();
                break;

            case Tool.Select or Tool.Ramp:
                drag = Drag.Marquee;
                marqueeBase = additive ? [.. selection] : [];
                if (marqueeBase.Count == 0 && selection.Count > 0)
                {
                    selection.Clear();
                    SelectionChanged?.Invoke();
                }

                break;
        }

        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (drag == Drag.None || Model is not { } model || Session is not { } session) return;
        var p = e.GetPosition(this);
        dragNow = p;
        double gridX = p.X - GridLeft;

        switch (drag)
        {
            case Drag.Paint when paintRow is not null:
                session.Add([session.HitAt(paintRow.Lane, SnapTime(Axis.Time(gridX)))]); // Add ignores a hit already there
                break;

            case Drag.Wipe:
            {
                var row = RowAt(p.Y) as LaneRow;
                if (row is not null && (wipeLane is null || row.Lane == wipeLane) && HitUnder(row, gridX) is { } under) EraseHit(under);
                break;
            }

            case Drag.Move when dragAnchor is not null:
            {
                // The grabbed hit snaps; the others keep their offsets to it. Rows move by whole visible lanes.
                double offset = SnapTime(dragAnchor.Time + Axis.Time(gridX) - Axis.Time(dragStart.X - GridLeft)) - dragAnchor.Time;
                int rowShift = RowIndexAt(p.Y) - RowIndexAt(dragStart.Y);
                session.ResetToGestureStart();
                selection.Clear();
                selection.UnionWith(session.Move(dragHits, offset, h => ShiftLane(model, h.Lane, rowShift)));
                break;
            }

            case Drag.Marquee:
            {
                selection.Clear();
                selection.UnionWith(marqueeBase);
                selection.UnionWith(HitsIn(new Rect(dragStart, p).Normalize()));
                break;
            }

            case Drag.ResizeWave:
                WaveHeight = Math.Clamp(waveHeightAtStart + p.Y - dragStart.Y, 0, 200);
                ScrollY = scrollY;
                ScrollChanged?.Invoke();
                break;

            case Drag.Cycle when Math.Abs(p.X - dragStart.X) >= 3:
            {
                double a = SnapTime(Math.Max(0, Axis.Time(dragStart.X - GridLeft))), b = SnapTime(Math.Max(0, Axis.Time(gridX)));
                Cycle = true;
                SetLocators(Math.Min(a, b), Math.Max(a, b));
                break;
            }
        }

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (drag == Drag.None) return;
        if (drag == Drag.Cycle && Math.Abs(dragNow.X - dragStart.X) < 3 && LocatorLeft is { } l && LocatorRight is { } r)
        {
            // A click (no drag) inside the band toggles cycling.
            double t = Axis.Time(dragStart.X - GridLeft);
            if (t >= Math.Min(l, r) && t <= Math.Max(l, r))
            {
                Cycle = !Cycle; // raises LocatorsChanged
            }
        }

        Session?.EndGesture();
        drag = Drag.None;
        dragAnchor = null;
        paintRow = null;
        e.Pointer.Capture(null);
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        Session?.EndGesture();
        drag = Drag.None;
        base.OnPointerCaptureLost(e);
    }

    private IEnumerable<EditHit> HitsIn(Rect area)
    {
        if (Model is not { } model) yield break;
        double from = Axis.Time(area.Left - GridLeft), to = Axis.Time(area.Right - GridLeft);
        for (int i = 0; i < model.Rows.Count; i++)
        {
            double y = HeaderHeight + i * RowHeight - scrollY + RowHeight / 2;
            if (y < area.Top || y > area.Bottom || model.Rows[i] is not LaneRow lane || !model.Notes.TryGetValue(lane.Lane, out var notes)) continue;
            foreach (var n in notes)
                if (n.Hit is { } hit && n.Time >= from && n.Time <= to) yield return hit;
        }
    }

    /// <summary>The lane <paramref name="shift"/> visible lane rows away (group rows skipped), clamped to the list.</summary>
    private static Lane ShiftLane(ArrangeModel model, Lane lane, int shift)
    {
        if (shift == 0) return lane;
        var lanes = model.Rows.OfType<LaneRow>().Select(r => r.Lane).ToList();
        int at = lanes.IndexOf(lane);
        return at < 0 ? lane : lanes[Math.Clamp(at + shift, 0, lanes.Count - 1)];
    }


    /// <summary>Double-clicking an object asks to hear its hits (at their moments' volumes).</summary>
    public event Action<IReadOnlyList<EditHit>>? HitsPreviewRequested;

    /// <summary>
    /// A click in the object strip: moves the playhead to the object (or the slider node clicked) and selects its hits;
    /// a click on a slider's body selects the hits of all its nodes. A double-click also plays them.
    /// </summary>
    private void ObjectPressed(ArrangeModel model, EditSession session, double time, bool doubleClick)
    {
        if (model.ObjectAt(time, 6 / Axis.Zoom) is not { } o) return;
        var node = o.Nodes.MinBy(n => Math.Abs(n.Time - time));
        bool onNode = Math.Abs(node.Time - time) <= 7 / Axis.Zoom;
        var keys = onNode ? [node.Key] : o.Nodes.Select(n => n.Key).ToHashSet();
        var hits = session.State.Hits.Where(h => h.Anchor is { } k && keys.Contains(k)).ToList();
        SetSelection(hits);
        SeekRequested?.Invoke(onNode ? node.Time : o.Start);
        if (doubleClick) HitsPreviewRequested?.Invoke(hits);
    }
    // ---------------------------------------------------------------- editing commands (keyboard and menus)

    private void SetSelection(IEnumerable<EditHit> hits)
    {
        selection.Clear();
        selection.UnionWith(hits);
        NotifySelection();
    }

    private void NotifySelection()
    {
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Removes one hit and drops it from the selection (right-click, Draw strokes that start on a hit, Erase).</summary>
    private void EraseHit(EditHit hit)
    {
        Session?.Remove([hit]);
        selection.Remove(hit);
    }

    /// <summary>Moves the selected hits and keeps them selected.</summary>
    private void MoveSelection(double offset, Func<EditHit, Lane> laneOf)
    {
        if (Session is not { } session || selection.Count == 0) return;
        SetSelection(session.Move(selection.ToList(), offset, laneOf));
    }

    public void SelectAll()
    {
        if (Session is not null) SetSelection(Session.State.Hits);
    }

    public void ClearSelection() => SetSelection([]);

    public void SelectWhere(Func<EditHit, bool> predicate)
    {
        if (Session is not null) SetSelection(Session.State.Hits.Where(predicate));
    }

    public void DeleteSelection()
    {
        if (Session is null || selection.Count == 0) return;
        Session.Remove(selection.ToList());
        ClearSelection();
    }

    /// <summary>Left/Right: by one snap step, keeping offsets (Alt: 1 ms). Ctrl+Left/Right: to the previous/next grid line.</summary>
    public void Nudge(int direction, bool fine, bool toGrid)
    {
        if (Session is not { } session || Model is not { } model || selection.Count == 0) return;
        var anchor = selection.MinBy(h => h.Time)!;
        double offset;
        if (fine) offset = direction;
        else
        {
            double step = (model.RedLineAt(anchor.Time)?.BeatLength ?? 500) / Divisor;
            offset = toGrid ? model.Snap(anchor.Time + direction * step * 0.51, Divisor) - anchor.Time : direction * step;
            if (toGrid && Math.Abs(offset) < 1e-6) offset = direction * step;
        }

        MoveSelection(offset, h => h.Lane);
    }

    /// <summary>Ctrl+Up/Down: move the selection to the adjacent visible lane.</summary>
    public void ShiftSelectedLanes(int direction)
    {
        if (Model is { } model) MoveSelection(0, h => ShiftLane(model, h.Lane, direction));
    }

    private List<(Lane Lane, double Offset)> clipboard = [];

    public void Copy()
    {
        if (selection.Count == 0) return;
        double start = selection.Min(h => h.Time);
        clipboard = [.. selection.Select(h => (h.Lane, h.Time - start))];
    }

    public void Cut()
    {
        Copy();
        DeleteSelection();
    }

    /// <summary>Pastes at <paramref name="time"/> (snapped), selecting the pasted hits.</summary>
    public void Paste(double time)
    {
        if (Session is not { } session || clipboard.Count == 0) return;
        double at = SnapTime(time);
        var hits = clipboard.Select(c => session.HitAt(c.Lane, at + c.Offset)).Distinct().ToList();
        session.Add(hits);
        SetSelection(hits);
    }

    /// <summary>Ctrl+D: copies the selection right after itself (its length rounded up to a snap step).</summary>
    public void Duplicate()
    {
        if (selection.Count == 0 || Model is not { } model) return;
        double start = selection.Min(h => h.Time), end = selection.Max(h => h.Time);
        double step = (model.RedLineAt(start)?.BeatLength ?? 500) / Divisor;
        Copy();
        Paste(start + Math.Max(step, Math.Ceiling((end - start + step / 2) / step) * step));
    }

    /// <summary>Drops selected hits that no longer exist (after undo or redo).</summary>
    public void PruneSelection()
    {
        if (Session is null) return;
        var present = Session.State.Hits.ToHashSet();
        if (selection.RemoveWhere(h => !present.Contains(h)) > 0) SelectionChanged?.Invoke();
    }
}

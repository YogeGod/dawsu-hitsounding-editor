using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

/// <summary>The key editor's tools, numbered as Cubase's (1 Select, 3 Split, 4 Glue, 5 Erase, 8 Draw).</summary>
internal enum KeyTool { Select, Grab, Draw, Erase, Split, Glue, Mute, Ramp }

/// <summary>
/// Keysound mode's key editor, after Cubase's Key Editor: a ruler, a strip with the reference's gameplay objects over the
/// song waveform, a piano keyboard beside the note grid, and a velocity lane. It edits the selected track of a
/// <see cref="KeysoundSession"/>; other tracks can show as ghost notes and are never edited here.
/// </summary>
internal sealed class PianoRollView : Avalonia.Controls.Control
{
    public const double KeyboardWidth = 64, RulerHeight = 24, StripHeight = 46, MinRow = 6, MaxRow = 30;

    /// <summary>The velocity lane at the bottom (0 while the controller is hidden), as the Hitsound editor's controller.</summary>
    public double VelocityLaneHeight { get; set; } = 126;
    /// <summary>The space over the velocity lane that the editor covers with the scrollbar and the controller's title row.</summary>
    public double BottomReserve { get; set; } = 54;
    private const double EdgeGrab = 6, DragThreshold = 3;

    private static readonly IBrush WhiteKey = Paint.Brush("#e8e8ea"), BlackKey = Paint.Brush("#1a1a1c"), KeyText = Paint.Brush("#55565c");
    private static readonly IBrush Ghost = Paint.Brush(Color.Parse("#8a8f99"), 0.28), WaveFill = Paint.Brush(Color.Parse("#5b6b80"), 0.45);
    private static readonly IBrush BoxFill = Paint.Brush(Colors.White, 0.08), SpinnerFill = Paint.Brush(Color.Parse("#c7c9d1"), 0.55);
    private static readonly IPen RowLine = new ImmutablePen(Paint.Brush(Colors.Black, 0.35), 1), OctaveLine = new ImmutablePen(Paint.Brush(Colors.Black, 0.7), 1);
    private static readonly IPen NoteEdge = new ImmutablePen(Paint.Brush(Colors.Black, 0.55), 1);
    private static readonly IBrush RecordFill = Paint.Brush(Color.Parse("#e5484d"), 0.8);
    // A synced note's start edge; a target that moved or is gone shows in orange (Sync again).
    private static readonly IBrush SyncedMark = Paint.Brush("#3ddc84"), StaleMark = Paint.Brush("#ff8a5c");
    private static readonly IPen WhiteRing = new ImmutablePen(Brushes.White, 1.5); // selected notes and object heads
    private static readonly IPen BoxPen = new ImmutablePen(Paint.Brush(Colors.White, 0.6), 1, new ImmutableDashStyle([3, 3], 0));
    // osu!'s default combo colours, for references that set none.
    private static readonly Color[] DefaultCombo = [Color.Parse("#ffc000"), Color.Parse("#00ca00"), Color.Parse("#127cff"), Color.Parse("#f21839")];
    private static readonly IBrush[] VelocityFills = [.. Enumerable.Range(0, 128).Select(v => (IBrush)Paint.Brush(VelocityColor(v)))];
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast), HandCursor = new(StandardCursorType.Hand);

    private KeysoundDocument? doc;
    private readonly HashSet<Guid> selection = [];
    private Drag? drag;
    private (IBrush Body, IBrush Head)[] comboFills = [];
    private readonly Dictionary<int, FormattedText> comboLabels = [];
    private double objectLookBack; // the longest object, so objects starting before the view but reaching into it are found
    private (double Left, double Zoom, double Width, Waveform Wave)? waveKey;
    private StreamGeometry? waveGeometry;
    private double rowHeight = 12, topPitch = 84; // the pitch at the top edge of the grid (fractional while scrolling)

    public TimeAxis Axis { get; } = new();
    public Waveform? Wave { get; set; }
    public double Playhead { get; set; }
    public KeyTool Tool { get; set; } = KeyTool.Select;
    public bool SnapOn { get; set; } = true;
    /// <summary>The snap spacing, as the Hitsound editor's: whole beats (2/1, 3/1, 4/1) or a fraction of one.</summary>
    public SnapSpacing Spacing { get; set; } = new(1, 4);
    /// <summary>Grid, Grid relative (moves keep their offset from the grid) or Objects (the reference's object moments).</summary>
    public SnapMode SnapMode { get; set; } = SnapMode.Grid;
    /// <summary>Notes silenced while listening (the Mute tool). Never saved and Generate ignores it, as in the Hitsound editor.</summary>
    public HashSet<Guid> PreviewMuted { get; } = [];
    /// <summary>The Mute tool silenced or restored a note.</summary>
    public event Action? MuteChanged;
    /// <summary>Right-click in the grid: show the tool picker at the pointer (it never edits a note).</summary>
    public event Action? ToolPickerRequested;
    /// <summary>The ruler's bookmarks: the editor keeps them in the project's state.</summary>
    public IReadOnlyList<KeyBookmark> Bookmarks => doc?.Session.State.Bookmarks ?? [];
    public bool ShowGhosts { get; set; } = true;
    public bool ShowMarkers { get; set; } = true;
    /// <summary>Notes being recorded, drawn over the selected track until the take ends.</summary>
    public IReadOnlyList<KeyNote>? RecordingPreview { get; set; }

    /// <summary>Cubase's Note Insert Velocity: what drawn notes get.</summary>
    public int InsertVelocity { get; set; } = 100;

    /// <summary>A note to hear now (pitch, velocity): a clicked key, a drawn, selected or re-pitched note.</summary>
    public event Action<int, int>? AuditionRequested;

    /// <summary>Clicks on the ruler or strip move the playhead.</summary>
    public event Action<double>? SeekRequested;

    /// <summary>Alt+wheel (osu! keys: Ctrl+wheel): a finer (+1) or coarser (-1) snap.</summary>
    public event Action<int>? DivisorWheel;

    /// <summary>The osu! editor's wheel: move the playhead by this many snap steps (later = +).</summary>
    public event Action<int>? PlayheadStepRequested;

    /// <summary>The next grid line from <paramref name="time"/> in <paramref name="direction"/>, at the snap's spacing.</summary>
    public double GridStep(double time, int direction)
    {
        if (doc is null) return time;
        double step = Spacing.Step(doc.Tempo.SegmentAtTime(time).BeatLength);
        double target = doc.Tempo.Snap(time + direction * step * 0.51, Spacing.Beats, Spacing.Divisions);
        return Math.Max(0, Math.Abs(target - time) < 0.5 ? time + direction * step : target);
    }
    /// <summary>Ctrl+click (left) or Alt+click (right) in the ruler, as in Hitsound mode: set that locator here.</summary>
    public event Action<bool, double>? LocatorRequested;
    /// <summary>The upper half of the ruler, as in Cubase: a drawn, moved or resized cycle range (left, right; true for a new one).</summary>
    public event Action<double, double, bool>? CycleRangeChanged;
    /// <summary>A click inside the cycle range in the upper ruler: turn cycling on or off.</summary>
    public event Action? CycleToggleRequested;

    public double? LocatorLeft { get; set; }
    public double? LocatorRight { get; set; }
    /// <summary>Cycle is on: the range between the locators is drawn as the active cycle.</summary>
    public bool Cycle { get; set; }
    /// <summary>The song's listening offset (ms): the waveform moves with it, as the song does.</summary>
    public double SongOffset { get; set; }
    /// <summary>Selection, tool or view changes the editor shows (not edits: those come from the session).</summary>
    public event Action? ViewChanged;
    /// <summary>The view scrolled or zoomed (time or key rows): the scrollbars and the overview follow.</summary>
    public event Action? ScrollChanged;

    public PianoRollView()
    {
        Focusable = true;
        ClipToBounds = true;
        Axis.Changed += InvalidateVisual;
        Axis.Changed += () => ScrollChanged?.Invoke();
    }

    private KeysoundSession? Session => doc?.Session;
    private KeyTrack? Track => doc?.SelectedTrack is { } id ? Session?.State.Track(id) : null;
    private Rect Grid => new(KeyboardWidth, RulerHeight + StripHeight, Math.Max(0, Bounds.Width - KeyboardWidth), Math.Max(0, Bounds.Height - RulerHeight - StripHeight - VelocityLaneHeight - BottomReserve));
    private Rect VelocityArea => new(KeyboardWidth, Bounds.Height - VelocityLaneHeight, Math.Max(0, Bounds.Width - KeyboardWidth), VelocityLaneHeight);

    /// <summary>The grid's height in px and key rows, for the vertical scrollbar.</summary>
    public double GridHeight => Grid.Height;
    public double RowHeight => rowHeight;
    /// <summary>The pitch at the top edge of the grid (fractional while scrolling); the vertical scrollbar's value is 127 minus it.</summary>
    public double TopPitch
    {
        get => topPitch;
        set { topPitch = value; ScrollPitch(0); }
    }

    /// <summary>Key row height in px, for the row-height slider.</summary>
    public void SetRowHeight(double height)
    {
        if (Math.Abs(height - rowHeight) < 0.01) return;
        ZoomRows(height / rowHeight);
    }

    public void Attach(KeysoundDocument? document)
    {
        if (doc is not null) doc.Session.Changed -= OnSessionChanged;
        doc = document;
        selection.Clear();
        PreviewMuted.Clear();
        drag = null;
        objectMoments = [];
        if (doc is not null)
        {
            doc.Session.Changed += OnSessionChanged;
            Axis.End = Math.Max(doc.Objects.Count > 0 ? doc.Objects[^1].End + 2000 : 0, 60_000);
            Axis.Zoom = 0.45; // about 2.2 s per 1000 px, so objects and notes are readable from the start
            Axis.Left = (doc.Objects.Count > 0 ? doc.Objects[0].Start : doc.Tempo.Segments[0].Time) - 500;
            objectLookBack = doc.Objects.Select(o => o.End - o.Start).DefaultIfEmpty(0).Max();
            objectMoments = [.. doc.Objects.SelectMany(o => o.Nodes.Select(n => n.Time)).Order()];
            Color[] colours = doc.ComboColours.Count > 0 ? [.. doc.ComboColours.Select(c => Color.FromRgb(c.R, c.G, c.B))] : DefaultCombo;
            comboFills = [.. colours.Select(c => ((IBrush)Paint.Brush(c, 0.55), (IBrush)Paint.Brush(c)))];
        }

        InvalidateVisual();
    }

    /// <summary>Called when the selected track changes: the selection belongs to one track.</summary>
    public void TrackChanged()
    {
        selection.Clear();
        drag = null;
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    private void OnSessionChanged()
    {
        // Undo may remove notes that are selected. During a drag the selection is the drag's own (an Alt+drag selects copies
        // that come and go with each move).
        if (drag is not null) { }
        else if (Track is { } t) selection.IntersectWith(t.Notes.Select(n => n.Id));
        else selection.Clear();
        InvalidateVisual();
    }

    protected override void OnSizeChanged(Avalonia.Controls.SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Axis.Width = Grid.Width;
        ScrollChanged?.Invoke();
    }

    // Coordinates.

    private double XOfTime(double time) => KeyboardWidth + Axis.X(time);
    private double XOfTick(double tick) => XOfTime(doc!.Tempo.TimeAt(tick));
    private double TimeOfX(double x) => Axis.Time(x - KeyboardWidth);
    private double YOfPitch(double pitch) => Grid.Top + (topPitch - pitch) * rowHeight;
    private int PitchOfY(double y) => Math.Clamp((int)Math.Floor(topPitch - (y - Grid.Top) / rowHeight) + 1, KeyNote.MinPitch, KeyNote.MaxPitch);

    private static bool IsBlack(int pitch) => (pitch % 12) is 1 or 3 or 6 or 8 or 10;
    // Cubase's names: middle C (60) is C3.
    internal static string PitchName(int pitch) => $"{NoteNames[pitch % 12]}{pitch / 12 - 2}";

    /// <summary>Velocity colour, as in Cubase's default: blue (soft) through purple to red (loud).</summary>
    private static Color VelocityColor(int velocity) => Paint.LevelColor((velocity - 1) / 126.0);

    private static IBrush VelocityFill(int velocity) => VelocityFills[Math.Clamp(velocity, 0, 127)];

    private (long From, long To) TickRange(double from, double to) =>
        ((long)Math.Floor(doc!.Tempo.TickAt(from)), (long)Math.Ceiling(doc.Tempo.TickAt(to)));

    /// <summary>The pitches with a row on screen, top to bottom, with each row's top edge.</summary>
    private IEnumerable<(int Pitch, double Y)> VisiblePitches()
    {
        int top = Math.Min(KeyNote.MaxPitch, (int)Math.Ceiling(topPitch)), bottom = Math.Max(KeyNote.MinPitch, PitchOfY(Grid.Bottom) - 1);
        for (int p = top; p >= bottom; p--) yield return (p, YOfPitch(p));
    }

    private double[] objectMoments = [];

    /// <summary>A time on the snap: the grid, or with snap mode Objects the nearest gameplay-object moment.</summary>
    private double SnapTime(double time)
    {
        if (!SnapOn) return time;
        if (SnapMode != SnapMode.Objects || objectMoments.Length == 0) return doc!.Tempo.Snap(time, Spacing.Beats, Spacing.Divisions);
        int next = Sorted.LowerBound(objectMoments, time, x => x);
        if (next == 0) return objectMoments[0];
        if (next == objectMoments.Length) return objectMoments[^1];
        double before = objectMoments[next - 1], after = objectMoments[next];
        return time - before <= after - time ? before : after;
    }

    /// <summary>
    /// Where a note at <paramref name="time"/> lands when dragged by <paramref name="raw"/> ms: on the snap, or with Grid
    /// relative by whole grid steps, keeping its offset from its own grid line (as in the Hitsound editor).
    /// </summary>
    private double SnapMoved(double time, double raw) => SnapOn && SnapMode == SnapMode.GridRelative
        ? time + doc!.Tempo.Snap(time + raw, Spacing.Beats, Spacing.Divisions) - doc.Tempo.Snap(time, Spacing.Beats, Spacing.Divisions)
        : SnapTime(time + raw);

    /// <summary>
    /// The grid cell's start at or before a time: where Draw puts a note. Ticks count beats, so one step is the same number
    /// of ticks under every red line.
    /// </summary>
    private double FloorSnap(double time)
    {
        if (!SnapOn) return time;
        if (SnapMode == SnapMode.Objects) return SnapTime(time);
        double snapped = doc!.Tempo.Snap(time, Spacing.Beats, Spacing.Divisions);
        return snapped <= time + 0.01 ? snapped : doc.Tempo.Snap(time - Spacing.Step(doc.Tempo.SegmentAtTime(time).BeatLength), Spacing.Beats, Spacing.Divisions);
    }

    private long StepTicks => Math.Max(1, TempoMap.Ppq * Spacing.Beats / Spacing.Divisions);

    // Drawing.

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        ctx.FillRectangle(Paint.Bg, new Rect(b.Size));
        if (doc is null) return;
        var grid = Grid;
        double from = TimeOfX(KeyboardWidth), to = TimeOfX(b.Width);

        using (ctx.PushClip(grid)) DrawRows(ctx, grid);
        var lines = doc.Tempo.GridLines(from, to, VisibleDivisions()).ToList();
        var objects = VisibleObjects(from - 200, to + 200).ToList();
        var ticks = TickRange(from, to);
        using (ctx.PushClip(new Rect(KeyboardWidth, RulerHeight, grid.Width, b.Height - RulerHeight)))
        {
            foreach (var (time, level) in lines)
            {
                double x = Math.Round(XOfTime(time)) + 0.5;
                var pen = level switch { 0 => Paint.BarPen, 1 => Paint.BeatPen, _ => RowLine };
                ctx.DrawLine(pen, new Point(x, grid.Top), new Point(x, grid.Bottom));
                if (VelocityLaneHeight > 0) ctx.DrawLine(pen, new Point(x, VelocityArea.Top), new Point(x, VelocityArea.Bottom));
            }

            if (ShowMarkers)
                foreach (var o in objects)
                    foreach (var (time, _) in o.Nodes)
                    {
                        double x = Math.Round(XOfTime(time)) + 0.5;
                        ctx.DrawLine(Paint.ObjectLinePen, new Point(x, grid.Top), new Point(x, grid.Bottom));
                    }
        }

        using (ctx.PushClip(grid)) DrawNotes(ctx, ticks);
        DrawKeyboard(ctx, grid);
        DrawRuler(ctx, lines, b.Width);
        DrawStrip(ctx, objects, b.Width);
        if (VelocityLaneHeight > 0) DrawVelocity(ctx, ticks);

        if (drag is { Kind: DragKind.Box } box)
        {
            var r = new Rect(box.Start, box.Current).Normalize().Intersect(grid);
            ctx.DrawRectangle(BoxFill, BoxPen, r);
        }

        double px = XOfTime(Playhead);
        if (px >= KeyboardWidth && px <= b.Width)
            ctx.DrawLine(Paint.PlayheadPen, new Point(Math.Round(px) + 0.5, 0), new Point(Math.Round(px) + 0.5, b.Height));
    }

    // Finer grid lines only when they are at least 8 px apart.
    private int VisibleDivisions()
    {
        double beatPx = doc!.Tempo.SegmentAtTime(TimeOfX(KeyboardWidth)).BeatLength * Axis.Zoom;
        int d = Spacing.Divisions;
        while (d > 1 && beatPx / d < 8) d = d % 2 == 0 ? d / 2 : 1;
        return d;
    }

    private IEnumerable<KeyObject> VisibleObjects(double from, double to)
    {
        var objects = doc!.Objects;
        int i = Sorted.LowerBound(objects, from - objectLookBack, o => o.Start);
        for (; i < objects.Count && objects[i].Start <= to; i++)
            if (objects[i].End >= from) yield return objects[i];
    }

    private void DrawRows(DrawingContext ctx, Rect grid)
    {
        IBrush white = Paint.RollWhite, black = Paint.RollBlack;
        foreach (var (p, y) in VisiblePitches())
        {
            ctx.FillRectangle(IsBlack(p) ? black : white, new Rect(grid.Left, y, grid.Width, rowHeight));
            ctx.DrawLine(p % 12 == 0 ? OctaveLine : RowLine, new Point(grid.Left, y + rowHeight - 0.5), new Point(grid.Right, y + rowHeight - 0.5));
        }
        // Grid Colors, Cycle Region, as in the Hitsound grid.
        if (Cycle && Paint.GridCycle is { } shade && LocatorLeft is { } l && LocatorRight is { } r && r > l)
        {
            double x0 = Math.Max(grid.Left, XOfTime(l)), x1 = Math.Min(grid.Right, XOfTime(r));
            if (x1 > x0) ctx.FillRectangle(shade, new Rect(x0, grid.Top, x1 - x0, grid.Height));
        }
    }

    private void DrawKeyboard(DrawingContext ctx, Rect grid)
    {
        ctx.FillRectangle(Paint.Chrome2, new Rect(0, 0, KeyboardWidth, Bounds.Height));
        using (ctx.PushClip(new Rect(0, grid.Top, KeyboardWidth, grid.Height)))
        {
            foreach (var (p, y) in VisiblePitches())
            {
                bool black = IsBlack(p);
                ctx.FillRectangle(black ? BlackKey : WhiteKey, new Rect(0, y, black ? KeyboardWidth * 0.62 : KeyboardWidth, rowHeight));
                ctx.DrawLine(RowLine, new Point(0, y + rowHeight - 0.5), new Point(KeyboardWidth, y + rowHeight - 0.5));
                if (p % 12 == 0 && rowHeight >= 8)
                    Paint.DrawText(ctx, PitchName(p), new Point(KeyboardWidth - 26, y + (rowHeight - 11) / 2 - 1), 9, KeyText, 0);
            }
        }

        ctx.DrawLine(Paint.DividerPen, new Point(KeyboardWidth - 0.5, 0), new Point(KeyboardWidth - 0.5, Bounds.Height));
        if (VelocityLaneHeight > 0)
        {
            var lane = VelocityArea;
            Paint.DrawText(ctx, "Velocity", new Point(6, lane.Top + 4), 10, Paint.Dim, KeyboardWidth - 8);
            foreach (int v in (int[])[127, 64, 1])
                Paint.DrawText(ctx, v.ToString(), new Point(KeyboardWidth + 4, Math.Min(lane.Bottom - 13, lane.Bottom - (lane.Height - 8) * v / 127.0 - 6)), 9, Paint.Dim, 0);
        }
    }

    private void DrawNotes(DrawingContext ctx, (long From, long To) ticks)
    {
        var (fromTick, toTick) = ticks;
        if (ShowGhosts)
            foreach (var t in doc!.Session.State.Tracks)
                if (t.Id != doc.SelectedTrack)
                    foreach (var n in t.Notes)
                        if (n.End >= fromTick && n.Start <= toTick)
                            ctx.FillRectangle(Ghost, NoteRect(n));

        if (RecordingPreview is { } recording)
            foreach (var n in recording)
                if (n.End >= fromTick && n.Start <= toTick)
                    ctx.DrawRectangle(RecordFill, NoteEdge, NoteRect(n), 1.5, 1.5);

        if (Track is not { } track) return;
        foreach (var n in track.Notes)
        {
            if (n.End < fromTick || n.Start > toTick) continue;
            var r = NoteRect(n);
            bool selected = selection.Contains(n.Id);
            // A note the Mute tool silenced shows faintly, as a muted hit does in the Hitsound grid.
            using (ctx.PushOpacity(PreviewMuted.Contains(n.Id) ? 0.3 : 1))
                ctx.DrawRectangle(VelocityFill(n.Velocity), selected ? WhiteRing : NoteEdge, r, 1.5, 1.5);
            if (n.Target is not null)
                ctx.FillRectangle(KeySync.Status(n, doc!.TargetIndex) == KeySyncStatus.Synced ? SyncedMark : StaleMark,
                    new Rect(r.Left, r.Top, Math.Min(3, r.Width), r.Height));
            if (r.Width > 30 && rowHeight >= 10)
                Paint.DrawText(ctx, PitchName(n.Pitch), new Point(r.Left + 3, r.Top + (rowHeight - 12) / 2), 9, Brushes.White, r.Width - 5);
        }
    }

    private Rect NoteRect(KeyNote n)
    {
        double x0 = XOfTick(n.Start), x1 = XOfTick(n.End), y = YOfPitch(n.Pitch);
        return new Rect(x0, y + 0.5, Math.Max(2, x1 - x0), Math.Max(2, rowHeight - 1));
    }

    private void DrawRuler(DrawingContext ctx, List<(double Time, int Level)> lines, double width)
    {
        ctx.FillRectangle(Paint.Chrome2, new Rect(KeyboardWidth, 0, width - KeyboardWidth, RulerHeight));
        using (ctx.PushClip(new Rect(KeyboardWidth, 0, width - KeyboardWidth, RulerHeight)))
        {
            double lastLabel = double.NegativeInfinity;
            foreach (var (time, level) in lines)
            {
                if (level == 2) continue;
                double x = Math.Round(XOfTime(time)) + 0.5;
                ctx.DrawLine(level == 0 ? Paint.BarPen : Paint.BeatPen, new Point(x, level == 0 ? 4 : 14), new Point(x, RulerHeight));
                if (x - lastLabel > (level == 0 ? 34 : 40))
                {
                    var (bar, beat) = doc!.Tempo.BarBeatAt(time);
                    Paint.DrawText(ctx, level == 0 ? bar.ToString() : $"{bar}.{beat}", new Point(x + 3, 3), 10, level == 0 ? Paint.Text : Paint.Dim, 0);
                    lastLabel = x;
                }
            }
        }

        DrawLocators(ctx, width);
        DrawBookmarks(ctx, width);
        ctx.DrawLine(Paint.DividerPen, new Point(KeyboardWidth, RulerHeight - 0.5), new Point(width, RulerHeight - 0.5));
    }

    private static readonly IBrush BookmarkFill = Paint.Brush("#4cc3d1");

    /// <summary>
    /// Bookmark flags on the ruler, as in the Hitsound editor: a labelled one shows its label beside its flag, cut short
    /// before the next flag, over the bar numbers.
    /// </summary>
    private void DrawBookmarks(DrawingContext ctx, double width)
    {
        var marks = Bookmarks;
        using var clip = ctx.PushClip(new Rect(KeyboardWidth, 0, width - KeyboardWidth, RulerHeight));
        for (int i = 0; i < marks.Count; i++)
        {
            double x = Math.Round(XOfTime(marks[i].Time)) + 0.5;
            if (x < KeyboardWidth - 8 || x > width + 8) continue;
            var flag = new StreamGeometry();
            using (var g = flag.Open())
            {
                g.BeginFigure(new Point(x, 0), true);
                g.LineTo(new Point(x + 7, 4));
                g.LineTo(new Point(x, 8));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(BookmarkFill, null, flag);
            ctx.FillRectangle(BookmarkFill, new Rect(x, 0, 1, RulerHeight));
            if (marks[i].Label is not { Length: > 0 } label) continue;
            double next = i + 1 < marks.Count ? XOfTime(marks[i + 1].Time) - 4 : width;
            double room = next - (x + 9);
            if (room < 16) continue;
            var text = Paint.Format(label, 10, BookmarkFill);
            ctx.FillRectangle(Paint.Chrome2, new Rect(x + 8, 9, Math.Min(text.Width, room) + 2, RulerHeight - 10));
            Paint.DrawText(ctx, label, new Point(x + 9, 10), 10, BookmarkFill, room);
        }
    }

    /// <summary>Gameplay objects over the song waveform: numbered circular heads, straight slider bodies, unnumbered tails.</summary>
    private void DrawStrip(DrawingContext ctx, List<KeyObject> objects, double width)
    {
        var area = new Rect(KeyboardWidth, RulerHeight, width - KeyboardWidth, StripHeight);
        ctx.FillRectangle(Paint.PanelLow, area);
        using (ctx.PushClip(area))
        {
            double mid = area.Top + area.Height / 2;
            if (WaveGeometry(area, mid) is { } wave) ctx.DrawGeometry(WaveFill, null, wave);

            double radius = Math.Min(9, area.Height / 2 - 4);
            foreach (var o in objects)
            {
                var (body, head) = comboFills[o.Colour % comboFills.Length];
                double x0 = XOfTime(o.Start), x1 = XOfTime(o.End);
                switch (o.Kind)
                {
                    case HitObjectKind.Spinner:
                        ctx.DrawRectangle(SpinnerFill, null, new Rect(x0, mid - 4, Math.Max(2, x1 - x0), 8), 4, 4);
                        ctx.DrawEllipse(SpinnerFill, WhiteRing, new Point(x1, mid), radius * 0.6, radius * 0.6);
                        break;
                    case HitObjectKind.Slider:
                        ctx.DrawRectangle(body, null, new Rect(x0, mid - radius, Math.Max(2, x1 - x0), radius * 2), radius, radius);
                        for (int i = 1; i < o.Nodes.Count; i++)
                        {
                            var (time, position) = o.Nodes[i];
                            double r = position == HitPosition.SliderTail ? radius * 0.8 : radius * 0.55;
                            ctx.DrawEllipse(head, WhiteRing, new Point(XOfTime(time), mid), r, r);
                        }

                        DrawHead(ctx, x0, mid, radius, head, o.Combo);
                        break;
                    default:
                        DrawHead(ctx, x0, mid, radius, head, o.Combo);
                        break;
                }
            }
        }

        ctx.DrawLine(Paint.DividerPen, new Point(KeyboardWidth, area.Bottom - 0.5), new Point(width, area.Bottom - 0.5));
    }

    // The waveform only changes on scroll, zoom or resize: playback redraws reuse the geometry.
    // From the theme (Preferences, Colours, Ruler Colors), as the Hitsound ruler.
    private static IBrush CycleFill => Paint.TokenAt("cycle.active", 0.45);
    private static IBrush RangeFill => Paint.TokenAt("cycle.inactive", 0.2);
    private static IPen LocatorPen => UiTheme.Current.Cached("pen:locator", () => (IPen)new ImmutablePen((IImmutableBrush)Paint.Brush(UiTheme.Current.Color("cycle.active")), 1.5));

    // The L/R locators in the ruler: their range shaded (blue while Cycle is on) and a line at each.
    private void DrawLocators(DrawingContext ctx, double width)
    {
        using var clip = ctx.PushClip(new Rect(KeyboardWidth, 0, width - KeyboardWidth, RulerHeight));
        if (LocatorLeft is { } l && LocatorRight is { } r)
        {
            double x0 = XOfTime(Math.Min(l, r)), x1 = XOfTime(Math.Max(l, r));
            ctx.FillRectangle(Cycle ? CycleFill : RangeFill, new Rect(x0, 0, Math.Max(1, x1 - x0), RulerHeight / 2));
        }

        foreach (var t in new[] { LocatorLeft, LocatorRight })
            if (t is { } at)
            {
                double x = Math.Round(XOfTime(at)) + 0.5;
                ctx.DrawLine(LocatorPen, new Point(x, 0), new Point(x, RulerHeight));
            }
    }

    private StreamGeometry? WaveGeometry(Rect area, double mid)
    {
        if (Wave is not { } wave) return null;
        var key = (Axis.Left - SongOffset, Axis.Zoom, area.Width, wave);
        if (waveGeometry is not null && waveKey == key) return waveGeometry;
        double msPerPx = 1 / Axis.Zoom, half = area.Height / 2 - 2;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
            for (double x = area.Left; x < area.Right; x++)
            {
                double t = TimeOfX(x) - SongOffset; // a later song (positive offset) draws later
                float peak = wave.Max(t, t + msPerPx);
                if (peak <= 0.002f) continue;
                double a = Math.Max(0.5, peak * half);
                g.BeginFigure(new Point(x, mid - a), true);
                g.LineTo(new Point(x + 1, mid - a));
                g.LineTo(new Point(x + 1, mid + a));
                g.LineTo(new Point(x, mid + a));
                g.EndFigure(true);
            }

        (waveGeometry, waveKey) = (geometry, key);
        return geometry;
    }

    private void DrawHead(DrawingContext ctx, double x, double y, double radius, IBrush fill, int combo)
    {
        ctx.DrawEllipse(fill, WhiteRing, new Point(x, y), radius, radius);
        if (!comboLabels.TryGetValue(combo, out var text))
            comboLabels[combo] = text = Paint.Format(combo.ToString(), radius * 1.05, Brushes.White, Paint.UiBold);
        ctx.DrawText(text, new Point(x - text.Width / 2, y - text.Height / 2));
    }

    private void DrawVelocity(DrawingContext ctx, (long From, long To) ticks)
    {
        var area = VelocityArea;
        ctx.DrawLine(Paint.DividerPen, new Point(0, area.Top - 0.5), new Point(Bounds.Width, area.Top - 0.5));
        if (Track is not { } track) return;
        using (ctx.PushClip(area))
            foreach (bool selected in (bool[])[false, true]) // selected bars on top
                foreach (var n in track.Notes)
                {
                    if (n.Start < ticks.From - 1 || n.Start > ticks.To || selection.Contains(n.Id) != selected) continue;
                    double x = XOfTick(n.Start), h = (area.Height - 8) * n.Velocity / 127.0;
                    var brush = VelocityFill(n.Velocity);
                    ctx.FillRectangle(brush, new Rect(x, area.Bottom - h, 3, h));
                    ctx.FillRectangle(selected ? Brushes.White : brush, new Rect(x - 1, area.Bottom - h - 2, 7, 2));
                }
    }

    // Editing.

    private enum DragKind { Move, Copy, Resize, ResizeStart, Draw, Box, Erase, Velocity, NoteVelocity, Pan, VelocityPaint, VelocityRamp, VelocityScale, CycleNew, CycleMove, CycleLeft, CycleRight }

    private (double Left, double Right) cycleAtStart; // the locators when a cycle move or resize began

    // The upper half of the ruler holds the cycle range; the lower half and the strip move the playhead.
    private static bool InCycleRuler(Point p) => p.X >= KeyboardWidth && p.Y < RulerHeight / 2;

    /// <summary>What a press at this x in the upper ruler does: resize an end of the cycle range, move it, or draw a new one.</summary>
    private DragKind CycleDragAt(double x)
    {
        if (LocatorLeft is not { } l || LocatorRight is not { } r) return DragKind.CycleNew;
        double xl = XOfTime(l), xr = XOfTime(r);
        if (Math.Abs(x - xl) <= 5) return DragKind.CycleLeft;
        if (Math.Abs(x - xr) <= 5) return DragKind.CycleRight;
        return x > Math.Min(xl, xr) && x < Math.Max(xl, xr) ? DragKind.CycleMove : DragKind.CycleNew;
    }

    private void ApplyCycleDrag(Point p)
    {
        if (drag is null || doc is null) return;
        drag.Current = p;
        if (!drag.Moved && Math.Abs(p.X - drag.Start.X) < DragThreshold && drag.Kind is DragKind.CycleNew or DragKind.CycleMove) return;
        drag.Moved = true;
        double at = Math.Max(0, SnapTime(Math.Max(0, TimeOfX(p.X))));
        var (l0, r0) = cycleAtStart;
        switch (drag.Kind)
        {
            case DragKind.CycleNew:
                double from = Math.Max(0, SnapTime(Math.Max(0, TimeOfX(drag.Start.X))));
                CycleRangeChanged?.Invoke(Math.Min(from, at), Math.Max(from, at), true);
                break;
            case DragKind.CycleMove:
                double moved = SnapTime(l0 + TimeOfX(p.X) - TimeOfX(drag.Start.X)) - l0;
                moved = Math.Max(moved, -Math.Min(l0, r0));
                CycleRangeChanged?.Invoke(l0 + moved, r0 + moved, false);
                break;
            case DragKind.CycleLeft:
                CycleRangeChanged?.Invoke(at, r0, false);
                break;
            case DragKind.CycleRight:
                CycleRangeChanged?.Invoke(l0, at, false);
                break;
        }
    }

    private sealed class Drag(DragKind kind, Point start)
    {
        public DragKind Kind { get; } = kind;
        public Point Start { get; } = start;
        public Point Current { get; set; } = start;
        public bool Moved { get; set; }
        public Dictionary<Guid, KeyNote> Originals { get; } = [];
        public KeyNote? Anchor { get; set; } // the note under the pointer
        public HashSet<Guid> BoxBase { get; } = [];
        public Dictionary<Guid, Guid> CopyIds { get; } = []; // original → copy, for Alt+drag
        public int PitchShift { get; set; } // the last pitch change of a move, to audition each new pitch once
    }

    private KeyNote? NoteAt(Point p) =>
        Track is { } t && Grid.Contains(p) ? t.Notes.LastOrDefault(n => NoteRect(n).Inflate(new Thickness(0, 0, 1, 0)).Contains(p)) : null;

    private KeyNote? VelocityNoteAt(Point p) =>
        Track?.Notes.Where(n => Math.Abs(XOfTick(n.Start) + 1.5 - p.X) <= 4).OrderByDescending(n => selection.Contains(n.Id)).FirstOrDefault();

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (doc is null) return;
        var p = e.GetPosition(this);
        var point = e.GetCurrentPoint(this).Properties;
        var mods = e.KeyModifiers;

        if (InCycleRuler(p) && !mods.HasFlag(KeyModifiers.Control) && !mods.HasFlag(KeyModifiers.Alt) && point.IsLeftButtonPressed)
        {
            drag = new Drag(CycleDragAt(p.X), p);
            if (LocatorLeft is { } l0 && LocatorRight is { } r0) cycleAtStart = (l0, r0);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (p.Y < RulerHeight + StripHeight && p.X >= KeyboardWidth)
        {
            double at = Math.Max(0, TimeOfX(p.X));
            if (mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Alt))
            {
                at = Math.Max(0, SnapTime(at));
                LocatorRequested?.Invoke(mods.HasFlag(KeyModifiers.Control), at);
                e.Handled = true;
                return;
            }

            if (p.Y < RulerHeight && doc.Session.NearestBookmark(at, 7 / Axis.Zoom) is { } bookmark) at = bookmark.Time;
            SeekRequested?.Invoke(at);
            e.Handled = true;
            return;
        }

        if (p.X < KeyboardWidth && Grid.Contains(new Point(KeyboardWidth, p.Y)))
        {
            int key = PitchOfY(p.Y);
            AuditionRequested?.Invoke(key, InsertVelocity); // the keyboard plays its key
            if (mods.HasFlag(KeyModifiers.Control) && Track is { } keyTrack) // Ctrl+click selects every note of that pitch
            {
                if (!mods.HasFlag(KeyModifiers.Shift)) selection.Clear();
                selection.UnionWith(keyTrack.Notes.Where(n => n.Pitch == key).Select(n => n.Id));
                InvalidateVisual();
                ViewChanged?.Invoke();
            }

            e.Handled = true;
            return;
        }

        if (Track is not { } track || p.X < KeyboardWidth) return;
        var session = doc.Session;

        if (VelocityArea.Contains(p))
        {
            if (mods.HasFlag(KeyModifiers.Alt) || mods.HasFlag(KeyModifiers.Shift) || Tool == KeyTool.Ramp)
            {
                drag = new Drag(mods.HasFlag(KeyModifiers.Alt) ? DragKind.VelocityPaint : DragKind.VelocityRamp, p);
                lastPaintX = null;
                session.BeginGesture("Velocity");
                ApplyDrag(p, mods);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (VelocityNoteAt(p) is not { } vn) return;
            if (!selection.Contains(vn.Id)) { selection.Clear(); selection.Add(vn.Id); }
            // Ctrl+drag compresses or expands the selected velocities around their average, as in Cubase.
            StartGesture(mods.HasFlag(KeyModifiers.Control) ? DragKind.VelocityScale : DragKind.Velocity, p, vn, track, "Velocity");
            ApplyDrag(p, mods);
            e.Pointer.Capture(this);
            e.Handled = true;
            ViewChanged?.Invoke();
            return;
        }

        if (!Grid.Contains(p)) return;
        if (Tool == KeyTool.Grab && !point.IsRightButtonPressed)
        {
            drag = new Drag(DragKind.Pan, p);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        var hit = NoteAt(p);

        // Right-click opens the tool picker at the pointer, as in the Hitsound grid and Cubase, and edits nothing (it used to
        // delete the note; the user ruled that out, 2026-11-12).
        if (point.IsRightButtonPressed)
        {
            e.Handled = true;
            ToolPickerRequested?.Invoke();
            return;
        }

        // Holding Alt draws with any tool, as in the Hitsound editor; Ctrl+Alt+drag copies notes instead.
        bool copyDrag = mods.HasFlag(KeyModifiers.Alt) && mods.HasFlag(KeyModifiers.Control);
        var tool = mods.HasFlag(KeyModifiers.Alt) && !copyDrag ? KeyTool.Draw : Tool;
        switch (tool)
        {
            case KeyTool.Mute:
                // Preview only, as in the Hitsound editor: silenced while listening, never saved, and Generate still renders it.
                if (hit is not null)
                {
                    if (!PreviewMuted.Remove(hit.Id)) PreviewMuted.Add(hit.Id);
                    MuteChanged?.Invoke();
                }

                break;

            case KeyTool.Split when hit is not null:
            {
                // At the snapped click; a click on a selected note splits every selected note there.
                long at = doc.Tempo.RoundTick(SnapTime(TimeOfX(p.X)));
                var targets = selection.Contains(hit.Id) ? track.Notes.Where(n => selection.Contains(n.Id)) : [hit];
                var (changed, added) = KeyEditing.Split(targets, at);
                session.ReplaceNotes(track.Id, new HashSet<Guid>(), changed, added, "Split notes");
                break;
            }

            case KeyTool.Glue when hit is not null:
                if (KeyEditing.Glue(track.Notes, hit) is var (joined, removed))
                {
                    session.ReplaceNotes(track.Id, new HashSet<Guid> { removed.Id }, [joined], [], "Glue notes");
                    selection.Remove(removed.Id);
                }

                break;

            case KeyTool.Split or KeyTool.Glue:
                break;

            case KeyTool.Erase:
                drag = new Drag(DragKind.Erase, p);
                session.BeginGesture("Erase notes");
                EraseAt(p);
                break;

            case KeyTool.Draw when hit is not null:
                RemoveNote(track, hit, "Delete note"); // Cubase's Draw tool deletes a note it clicks
                break;

            case KeyTool.Draw:
            {
                long startTick = doc.Tempo.RoundTick(FloorSnap(TimeOfX(p.X)));
                var note = new KeyNote(Guid.NewGuid(), startTick, StepTicks, PitchOfY(p.Y), InsertVelocity);
                selection.Clear();
                selection.Add(note.Id);
                StartGesture(DragKind.Draw, p, note, track, "Draw note");
                session.AddNotes(track.Id, [note]);
                AuditionRequested?.Invoke(note.Pitch, note.Velocity);
                break;
            }

            case KeyTool.Select when hit is not null && e.ClickCount == 2 && mods == KeyModifiers.None:
                // As in the Hitsound editor: double-clicking a note selects it, moves the playhead there and plays it.
                SelectOnly([hit.Id]);
                SeekRequested?.Invoke(doc!.Tempo.TimeAt(hit.Start));
                AuditionRequested?.Invoke(hit.Pitch, hit.Velocity);
                break;

            case KeyTool.Select or KeyTool.Ramp when hit is null && e.ClickCount == 2 && mods == KeyModifiers.None:
            {
                // Double-click on empty space inserts a note there, as Draw would (Cubase's Key Editor does the same with
                // its Select tool): snapped, one snap step long, at the insert velocity.
                long startTick = doc.Tempo.RoundTick(FloorSnap(TimeOfX(p.X)));
                var note = new KeyNote(Guid.NewGuid(), startTick, StepTicks, PitchOfY(p.Y), InsertVelocity);
                SelectOnly([note.Id]);
                session.AddNotes(track.Id, [note], "Draw note");
                AuditionRequested?.Invoke(note.Pitch, note.Velocity);
                break;
            }

            case KeyTool.Select when hit is not null && e.ClickCount == 2 && mods.HasFlag(KeyModifiers.Shift):
                // Cubase: Shift+double-click selects this note and every later one of its pitch.
                selection.UnionWith(KeyEditing.FollowingOfPitch(track.Notes, hit).Select(n => n.Id));
                break;

            case KeyTool.Select or KeyTool.Ramp when hit is not null:
            {
                // Ctrl+Alt is the copy drag: its Ctrl doesn't toggle the note's selection.
                bool ctrl = mods.HasFlag(KeyModifiers.Control) && !copyDrag, shift = mods.HasFlag(KeyModifiers.Shift), velocity = ctrl && shift;
                if (ctrl && !shift) { if (!selection.Remove(hit.Id)) selection.Add(hit.Id); }
                else if (shift && !ctrl) selection.Add(hit.Id);
                else if (!selection.Contains(hit.Id)) SelectOnly([hit.Id]);

                if (!selection.Contains(hit.Id)) break;
                AuditionRequested?.Invoke(hit.Pitch, hit.Velocity);
                var (kind, label) = velocity ? (DragKind.NoteVelocity, "Velocity")
                    : OnRightEdge(hit, p) ? (DragKind.Resize, "Resize notes")
                    : OnLeftEdge(hit, p) ? (DragKind.ResizeStart, "Resize notes")
                    : copyDrag ? (DragKind.Copy, "Copy notes") // Ctrl+Alt+drag copies (Alt alone draws, as in the Hitsound editor)
                    : (DragKind.Move, "Move notes");
                StartGesture(kind, p, hit, track, label);
                if (kind == DragKind.Copy)
                {
                    foreach (var id in drag!.Originals.Keys) drag.CopyIds[id] = Guid.NewGuid();
                    SelectOnly(drag.CopyIds.Values); // the copies are what's selected afterwards
                }

                break;
            }

            default:
                drag = new Drag(DragKind.Box, p);
                if (mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Shift)) drag.BoxBase.UnionWith(selection);
                else selection.Clear();
                break;
        }

        if (drag is not null) e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    /// <summary>A drag that edits notes: the selected ones (or the one being drawn) as they were, then one undo step.</summary>
    private void StartGesture(DragKind kind, Point p, KeyNote anchor, KeyTrack track, string label)
    {
        drag = new Drag(kind, p) { Anchor = anchor };
        foreach (var n in SelectedNotes()) drag.Originals[n.Id] = n;
        drag.Originals[anchor.Id] = anchor;
        doc!.Session.BeginGesture(label);
    }

    private void RemoveNote(KeyTrack track, KeyNote note, string label)
    {
        doc!.Session.RemoveNotes(track.Id, new HashSet<Guid> { note.Id }, label);
        selection.Remove(note.Id);
    }

    // The resize handles: the first and last few pixels, at most a quarter of a short note so it can still be moved.
    private static double GrabWidth(Rect r) => Math.Min(EdgeGrab, r.Width / 4);
    private bool OnRightEdge(KeyNote n, Point p) => NoteRect(n) is var r && p.X >= r.Right - GrabWidth(r);
    private bool OnLeftEdge(KeyNote n, Point p) => NoteRect(n) is var r && p.X <= r.Left + GrabWidth(r);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (drag is null)
        {
            Cursor = InCycleRuler(p) ? CycleDragAt(p.X) switch { DragKind.CycleLeft or DragKind.CycleRight => ResizeCursor, DragKind.CycleMove => HandCursor, _ => Cursor.Default }
                : Tool == KeyTool.Select && NoteAt(p) is { } n && (OnRightEdge(n, p) || OnLeftEdge(n, p)) ? ResizeCursor : Cursor.Default;
            return;
        }

        if (drag.Kind is DragKind.CycleNew or DragKind.CycleMove or DragKind.CycleLeft or DragKind.CycleRight)
        {
            ApplyCycleDrag(p);
            return;
        }

        if (drag.Kind == DragKind.Pan)
        {
            // The view follows the pointer: right shows earlier time, down shows higher keys.
            Axis.Left -= (p.X - drag.Current.X) / Axis.Zoom;
            ScrollPitch((p.Y - drag.Current.Y) / rowHeight);
            drag.Current = p;
            ViewChanged?.Invoke();
            return;
        }

        ApplyDrag(p, e.KeyModifiers);
    }

    private double? lastPaintX; // where the last Alt+drag step painted

    /// <summary>The shape of velocity ramps (the controller's ramp shape box).</summary>
    public RampShape RampShape { get; set; } = RampShape.Linear;

    private int VelocityAtY(double y)
    {
        var area = VelocityArea;
        return Math.Clamp((int)Math.Round((area.Bottom - y) / (area.Height - 8) * 127), KeyNote.MinVelocity, KeyNote.MaxVelocity);
    }

    // Notes of the track whose velocity bar (at their start) lies between two x positions.
    private List<KeyNote> NotesBetween(KeyTrack track, double x0, double x1)
    {
        double from = Math.Min(x0, x1) - 4, to = Math.Max(x0, x1) + 4;
        return [.. track.Notes.Where(n => XOfTick(n.Start) + 1.5 is var x && x >= from && x <= to)];
    }

    private void ApplyDrag(Point p, KeyModifiers mods)
    {
        if (drag is null || doc is null || Track is not { } track) return;
        drag.Current = p;
        if (!drag.Moved && Math.Abs(p.X - drag.Start.X) + Math.Abs(p.Y - drag.Start.Y) < DragThreshold && drag.Kind is DragKind.Move or DragKind.Copy or DragKind.Resize or DragKind.ResizeStart) return;
        drag.Moved = true;
        var session = doc.Session;
        var tempo = doc.Tempo;
        switch (drag.Kind)
        {
            case DragKind.Box:
            {
                var r = new Rect(drag.Start, p).Normalize();
                selection.Clear();
                selection.UnionWith(drag.BoxBase);
                selection.UnionWith(track.Notes.Where(n => NoteRect(n).Intersects(r)).Select(n => n.Id));
                InvalidateVisual();
                ViewChanged?.Invoke();
                break;
            }

            case DragKind.Erase:
                EraseAt(p);
                break;

            case DragKind.Move or DragKind.Copy when drag.Anchor is { } anchor:
            {
                // The anchor note's start follows the pointer (snapped); the others keep their distance to it.
                double anchorTime = tempo.TimeAt(anchor.Start);
                double target = SnapMoved(anchorTime, TimeOfX(p.X) - TimeOfX(drag.Start.X));
                long dt = tempo.RoundTick(target) - anchor.Start;
                int dp = (int)Math.Round((drag.Start.Y - p.Y) / rowHeight);
                if (mods.HasFlag(KeyModifiers.Control)) // Ctrl keeps the move to one direction, whichever is larger
                {
                    if (Math.Abs(p.X - drag.Start.X) > Math.Abs(p.Y - drag.Start.Y)) dp = 0; else dt = 0;
                }

                int lowest = drag.Originals.Values.Min(n => n.Pitch), highest = drag.Originals.Values.Max(n => n.Pitch);
                dp = Math.Clamp(dp, KeyNote.MinPitch - lowest, KeyNote.MaxPitch - highest);
                if (dp != drag.PitchShift)
                {
                    drag.PitchShift = dp;
                    AuditionRequested?.Invoke(anchor.Pitch + dp, anchor.Velocity);
                }

                var moved = drag.Originals.Values.Select(n => n with { Start = n.Start + dt, Pitch = n.Pitch + dp });
                if (drag.Kind == DragKind.Copy)
                {
                    session.ResetToGestureStart(); // the copies are added again at each new place
                    session.AddNotes(track.Id, [.. moved.Select(n => n with { Id = drag.CopyIds[n.Id] })], "Copy notes");
                }
                else session.UpdateNotes(track.Id, [.. moved], "Move notes");
                break;
            }

            case DragKind.ResizeStart when drag.Anchor is { } anchor:
            {
                // The start follows the pointer (snapped); the end stays.
                double start = SnapTime(tempo.TimeAt(anchor.Start) + (TimeOfX(p.X) - TimeOfX(drag.Start.X)));
                long ds = tempo.RoundTick(start) - anchor.Start;
                session.UpdateNotes(track.Id, KeyEditing.ResizeStart(drag.Originals.Values, ds, MinLength), "Resize notes");
                break;
            }

            case DragKind.NoteVelocity:
            {
                // Ctrl+Shift+drag on a note: up is louder, for every selected note.
                int delta = (int)Math.Round((drag.Start.Y - p.Y) / 2);
                session.UpdateNotes(track.Id, [.. drag.Originals.Values.Select(n => n with { Velocity = Math.Clamp(n.Velocity + delta, KeyNote.MinVelocity, KeyNote.MaxVelocity) })], "Velocity");
                break;
            }

            case DragKind.Resize when drag.Anchor is { } anchor:
            {
                double end = SnapTime(tempo.TimeAt(anchor.End) + (TimeOfX(p.X) - TimeOfX(drag.Start.X)));
                long dl = tempo.RoundTick(end) - anchor.End;
                session.UpdateNotes(track.Id, [.. drag.Originals.Values.Select(n => n with { Length = Math.Max(MinLength, n.Length + dl) })], "Resize notes");
                break;
            }

            case DragKind.Draw when drag.Anchor is { } note:
            {
                // Dragging right while drawing sets the length, at least one grid step.
                double end = SnapTime(TimeOfX(p.X));
                long length = Math.Max(MinLength, tempo.RoundTick(end) - note.Start);
                session.UpdateNotes(track.Id, [note with { Length = length }], "Draw note");
                break;
            }

            case DragKind.VelocityPaint:
            {
                // Alt+drag: every bar the pointer passes takes the pointer's height.
                int value = VelocityAtY(p.Y);
                session.UpdateNotes(track.Id, [.. NotesBetween(track, lastPaintX ?? p.X, p.X).Select(n => n with { Velocity = value })], "Velocity");
                lastPaintX = p.X; // the next move paints everything in between too
                break;
            }

            case DragKind.VelocityRamp:
            {
                // Shift+drag: a straight ramp from where the drag started to the pointer, recomputed from the start.
                session.ResetToGestureStart();
                if (session.State.Track(track.Id) is not { } fresh) break;
                double x0 = drag.Start.X, x1 = p.X;
                int v0 = VelocityAtY(drag.Start.Y), v1 = VelocityAtY(p.Y);
                session.UpdateNotes(track.Id, [.. NotesBetween(fresh, x0, x1).Select(n =>
                {
                    double x = XOfTick(n.Start), f = Math.Abs(x1 - x0) < 1 ? 1 : Math.Clamp((x - x0) / (x1 - x0), 0, 1);
                    return n with { Velocity = Math.Clamp(VolumeRamp.At(RampShape, f, v0, v1), KeyNote.MinVelocity, KeyNote.MaxVelocity) };
                })], "Velocity");
                break;
            }

            case DragKind.VelocityScale when drag.Originals.Count > 0:
            {
                double factor = Math.Clamp(1 + (drag.Start.Y - p.Y) / 60, 0, 4);
                double mean = drag.Originals.Values.Average(n => n.Velocity);
                session.UpdateNotes(track.Id, [.. drag.Originals.Values.Select(n => n with
                {
                    Velocity = Math.Clamp((int)Math.Round(mean + (n.Velocity - mean) * factor), KeyNote.MinVelocity, KeyNote.MaxVelocity),
                })], "Velocity");
                break;
            }

            case DragKind.Velocity when drag.Anchor is { } anchor:
            {
                var area = VelocityArea;
                int value = Math.Clamp((int)Math.Round((area.Bottom - p.Y) / (area.Height - 8) * 127), KeyNote.MinVelocity, KeyNote.MaxVelocity);
                int delta = value - anchor.Velocity;
                session.UpdateNotes(track.Id, [.. drag.Originals.Values.Select(n => n with { Velocity = Math.Clamp(n.Velocity + delta, KeyNote.MinVelocity, KeyNote.MaxVelocity) })], "Velocity");
                break;
            }
        }
    }

    private long MinLength => SnapOn ? StepTicks : 1;

    private void EraseAt(Point p)
    {
        if (Track is { } track && NoteAt(p) is { } n) RemoveNote(track, n, "Erase notes");
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        FinishDrag();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        FinishDrag();
    }

    private void FinishDrag()
    {
        if (drag is null) return;
        var kind = drag.Kind;
        if (kind is DragKind.CycleNew or DragKind.CycleMove && !drag.Moved && LocatorLeft is { } l && LocatorRight is { } r
            && TimeOfX(drag.Start.X) is var t && t >= Math.Min(l, r) && t <= Math.Max(l, r))
            CycleToggleRequested?.Invoke(); // a click (no drag) inside the range, as in Cubase
        if (kind == DragKind.Copy && !drag.Moved)
        {
            // An Alt+click without a drag copies nothing: the originals stay selected.
            selection.Clear();
            selection.UnionWith(drag.Originals.Keys);
        }

        drag = null;
        if (kind != DragKind.Box) doc?.Session.EndGesture();
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    /// <summary>Esc during a drag puts everything back.</summary>
    public bool CancelDrag()
    {
        if (drag is null) return false;
        if (drag.Kind != DragKind.Box) doc?.Session.CancelGesture();
        drag = null;
        InvalidateVisual();
        return true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (doc is null) return;
        var p = e.GetPosition(this);
        double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        var (action, steps) = WheelMode.For(WheelMode.Osu, e.KeyModifiers, e.Delta.X, e.Delta.Y, overHeaders: p.X < KeyboardWidth);
        switch (action)
        {
            case WheelAction.ZoomRows: ZoomRows(delta > 0 ? 1.15 : 1 / 1.15, p.Y); break;
            case WheelAction.ZoomTime: Axis.ZoomAt(delta > 0 ? 1.25 : 0.8, Math.Max(0, p.X - KeyboardWidth)); break;
            case WheelAction.Divisor: DivisorWheel?.Invoke(delta > 0 ? 1 : -1); break; // finer up, coarser down
            case WheelAction.Pan: Axis.Left -= delta * 120 / Axis.Zoom; break;
            case WheelAction.Step: PlayheadStepRequested?.Invoke(steps); break;
            default: ScrollPitch(delta * 3); break;
        }
        e.Handled = true;
    }

    /// <summary>Zooms time around the middle of the view.</summary>
    public void ZoomTime(double factor) => Axis.ZoomAt(factor, Axis.Width / 2);

    public void ZoomRows(double factor, double? atY = null)
    {
        double y = atY ?? Grid.Top + Grid.Height / 2;
        double pitchAt = topPitch - (y - Grid.Top) / rowHeight;
        rowHeight = Math.Clamp(rowHeight * factor, MinRow, MaxRow);
        topPitch = pitchAt + (y - Grid.Top) / rowHeight;
        ScrollPitch(0);
    }

    public void ScrollPitch(double semitones)
    {
        double visible = Grid.Height / rowHeight;
        topPitch = Math.Clamp(topPitch + semitones, Math.Min(KeyNote.MaxPitch + 1, visible - 1), KeyNote.MaxPitch + 1);
        InvalidateVisual();
        ScrollChanged?.Invoke();
    }

    /// <summary>Shows the selected track's notes (or middle C) vertically.</summary>
    public void CentrePitch()
    {
        double centre = Track is { Notes.Count: > 0 } t ? t.Notes.Average(n => n.Pitch) : 60;
        topPitch = centre + Grid.Height / rowHeight / 2;
        ScrollPitch(0);
    }

    // Keyboard commands on the selection (the editor routes keys here).

    public void SelectAll()
    {
        if (Track is not { } t) return;
        selection.UnionWith(t.Notes.Select(n => n.Id));
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    public void Deselect()
    {
        selection.Clear();
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    public void DeleteSelection()
    {
        if (Track is not { } t || selection.Count == 0) return;
        doc!.Session.RemoveNotes(t.Id, selection.ToHashSet());
        selection.Clear();
        ViewChanged?.Invoke();
    }

    public void Transpose(int semitones)
    {
        if (Track is not { } t || selection.Count == 0) return;
        var notes = SelectedNotes();
        semitones = Math.Clamp(semitones, KeyNote.MinPitch - notes.Min(n => n.Pitch), KeyNote.MaxPitch - notes.Max(n => n.Pitch));
        if (semitones == 0) return;
        doc!.Session.UpdateNotes(t.Id, [.. notes.Select(n => n with { Pitch = n.Pitch + semitones })], "Transpose");
    }

    private void SelectOnly(IEnumerable<Guid> ids)
    {
        selection.Clear();
        selection.UnionWith(ids);
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    private List<KeyNote> SelectedNotes() => Track is { } t ? [.. t.Notes.Where(n => selection.Contains(n.Id))] : [];

    /// <summary>Left/Right: the previous or next note in time; with <paramref name="extend"/> (Shift) it is added.</summary>
    public void SelectNeighbour(int direction, bool extend)
    {
        if (Track is not { } t) return;
        var ordered = KeyEditing.InTimeOrder(SelectedNotes()).ToList();
        var current = direction > 0 ? ordered.LastOrDefault() : ordered.FirstOrDefault();
        if (KeyEditing.Neighbour(t.Notes, current, direction) is not { } next) return;
        if (!extend) selection.Clear();
        selection.Add(next.Id);
        AuditionRequested?.Invoke(next.Pitch, next.Velocity);
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    /// <summary>The selected notes of the selected track.</summary>
    public IReadOnlyList<KeyNote> Selected => SelectedNotes();

    /// <summary>The info bar's velocity edit: a new velocity for every selected note, as one undo step.</summary>
    public void SetSelectionVelocity(Func<int, int> velocity)
    {
        if (Track is not { } track) return;
        var notes = SelectedNotes();
        if (notes.Count == 0) return;
        doc!.Session.UpdateNotes(track.Id, [.. notes.Select(n => n with { Velocity = Math.Clamp(velocity(n.Velocity), KeyNote.MinVelocity, KeyNote.MaxVelocity) })], "Velocity");
    }

    /// <summary>Ctrl+D: copies of the selection right after it; the copies become the selection.</summary>
    public void Duplicate()
    {
        if (Track is not { } t || SelectedNotes() is not { Count: > 0 } notes) return;
        var copies = KeyEditing.Duplicate(notes);
        doc!.Session.AddNotes(t.Id, copies, "Duplicate");
        SelectOnly(copies.Select(n => n.Id));
    }

    /// <summary>The selected notes, for the clipboard.</summary>
    public IReadOnlyList<KeyNote> CopySelection() => SelectedNotes();

    /// <summary>Pastes notes with the earliest at <paramref name="time"/>; the pasted notes become the selection.</summary>
    public void Paste(IReadOnlyCollection<KeyNote> notes, double time)
    {
        if (Track is not { } t || notes.Count == 0) return;
        var pasted = KeyEditing.PasteAt(notes, doc!.Tempo.RoundTick(time));
        doc.Session.AddNotes(t.Id, pasted, "Paste");
        SelectOnly(pasted.Select(n => n.Id));
    }

    /// <summary>Q: moves the selected notes' starts onto the grid (always, even with Snap off).</summary>
    /// <summary>Quantize, as the Hitsound editor's: the selected notes move <paramref name="strength"/> % of the way to the grid.</summary>
    public void QuantizeSelection(double strength = 100)
    {
        if (Track is not { } t || SelectedNotes() is not { Count: > 0 } notes) return;
        var tempo = doc!.Tempo;
        double f = Math.Clamp(strength, 1, 100) / 100;
        doc.Session.UpdateNotes(t.Id, KeyEditing.Quantize(notes, tick =>
        {
            double at = tempo.TimeAt(tick);
            return tempo.RoundTick(at + (SnapTime(at) - at) * f);
        }), "Quantize");
    }

    /// <summary>Repeat's gap: the selection's length in snap steps, at least one.</summary>
    public int DefaultRepeatGap()
    {
        var notes = SelectedNotes();
        if (notes.Count == 0) return 1;
        long span = notes.Max(n => n.End) - notes.Min(n => n.Start);
        return Math.Max(1, (int)Math.Ceiling((double)span / StepTicks - 0.01));
    }

    /// <summary>Ctrl+R, as in the Hitsound editor: <paramref name="copies"/> copies of the selection, each <paramref name="gap"/> snap steps after the last, as one step.</summary>
    public void Repeat(int copies, int gap)
    {
        if (Track is not { } t || SelectedNotes() is not { Count: > 0 } notes || copies is < 1 or > HitEditor.MaxRepeatCopies || gap is < 1 or > HitEditor.MaxRepeatGap) return;
        long first = notes.Min(n => n.Start), step = gap * StepTicks;
        var added = new List<KeyNote>();
        for (int c = 1; c <= copies; c++) added.AddRange(KeyEditing.PasteAt(notes, first + c * step));
        doc!.Session.AddNotes(t.Id, added, "Repeat");
        SelectOnly(added.Select(n => n.Id));
    }

    /// <summary>Ctrl+Alt+Left/Right, as in the Hitsound editor: the selection moves so its first note lands on the previous or next grid line.</summary>
    public void NudgeToGrid(int direction)
    {
        if (Track is not { } t || SelectedNotes() is not { Count: > 0 } notes) return;
        var tempo = doc!.Tempo;
        long start = notes.Min(n => n.Start);
        long dt = tempo.RoundTick(GridStep(tempo.TimeAt(start), direction)) - start;
        if (dt != 0) doc.Session.UpdateNotes(t.Id, [.. notes.Select(n => n with { Start = Math.Max(0, n.Start + dt) })], "Move notes");
    }

    /// <summary>Selects the selected track's notes that <paramref name="match"/> (the controller's velocity range).</summary>
    public int SelectWhere(Func<KeyNote, bool> match)
    {
        if (Track is not { } t) return 0;
        var ids = t.Notes.Where(match).Select(n => n.Id).ToList();
        SelectOnly(ids);
        return ids.Count;
    }

    /// <summary>The info bar's Start: the selected notes move so the first starts at this tick (the others keep their spacing).</summary>
    public void MoveSelectionTo(long tick)
    {
        if (Track is not { } t || selection.Count == 0) return;
        var notes = SelectedNotes();
        long dt = Math.Max(0, tick) - notes.Min(n => n.Start);
        if (dt != 0) doc!.Session.UpdateNotes(t.Id, [.. notes.Select(n => n with { Start = n.Start + dt })], "Move notes");
    }

    /// <summary>Cubase's Nudge Start/End: moves the selected notes' starts and/or ends by snap steps; the end stays put when the start moves, and a note keeps at least one step.</summary>
    public void NudgeEdges(int startSteps, int endSteps)
    {
        if (Track is not { } t || selection.Count == 0) return;
        long step = StepTicks, ds = startSteps * step, de = endSteps * step;
        var moved = SelectedNotes().Select(n =>
        {
            long start = Math.Max(0, n.Start + ds), end = n.End + de;
            if (end - start < MinLength) { if (ds != 0) start = end - MinLength; else end = start + MinLength; }
            return n with { Start = start, Length = end - start };
        }).ToList();
        doc!.Session.UpdateNotes(t.Id, moved, ds != 0 ? "Nudge start" : "Nudge end");
    }

    public void Nudge(int steps)
    {
        if (Track is not { } t || selection.Count == 0) return;
        long dt = steps * StepTicks;
        doc!.Session.UpdateNotes(t.Id, [.. SelectedNotes().Select(n => n with { Start = n.Start + dt })], "Move notes");
    }

    public string SelectionText()
    {
        if (Track is not { } t || selection.Count == 0) return "No notes selected";
        var notes = SelectedNotes();
        if (notes.Count != 1) return $"{notes.Count} notes selected";
        var n = notes[0];
        var (bar, beat) = doc!.Tempo.BarBeatAt(doc.Tempo.TimeAt(n.Start));
        return $"{PitchName(n.Pitch)} · bar {bar}.{beat} · {n.Length / (double)TempoMap.Ppq:0.###} beats · velocity {n.Velocity}";
    }
}

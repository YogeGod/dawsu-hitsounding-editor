using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>The editing tools (dawsu numbering: 1 Select, 2 Range, 3 Draw, 4 Erase, 5 Mute, 6 Ramp in the volume lane).</summary>
internal enum Tool
{
    Select,
    Grab,
    Range,
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
    public bool ShowMissingObjectWarnings { get; set; }
    public bool ShowExportWarnings { get; set; } = true;

    /// <summary>Lines down the lanes at every gameplay object's circles, slider nodes and spinner ends, whatever the snap.</summary>
    public bool ShowObjectLines { get; set; } = true;
    public IReadOnlyList<Conflict> ExportProblems { get; set; } = [];

    internal IEnumerable<(double Time, string Reason)> VisibleWarnings()
    {
        if (ShowExportWarnings)
            foreach (var problem in ExportProblems) yield return (problem.Time, problem.Reason);
        if (ShowMissingObjectWarnings && Model is { } model)
            foreach (var hit in model.Session.Unanchored.DistinctBy(h => h.Time)) yield return (hit.Time, "No hit object here. Use HS mode or Generate to create one.");
    }

    private bool VisibleProblem(EditHit hit) => hit.Anchor is null ? ShowMissingObjectWarnings : ShowExportWarnings && Model?.Conflicted.Contains(hit.Anchor.Value) == true;
    private static UiTheme Theme_ => UiTheme.Current;

    /// <summary>The column of drag handles (⠿) left of the lane names; the lane list's own width is themable.</summary>
    public const double NumberWidth = 24;
    public static double ListWidth => NamesCollapsed ? Math.Min(CollapsedListWidth, ShownListWidth) : ShownListWidth;

    /// <summary>
    /// The lane list's width as the user dragged it (kept in the preferences, not the theme); null uses the theme's
    /// width, which stays the default for everyone.
    /// </summary>
    public static double? UserListWidth { get; set; }

    private static double ShownListWidth => UserListWidth ?? Theme_.Number("list.width");

    /// <summary>The lane list's ‹: only the sound icons and buttons are shown, the names hidden.</summary>
    public static bool NamesCollapsed { get; set; }

    private const double CollapsedListWidth = 150;
    public static double GridLeft => NumberWidth + ListWidth;
    public static double SectionsHeight => Theme_.Number("header.sections");
    public const double RulerHeight = 24;
    public double HeaderHeight => RulerBottom + StripHeight;

    /// <summary>The song strip: the waveform with the gameplay objects drawn over it (at least tall enough for the objects).</summary>
    public double StripHeight => Math.Max(WaveHeight, ObjectsHeight);

    /// <summary>Bottom of the ruler: the waveform lane and the object strip sit between it and the lanes.</summary>
    public static double RulerBottom => SectionsHeight + RulerHeight;

    /// <summary>Height of the waveform lane; drag the line under it to resize, double-click to reset.</summary>
    public double WaveHeight { get; set; } = DefaultWaveHeight;

    public const double DefaultWaveHeight = 56, ObjectsHeight = 26;
    public const double MinRowHeight = 16, MaxRowHeight = 64;

    private static readonly IPen ConflictRing = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#ff453a"), 2);
    private static readonly IPen MarqueePen = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#2f6fd0"), 1);
    private static readonly IBrush MarqueeFill = Paint.Brush(Color.Parse("#2f6fd0"), 0.15);
    private static readonly IPen RangePen = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#e0a030"), 1);
    private static readonly IBrush RangeFill = Paint.Brush(Color.Parse("#e0a030"), 0.18);
    private static readonly IBrush HoverFill = Paint.Brush(Colors.White, 0.06);
    private static readonly IPen LaneDropPen = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#0a84ff"), 2);

    private double? rowHeight; // set by Ctrl+Shift+wheel; null follows the theme
    private double scrollY;
    private HashSet<EditHit> selection => Edits.Selected;
    private readonly HashSet<Guid> linkedSelection = [];
    private readonly DrawLayer playheadLayer;
    private int modelGeneration;
    private List<GridLine> gridLines = [];
    private (double Left, double Zoom, double GridWidth, double GridLeft, int Generation) gridKey;
    private bool gridReady;

    /// <summary>How many times the note scene was invalidated. A playhead tick does not increment this.</summary>
    internal int SceneInvalidations { get; private set; }

    // The gesture in progress.
    private enum Drag
    {
        None,
        Marquee,
        Move,
        Range,
        Paint,
        Wipe,
        Cycle,
        CycleMove,
        CycleLeft,
        CycleRight,
        ResizeWave,
        ResizeList,
        Layout,
        Pan,
    }

    private Drag drag;
    private Point dragStart, dragNow;
    private EditHit? dragAnchor;
    private List<EditHit> dragHits = [];
    private LaneRow? paintRow;
    private double waveHeightAtStart;
    /// <summary>While the lane list is dragged wider or narrower (the views aligned to it follow) and when it's done.</summary>
    public event Action? LaneListResizing, LaneListResized;

    // The lane list's right edge: 4 px on the list side, under the header (where the list's own buttons sit).
    private bool OnListEdge(Point p) => p.Y >= HeaderHeight && p.X >= GridLeft - 4 && p.X <= GridLeft;
    private double panLeft, panScroll;
    private (double Left, double Right) cycleAtStart; // the locators when a move or resize of the cycle range began
    public event Action<Lane, string>? SampleDropped;
    private Lane? wipeLane; // Draw strokes erase only in their own lane; null (Erase) erases in any lane
    private HashSet<EditHit> marqueeBase = [];
    private bool seekOnClick;
    private Row? draggedRow, dropRow;
    private bool layoutMoved, dropAfter;
    private double? laneDropY;

#pragma warning disable CS0618
    private Lane? SampleDropLane(DragEventArgs e) => e.Data.Get(FileBrowserPanel.SampleDragFormat) is string path
        && HitsoundRules.SampleExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant())
        && RowAt(e.GetPosition(this).Y) is LaneRow row && Model?.CanReplaceSample(row.Lane) == true ? row.Lane : null;

    private void SampleDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(FileBrowserPanel.SampleDragFormat)) return;
        e.DragEffects = SampleDropLane(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void SampleDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(FileBrowserPanel.SampleDragFormat)) return;
        if (SampleDropLane(e) is { } lane && e.Data.Get(FileBrowserPanel.SampleDragFormat) is string path) SampleDropped?.Invoke(lane, path);
        e.Handled = true;
    }
#pragma warning restore CS0618

    public ArrangeView()
    {
        playheadLayer = new DrawLayer(DrawPlayhead);
        VisualChildren.Add(playheadLayer);
        ClipToBounds = true;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, SampleDragOver);
        AddHandler(DragDrop.DropEvent, SampleDrop);
        Focusable = true;
        Edits.LaneMovePreviewRequested += PreviewLaneMove;
        Edits.Changed += () =>
        {
            SelectionChanged?.Invoke();
            InvalidateVisual();
        };
    }

    /// <summary>Redraws notes, grid and lane names. The playhead layer is separate and is not part of this scene.</summary>
    public new void InvalidateVisual()
    {
        SceneInvalidations++;
        base.InvalidateVisual();
        playheadLayer.Redraw();
    }

    /// <summary>Moves the playhead without rebuilding the scene.</summary>
    internal void InvalidatePlayhead() => playheadLayer.Redraw();

    /// <summary>Row height in px (vertical zoom, Ctrl+Shift+wheel). Design G's compact rows are 22 px.</summary>
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

    /// <summary>The selection and the editing commands (keyboard and menus).</summary>
    public HitEditor Edits { get; } = new();

    private EditSession? Session => Model?.Session;

    public TimeAxis Axis { get; } = new();

    /// <summary>The playhead in ms (set by the window's frame timer).</summary>
    public double Playhead { get; set; }

    /// <summary>The tool in use; leaving the Range tool drops its range.</summary>
    public Tool Tool
    {
        get => tool;
        set
        {
            tool = value;
            if (value != Tool.Range) Edits.ClearRange();
            UpdateCursor(KeyModifiers.None);
        }
    }

    private Tool tool = Tool.Select;

    /// <summary>How far (px) the pointer must move before a press counts as a drag.</summary>
    private const double DragThreshold = 3;

    /// <summary>Snap spacing in osu! beats and whether snapping is on (J).</summary>
    public SnapSpacing Spacing { get => Edits.Spacing; set => Edits.Spacing = value; }

    public bool SnapOn { get => Edits.SnapOn; set => Edits.SnapOn = value; }

    private static readonly IBrush[] VolumeFills = [.. Enumerable.Range(0, 101).Select(v => Paint.Brush(VolumeColor(v)))];

    /// <summary>A hit's colour by its volume, as Cubase colours notes by velocity: blue (quiet) through purple to red (loud).</summary>
    public static Color VolumeColor(int volume) => Paint.LevelColor(Math.Clamp(volume, 0, 100) / 100.0);

    /// <summary>The fill for a volume, 0–100 (cached).</summary>
    public static IBrush VolumeFill(int volume) => VolumeFills[Math.Clamp(volume, 0, 100)];

    // ---- areas the tutorial points at (this control's coordinates)

    /// <summary>The lane list's title: SAMPLE LANES, the filter and the + button.</summary>
    public Rect LaneListHeaderRect => new(0, 0, GridLeft, RulerBottom);

    /// <summary>The icon and + ● M S buttons of the first visible lane, or null when no lane is in view.</summary>
    public Rect? FirstLaneButtonsRect()
    {
        if (Model is not { } model) return null;
        for (int i = Math.Max(0, RowIndexAt(HeaderHeight)); i < model.Rows.Count; i++)
        {
            double y = HeaderHeight + RowTop(i) - scrollY;
            if (y > Bounds.Height) break;
            if (model.Rows[i] is LaneRow) return new Rect(GridLeft - IconFromRight - 14, y, IconFromRight + 14, RowHeight);
        }

        return null;
    }

    /// <summary>The grid of hits, below the ruler and the object strip.</summary>
    public Rect GridRect => new(GridLeft, HeaderHeight, Math.Max(0, Bounds.Width - GridLeft), Math.Max(0, Bounds.Height - HeaderHeight));

    /// <summary>The song waveform strip under the ruler.</summary>
    public Rect WaveRect => new(GridLeft, RulerBottom, Math.Max(0, Bounds.Width - GridLeft), WaveHeight);

    /// <summary>The "Lane details" toggle: show each lane's file index.</summary>
    public bool ShowIndexes { get; set; }

    /// <summary>Before a map is opened: the default lanes are shown to listen to, and the grid can't be drawn in.</summary>
    public bool IsPreview { get; set; }

    public IReadOnlyCollection<EditHit> Selection => selection;

    /// <summary>The moments the selected hits play on.</summary>
    public IReadOnlySet<MomentKey> SelectedMoments => Edits.SelectedMoments;

    public bool AuditionOnPlacement { get; set; } = true;
    public event Action<EditHit>? PlacementPreviewRequested;
    internal void PreviewLaneMove(IReadOnlyList<EditHit> hits)
    {
        if (AuditionOnPlacement) HitsPreviewRequested?.Invoke(hits);
    }

    private Lane? dragPreviewLane;
    public event Action? LaneLayoutChanged;

    internal void PlaceHit(Lane lane, double time)
    {
        if (Session is not { } session) return;
        if (DrawSoundSetRequested?.Invoke(time) == true) return;
        var before = session.State;
        var hit = session.HitAt(lane, time);
        session.Add([hit]);
        if (AuditionOnPlacement && !ReferenceEquals(before, session.State)) PlacementPreviewRequested?.Invoke(hit);
    }
    public event Action<Lane>? LaneListPreviewRequested;
    public bool DrawSoundSetActive { get; set; }
    public Func<double, bool>? DrawSoundSetRequested { get; set; }
    public event Action? SoundSetStrokeFinished;

    public event Action<double>? SeekRequested;

    /// <summary>Right-click on a lane name: the window shows the lane menu (rename, move, delete, add).</summary>
    public event Action<LaneRow>? LaneMenuRequested;
    public event Action<Guid>? DrumEntrySetupRequested;

    /// <summary>A "+" was clicked: a lane's (that lane), or the one above the lane list (null).</summary>
    public event Action<Lane?>? AddLaneRequested;

    /// <summary>The "−" above the lane list: delete the active lane.</summary>
    public event Action? RemoveLaneRequested;

    /// <summary>The "‹" above the lane list: collapse or expand the lane names.</summary>
    public event Action? CollapseNamesRequested;

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

    public double ContentHeight => Tops()[^1];

    /// <summary>Group rows ("Soft sampleset") are compact, as in V6.39; lane rows are <see cref="RowHeight"/>.</summary>
    public const double GroupRowHeight = 18;

    private double HeightOf(Row row) => HeightOf(row, RowHeight);

    private static double HeightOf(Row row, double rowHeight) => row is GroupRow ? Math.Min(GroupRowHeight, rowHeight) : rowHeight;

    /// <summary>
    /// The scroll after the row height changes that keeps the point <paramref name="y"/> px below the header on the same
    /// row, the same fraction into it (group rows don't grow with the lanes, so a plain ratio would drift).
    /// </summary>
    internal static double ScrollKeepingPoint(IReadOnlyList<Row> rows, double oldHeight, double newHeight, double scrollY, double y)
    {
        if (rows.Count == 0) return scrollY;
        double content = y + scrollY, top = 0;
        int i = 0;
        for (; i < rows.Count - 1 && top + HeightOf(rows[i], oldHeight) <= content; i++) top += HeightOf(rows[i], oldHeight);
        double into = Math.Clamp((content - top) / HeightOf(rows[i], oldHeight), 0, 1), newTop = 0;
        for (int j = 0; j < i; j++) newTop += HeightOf(rows[j], newHeight);
        return newTop + into * HeightOf(rows[i], newHeight) - y;
    }

    private double[] rowTops = [0];
    private (IReadOnlyList<Row>? Rows, double Height) rowTopsFor;

    // Each row's top (content coordinates), plus the total height at the end; rebuilt when the rows or their height change.
    private double[] Tops()
    {
        if (Model?.Rows is not { } rows) return [0];
        if (!ReferenceEquals(rowTopsFor.Rows, rows) || rowTopsFor.Height != RowHeight)
        {
            var tops = new double[rows.Count + 1];
            for (int i = 0; i < rows.Count; i++) tops[i + 1] = tops[i] + HeightOf(rows[i]);
            (rowTops, rowTopsFor) = (tops, (rows, RowHeight));
        }

        return rowTops;
    }

    private double RowTop(int i)
    {
        var tops = Tops();
        return tops[Math.Clamp(i, 0, tops.Length - 1)];
    }

    public void RevealLane(Lane lane)
    {
        if (Model is not { } model) return;
        model.RevealLane(lane);
        int row = model.Rows.ToList().FindIndex(r => r is LaneRow l && l.Lane == lane);
        if (row >= 0) ScrollY = RowTop(row) - Math.Max(0, (Bounds.Height - HeaderHeight - RowHeight) / 2);
        LaneLayoutChanged?.Invoke();
        InvalidateVisual();
    }

    public void SetModel(ArrangeModel? model)
    {
        if (Model is not null) Model.Changed -= OnModelChanged;
        Model = model;
        if (model is not null) model.Changed += OnModelChanged;
        scrollY = 0;
        Edits.Model = model;
        drag = Drag.None;
        Axis.End = model?.LastTime ?? 0;
        modelGeneration++;
        InvalidateVisual();
    }

    private void OnModelChanged()
    {
        modelGeneration++;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        playheadLayer.Measure(availableSize);
        return default;
    }

    private Size arrangedSize;

    protected override Size ArrangeOverride(Size finalSize)
    {
        Axis.Width = Math.Max(1, finalSize.Width - GridLeft);
        playheadLayer.Arrange(new Rect(finalSize));
        if (arrangedSize != finalSize)
        {
            arrangedSize = finalSize;
            playheadLayer.Redraw();
        }

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
        var lines = VisibleGridLines(model, gridWidth);
        CollectLinkedPlacements(selection, linkedSelection);
        CollectSelectedObjects(model);

        using (ctx.PushClip(new Rect(0, HeaderHeight, size.Width, size.Height - HeaderHeight)))
        {
            int first = Math.Max(0, RowIndexAt(HeaderHeight));
            for (int i = first; i < model.Rows.Count; i++)
            {
                double y = HeaderHeight + RowTop(i) - scrollY;
                if (y > size.Height) break;
                DrawRow(ctx, model, model.Rows[i], i, y, gridWidth);
            }

            using (ctx.PushClip(new Rect(GridLeft, HeaderHeight, gridWidth, size.Height - HeaderHeight)))
            {
                DrawGridLines(ctx, model, lines, from, to, HeaderHeight, size.Height);
                if (ShowObjectLines) DrawObjectLines(ctx, model, from, to, HeaderHeight, size.Height);

                var range = Edits.Range;
                var hoveredLane = HoveredLane;
                double rangeLeft = range is null ? 0 : GridLeft + Axis.X(range.Start), rangeRight = range is null ? 0 : GridLeft + Axis.X(range.End);
                for (int i = first; i < model.Rows.Count; i++)
                {
                    double y = HeaderHeight + RowTop(i) - scrollY;
                    if (y > size.Height) break;
                    if (model.Rows[i] is GroupRow folder)
                    {
                        using (ctx.PushOpacity(0.3))
                            foreach (var child in model.FolderChildren(folder.Group))
                                DrawLaneNotes(ctx, model, child, y, from, to, HeightOf(folder));
                        continue;
                    }
                    if (model.Rows[i] is not LaneRow lane) continue;
                    if (lane.Lane == hoveredLane)
                        ctx.FillRectangle(HoverFill, new Rect(GridLeft, y, gridWidth, RowHeight));
                    // The range is shaded under the notes, one box per lane row it covers.
                    if (range is not null && range.Lanes.Contains(lane.Lane))
                        ctx.DrawRectangle(RangeFill, RangePen, new Rect(rangeLeft, y, Math.Max(1, rangeRight - rangeLeft), RowHeight));
                    DrawNotes(ctx, model, lane, y, from, to);
                }

                if (drag == Drag.Marquee)
                {
                    var r = new Rect(dragStart, dragNow).Normalize();
                    ctx.DrawRectangle(MarqueeFill, MarqueePen, r);
                }
            }
            if (drag == Drag.Layout && laneDropY is { } dropY)
                ctx.DrawLine(LaneDropPen, new Point(0, dropY), new Point(size.Width, dropY));
        }

        DrawHeader(ctx, model, lines, size.Width, gridWidth);
        DrawWarningMarkers(ctx, from, to);
        ctx.DrawLine(Paint.DividerPen, new Point(GridLeft - 0.5, 0), new Point(GridLeft - 0.5, size.Height));
        if (IsPreview)
            Paint.DrawText(ctx, "Open a map to start drawing (Ctrl+O). Click a lane's ● to hear its sound.", new Point(GridLeft + 20, HeaderHeight + 20), Paint.BaseSize, Paint.Dim, 0);
    }

    /// <summary>Small warning marks above the lanes, coalesced at wide zoom.</summary>
    private void DrawWarningMarkers(DrawingContext ctx, double from, double to)
    {
        double lastX = double.NegativeInfinity;
        foreach (var warning in VisibleWarnings().Where(w => w.Time >= from && w.Time <= to).OrderBy(w => w.Time))
        {
            double x = GridLeft + Axis.X(warning.Time);
            if (x - lastX < 8) continue;
            lastX = x;
            ctx.DrawRectangle(Paint.Brush("#f0aa70"), null, new Rect(x - 3, HeaderHeight - 9, 6, 6), 1, 1);
        }
    }

    private void DrawHeader(DrawingContext ctx, ArrangeModel model, List<GridLine> lines, double width, double gridWidth)
    {
        ctx.FillRectangle(Paint.Chrome2, new Rect(0, 0, width, HeaderHeight));
        ctx.FillRectangle(TitleBand, new Rect(0, 0, GridLeft, RulerBottom));
        string laneTitle = model.DrumMapMode ? "DRUM MAP · " + model.ActiveDrumMap?.Name : "SAMPLE LANES";
        if (model.SelectedLanes.Count > 1) laneTitle += $" · {model.SelectedLanes.Count} SELECTED";
        Paint.DrawText(ctx, laneTitle, new Point(10, 6), 8, Paint.Dim, Math.Max(20, GridLeft - 114), Paint.UiBold);
        double headerY = -((RowHeight - 18) / 2) + 2;
        DrawToggle(ctx, "+", GridLeft - AddLaneFromRight, headerY, null);
        DrawToggle(ctx, "−", GridLeft - RemoveLaneFromRight, headerY, null);
        DrawToggle(ctx, NamesCollapsed ? "›" : "‹", GridLeft - CollapseFromRight, headerY, null);
        // The waveform and the objects strip are rows of the list, as in V6.39.
        ctx.DrawLine(Paint.DividerPen, new Point(0, RulerBottom - 0.5), new Point(GridLeft, RulerBottom - 0.5));
        if (WaveHeight >= 14)
        {
            Paint.DrawText(ctx, "Song audio · objects", new Point(12, RulerBottom + WaveHeight / 2 - 7), 11, Paint.Dim, ListWidth - 60);
            double songY = RulerBottom + (WaveHeight - RowHeight) / 2;
            DrawToggle(ctx, "M", GridLeft - MuteFromRight, songY, SongMuted ? Paint.MuteOn : null);
            DrawToggle(ctx, "S", GridLeft - SoloFromRight, songY, model.SongSoloed ? Paint.SoloOn : null);
        }
        else Paint.DrawText(ctx, "Gameplay objects", new Point(12, RulerBottom + StripHeight / 2 - 7), 11, Paint.Dim, ListWidth - 20);

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
            var bookmarkLabels = BookmarkLabels(width);
            foreach (var line in lines)
            {
                double x = GridLeft + Axis.X(line.Time);
                ctx.DrawLine(line.IsBar ? Paint.BarPen : Paint.BeatPen, new Point(x, rulerTop + (line.IsBar ? 4 : 14)), new Point(x, RulerBottom));
                string label = line.IsBar ? line.Bar.ToString() : $"{line.Bar}.{line.Beat}";
                double labelWidth = line.IsBar ? 24 : 30;
                if (x - lastLabel < labelWidth + 6 || (!line.IsBar && Axis.Zoom * line.BeatLength < 48)) continue;
                var numberBox = new Rect(x + 3, rulerTop + 2, labelWidth, 14);
                if (bookmarkLabels.Exists(b => b.Box.Intersects(numberBox))) continue; // a bookmark label sits there

                Paint.DrawText(ctx, label, new Point(x + 3, rulerTop + 3), RulerFont, line.IsBar ? Paint.RulerBar : Paint.RulerBeat, 0, line.IsBar ? Paint.UiBold : Paint.Ui);
                lastLabel = x;
            }

            DrawLocators(ctx, width);
            DrawBookmarks(ctx, bookmarkLabels);
            DrawWaveform(ctx, width);
            DrawObjects(ctx, model, width);
            // The grid runs on through the waveform and the objects, faded so it doesn't fight them, so hits line up
            // with what's above them.
            using (ctx.PushOpacity(StripGridOpacity))
                DrawGridLines(ctx, model, lines, Axis.Time(0), Axis.Time(gridWidth), RulerBottom, HeaderHeight);
        }

        ctx.DrawLine(Paint.DividerPen, new Point(0, HeaderHeight - 0.5), new Point(width, HeaderHeight - 0.5));
    }

    private const double StripGridOpacity = 0.35; // the grid lines over the waveform and objects

    // Bar and beat lines depend on the axis and the timing, not on the playhead or the selection.
    private List<GridLine> VisibleGridLines(ArrangeModel model, double gridWidth)
    {
        var key = (Axis.Left, Axis.Zoom, gridWidth, GridLeft, modelGeneration);
        if (!gridReady || gridKey != key)
        {
            gridLines = model.GridLines(Axis.Time(0), Axis.Time(gridWidth)).ToList();
            (gridKey, gridReady) = (key, true);
        }

        return gridLines;
    }

    /// <summary>Placements of the current selection, so a linked note is recognised without scanning the selection per note.</summary>
    internal static void CollectLinkedPlacements(IEnumerable<EditHit> hits, HashSet<Guid> into)
    {
        into.Clear();
        foreach (var hit in hits)
            if (hit.SetPlacement != Guid.Empty)
                into.Add(hit.SetPlacement);
    }

    /// <summary>The playhead only: a line through the grid and a cap on the ruler. The scene underneath is not redrawn.</summary>
    private void DrawPlayhead(DrawingContext ctx, Rect bounds)
    {
        if (Model is null || bounds.Width <= 0 || bounds.Height <= 0) return;
        double gridWidth = Math.Max(0, bounds.Width - GridLeft);
        double px = GridLeft + Axis.X(Playhead);
        using (ctx.PushClip(new Rect(GridLeft, HeaderHeight, gridWidth, Math.Max(0, bounds.Height - HeaderHeight))))
            ctx.DrawLine(Paint.PlayheadPen, new Point(px, HeaderHeight), new Point(px, bounds.Height));
        using (ctx.PushClip(new Rect(GridLeft, 0, gridWidth, HeaderHeight)))
            ctx.FillRectangle(Paint.White, new Rect(px - 2, SectionsHeight, 4, RulerHeight));
    }

    /// <summary>Bar, beat and (with snap on, when at least 6 px apart) snap lines between two heights.</summary>
    private void DrawGridLines(DrawingContext ctx, ArrangeModel model, List<GridLine> lines, double from, double to, double top, double bottom)
    {
        if (SnapOn && !Spacing.IsLong && lines.Count > 0 && Axis.Zoom * Spacing.Step(lines[0].BeatLength) >= 6)
            foreach (var (t, fraction) in model.SubLines(from, to, Spacing.Divisions))
            {
                double x = GridLeft + Axis.X(t);
                ctx.DrawLine(Paint.SnapPen(fraction), new Point(x, top), new Point(x, bottom));
            }

        foreach (var line in lines)
        {
            double x = GridLeft + Axis.X(line.Time);
            ctx.DrawLine(line.IsBar ? Paint.BarPen : Paint.BeatPen, new Point(x, top), new Point(x, bottom));
        }
        if (SnapOn && Spacing.IsLong)
            foreach (double time in model.LongSnapLines(from, to, Spacing))
            {
                double x = GridLeft + Axis.X(time);
                ctx.DrawLine(Paint.LongSnapPen, new Point(x, top), new Point(x, bottom));
            }
    }

    // As the key editor's object lines: one faint line per hit position of the objects in view.
    private void DrawObjectLines(DrawingContext ctx, ArrangeModel model, double from, double to, double top, double bottom)
    {
        var objects = model.Objects;
        for (int i = Sorted.LowerBound(objects, from - ObjectLookBack, o => o.Start); i < objects.Count && objects[i].Start <= to; i++)
        {
            if (objects[i].End < from) continue;
            foreach (var node in objects[i].Nodes)
            {
                if (node.Time < from || node.Time > to) continue;
                double x = Math.Round(GridLeft + Axis.X(node.Time)) + 0.5;
                ctx.DrawLine(Paint.StrongObjectLinePen, new Point(x, top), new Point(x, bottom));
            }
        }
    }

    private static readonly IBrush CycleOn = Paint.Brush(Color.Parse("#2f6fd0"), 0.55), CycleOff = Paint.Brush(Color.Parse("#8e8e93"), 0.35),
        SkipOn = Paint.Brush(Color.Parse("#ff453a"), 0.45);

    private static readonly IBrush StripBg = Paint.Brush("#1f1f21"),
        SpinnerFill = Paint.Brush(Color.Parse("#c9a42a"), 0.75), BookmarkFill = Paint.Brush("#4cc3d1");
    private const double BookmarkLabelSize = 11;
    // A dark outline keeps the objects readable over the waveform.
    private static readonly IPen ObjectRing = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#171a1c"), 1.5);
    private static readonly IPen ObjectConflictPen = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#ff453a"), 2);

    /// <summary>The song's waveform (set when decoding finishes) and its offset, so it lines up with the song as heard.</summary>
    public Audio.Waveform? Wave { get; set; }

    // Loudness, then the low, mid and high bands drawn over it, shortest on top.
    private StreamGeometry[]? waveGeometry;
    private (double, double, double, double, double, double, Audio.Waveform?) waveKey;

    public double SongOffset { get; set; }

    /// <summary>
    /// The waveform lane: one vertical line per pixel, the loudest peak in that pixel's time slice. Over it, each frequency
    /// band's share of that height in its own colour (low: kicks, mid: snares, high: hi-hats), the shortest on top.
    /// </summary>
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
            // [0] the loudness; then [1 + rank·3 + band]: per column the bands tallest first (rank 0) to shortest (rank 2),
            // so a shorter band is drawn over a taller one and every band stays visible. Layers 1–9 are the dim body of
            // the sound, 10–18 the same for transients (attacks), which are drawn in full colour.
            var layers = new StreamGeometry[1 + 2 * BandLayers];
            var open = new StreamGeometryContext[layers.Length];
            for (int i = 0; i < layers.Length; i++) open[i] = (layers[i] = new StreamGeometry()).Open();
            void Bar(StreamGeometryContext g, double x, double a)
            {
                g.BeginFigure(new Point(x, mid - a), true);
                g.LineTo(new Point(x + 1, mid - a));
                g.LineTo(new Point(x + 1, mid + a));
                g.LineTo(new Point(x, mid + a));
                g.EndFigure(true);
            }

            Span<int> order = stackalloc int[3];
            float[] bands = new float[3];
            Span<double> attack = stackalloc double[3]; // per band: kicks (low), snares (mid), hats (high)
            double decay = Math.Exp(-msPerPixel / TransientHoldMs);
            for (double x = GridLeft; x < width; x++)
            {
                double t = Axis.Time(x - GridLeft) - SongOffset;
                float peak = wave.Max(t, t + msPerPixel);
                var (low, mids, high) = wave.Bands(t, t + msPerPixel);
                (bands[0], bands[1], bands[2]) = (low, mids, high);
                // A transient in a band: that band much louder than in the moment before. Its colour holds briefly, then fades.
                float before = wave.Max(t - TransientLookBackMs, t);
                var (bLow, bMid, bHigh) = wave.Bands(t - TransientLookBackMs, t);
                ReadOnlySpan<float> earlier = [before * bLow, before * bMid, before * bHigh];
                for (int b = 0; b < 3; b++)
                {
                    float now = peak * bands[b];
                    double rise = now > 0.01f ? Math.Clamp((now - earlier[b] * 1.1) / now * 3, 0, 1) : 0;
                    attack[b] = Math.Max(rise, attack[b] * decay);
                }

                if (peak <= 0.002f) continue;
                Bar(open[0], x, Math.Max(0.5, peak * half));
                (order[0], order[1], order[2]) = (0, 1, 2);
                order.Sort((a, b) => bands[b].CompareTo(bands[a]));
                for (int rank = 0; rank < 3; rank++)
                {
                    int band = order[rank];
                    int layer = attack[band] >= TransientThreshold ? 1 + BandLayers : 1;
                    if (peak * bands[band] > 0.002f) Bar(open[layer + rank * 3 + band], x, Math.Max(0.5, peak * bands[band] * half));
                }
            }

            foreach (var g in open) g.Dispose();
            (waveGeometry, waveKey) = (layers, key);
        }

        // Without bands (none decoded) the loudness alone is drawn, brighter. With bands: the body of the sound dim, its
        // transients (kicks, snares, hats) in full colour so they stand out.
        double opacity = Theme_.Number("wave.opacity");
        bool banded = wave.Low is not null;
        ctx.DrawGeometry(Paint.Brush(Theme_.Color("wave.base"), banded ? opacity * 0.2 : opacity), null, waveGeometry[0]);
        if (!banded) return;
        IBrush Band(string key, double o) => Paint.Brush(Theme_.Color(key), o);
        IBrush[] dim = [Band("wave.low", opacity * 0.3), Band("wave.mid", opacity * 0.3), Band("wave.high", opacity * 0.3)];
        IBrush[] bright = [Band("wave.low", 1), Band("wave.mid", 1), Band("wave.high", 1)];
        for (int i = 1; i < waveGeometry.Length; i++)
            ctx.DrawGeometry((i > BandLayers ? bright : dim)[(i - 1) % 3], null, waveGeometry[i]);
    }

    // Transients in the waveform: the loudness rise over the last 60 ms that counts, and how long its colour holds.
    private const int BandLayers = 9;
    private const double TransientLookBackMs = 60, TransientHoldMs = 35, TransientThreshold = 0.25;

    /// <summary>
    /// The object strip: circles as dots, sliders as a body with H at the head, chevrons at repeats and E at the end,
    /// spinners as a bar. Objects with a conflict get a red outline.
    /// </summary>
    /// <summary>The visible radius of a hit circle in the strip: a share of the strip's height, so it scales with it.</summary>
    private double ObjectRadius => Math.Clamp(StripHeight * 0.36, 5, 60);

    // Objects whose hits are all selected are drawn selected; a selected slider's slide sounds light up in their lanes.
    private HashSet<MomentKey> selectedKeys = [];
    private List<(double Start, double End)> selectedSliders = [];

    private void CollectSelectedObjects(ArrangeModel model)
    {
        selectedKeys = [.. selection.Where(h => h.Anchor is not null).Select(h => h.Anchor!.Value)];
        if (selectedKeys.Count == 0) { selectedSliders = []; return; } // the usual case while playing: nothing to scan
        selectedSliders = [.. model.Objects.Where(o => o.Kind == HitObjectKind.Slider && o.Nodes.All(n => selectedKeys.Contains(n.Key))).Select(o => (o.Start, o.End))];
    }

    private bool InSelectedSlider(double start, double end) => selectedSliders.Any(s => start >= s.Start - 1 && end <= s.End + 1);

    private void DrawObjects(DrawingContext ctx, ArrangeModel model, double width)
    {
        // Over the waveform, as osu!'s editor timeline draws them: combo-coloured circles with their combo number and
        // slider bodies between head and tail, sized by the strip's height (drag its bottom edge) so they keep their
        // proportions; on their own background only when the waveform is folded away.
        double top = RulerBottom, h = StripHeight, cy = top + h / 2;
        if (WaveHeight < ObjectsHeight) ctx.FillRectangle(Paint.Chrome2, new Rect(GridLeft, top, width - GridLeft, h));
        double radius = ObjectRadius;
        var colours = ComboBrushes(model);
        double from = Axis.Time(0) - 50, to = Axis.Time(width - GridLeft) + 50;
        var objects = model.Objects;
        using (ctx.PushClip(new Rect(GridLeft, top, Math.Max(0, width - GridLeft), h)))
            for (int i = Sorted.LowerBound(objects, from - ObjectLookBack, o => o.Start); i < objects.Count && objects[i].Start <= to + radius / Axis.Zoom; i++)
            {
                var o = objects[i];
                if (o.End < from - radius / Axis.Zoom) continue;
                double x0 = GridLeft + Axis.X(o.Start), x1 = GridLeft + Axis.X(o.End);
                bool conflict = ShowExportWarnings && o.Nodes.Any(n => model.Conflicted.Contains(n.Key));
                var ring = conflict ? ObjectConflictPen : ObjectWhiteRing;
                var (body, head) = colours[o.Colour % colours.Length];
                bool selected = o.Nodes.Count > 0 && o.Nodes.All(n => selectedKeys.Contains(n.Key));
                switch (o.Kind)
                {
                    case HitObjectKind.Slider:
                        // The body runs head to tail; repeats and the tail are smaller circles; the head with its number on top.
                        ctx.DrawRectangle(body, selected ? Paint.SelectedRingPen : ObjectRing, new Rect(x0, cy - radius, Math.Max(2, x1 - x0), radius * 2), radius, radius);
                        for (int n = o.Nodes.Count - 1; n >= 1; n--)
                        {
                            var (time, _, position) = o.Nodes[n];
                            double r = position == HitPosition.SliderTail ? radius * 0.8 : radius * 0.55;
                            if (SkinTextures.Loaded) DrawComboCircle(ctx, GridLeft + Axis.X(time), cy, r, head, ring, 0, arrow: position != HitPosition.SliderTail);
                            else ctx.DrawEllipse(head, ring, new Point(GridLeft + Axis.X(time), cy), r, r);
                        }

                        DrawComboCircle(ctx, x0, cy, radius, head, ring, o.Combo);
                        if (selected) ctx.DrawEllipse(null, Paint.SelectedRingPen, new Point(x0, cy), radius + 2, radius + 2);
                        break;

                    case HitObjectKind.Spinner:
                        ctx.DrawRectangle(SpinnerFill, ObjectRing, new Rect(x0, cy - radius * 0.45, Math.Max(2, x1 - x0), radius * 0.9), radius * 0.45, radius * 0.45);
                        ctx.DrawEllipse(SpinnerFill, ring, new Point(x1, cy), radius * 0.6, radius * 0.6);
                        break;

                    default:
                        DrawComboCircle(ctx, x0, cy, radius, head, ring, o.Combo);
                        if (selected) ctx.DrawEllipse(null, Paint.SelectedRingPen, new Point(x0, cy), radius + 2, radius + 2);
                        break;
                }
            }

        ctx.DrawLine(Paint.DividerPen, new Point(GridLeft, top + h - 0.5), new Point(width, top + h - 0.5));
    }

    // A hit circle: the combo colour with a white ring (red when it has a save problem) and its combo number.
    private void DrawComboCircle(DrawingContext ctx, double x, double y, double radius, IBrush fill, IPen ring, int combo, bool arrow = false)
    {
        // lazer's classic skin textures when they could be read (tinted circle, overlay, the skin's digits).
        if (SkinTextures.Loaded && fill is ISolidColorBrush solid)
        {
            SkinTextures.DrawCircle(ctx, new Point(x, y), radius, solid.Color, combo, arrow);
            if (ReferenceEquals(ring, ObjectConflictPen)) ctx.DrawEllipse(null, ring, new Point(x, y), radius, radius);
            return;
        }

        ctx.DrawEllipse(fill, ring, new Point(x, y), radius, radius);
        if (radius < 6 || combo <= 0) return;
        var key = (combo, Math.Round(radius));
        if (!comboLabels.TryGetValue(key, out var text))
        {
            if (comboLabels.Count > 400) comboLabels.Clear(); // sizes change while the strip is resized
            comboLabels[key] = text = Paint.Format(combo.ToString(), radius * 1.05, Brushes.White, Paint.UiBold);
        }

        ctx.DrawText(text, new Point(x - text.Width / 2, y - text.Height / 2));
    }

    private readonly Dictionary<(int, double), FormattedText> comboLabels = [];
    private (IBrush Body, IBrush Head)[] comboBrushes = [];
    private IReadOnlyList<Color>? comboBrushColours;

    // The combo colours as body (faded) and head brushes, rebuilt when the map changes.
    private (IBrush Body, IBrush Head)[] ComboBrushes(ArrangeModel model)
    {
        if (!ReferenceEquals(comboBrushColours, model.ComboColours))
        {
            comboBrushColours = model.ComboColours;
            comboBrushes = [.. model.ComboColours.Select(c => ((IBrush)Paint.Brush(c, 0.55), (IBrush)Paint.Brush(c)))];
        }

        return comboBrushes;
    }

    private static readonly IPen ObjectWhiteRing = new Avalonia.Media.Immutable.ImmutablePen(Brushes.White, 1.5);

    /// <summary>Bookmark flags on the ruler (osu!'s editor bookmarks, saved in the .osu), with their labels.</summary>
    private void DrawBookmarks(DrawingContext ctx, List<(Rect Box, FormattedText Text)> labels)
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

        var back = Theme_.Cached("brush:bookmarkLabelBack", () => (IBrush)Paint.Brush(Theme_.Color("arrange.bg"), 0.9));
        foreach (var (box, text) in labels)
        {
            ctx.DrawRectangle(back, null, box, 2, 2);
            ctx.DrawText(text, new Point(box.X + 3, box.Y + (box.Height - text.Height) / 2));
        }
    }

    /// <summary>
    /// Where each labelled bookmark's label goes: right of its flag, on the ruler's number row (the numbers under a label
    /// are skipped), cut short before the next flag so labels never overlap.
    /// </summary>
    private List<(Rect Box, FormattedText Text)> BookmarkLabels(double width)
    {
        var labels = new List<(Rect, FormattedText)>();
        if (Session is null) return labels;
        var times = Session.BookmarkTimes.ToList();
        for (int i = 0; i < times.Count; i++)
        {
            if (Session.BookmarkLabel(times[i]) is not { Length: > 0 } label) continue;
            double x = GridLeft + Axis.X(times[i]);
            double end = Math.Min(width, i + 1 < times.Count ? GridLeft + Axis.X(times[i + 1]) - 3 : width);
            double room = end - (x + 8) - 6;
            if (room < 14 || end < GridLeft) continue;
            var text = Paint.Format(label, BookmarkLabelSize, BookmarkFill);
            text.MaxTextWidth = room;
            text.MaxLineCount = 1;
            text.Trimming = TextTrimming.CharacterEllipsis;
            labels.Add((new Rect(x + 8, SectionsHeight + 2, text.Width + 6, 14), text));
        }

        return labels;
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
                // V6.39: grip, fold arrow, "Soft sampleset", then the lane count, M and S at the right edge.
                double gh = HeightOf(row), ty = y + (gh - 14) / 2;
                ctx.FillRectangle(Paint.GroupHead, new Rect(0, y, NumberWidth + ListWidth, gh));
                Paint.DrawText(ctx, "⠿", new Point(12, y + (gh - 16) / 2), 12, Paint.Text, 0);
                Paint.DrawText(ctx, model.IsCollapsed(group.Group) ? "▸" : "▾", new Point(34, ty), 11, Paint.Dim, 0);
                Paint.DrawText(ctx, ArrangeModel.GroupName(group.Group), new Point(GroupNameX, ty), 10, Paint.Text, ListWidth - 110, Paint.UiBold);
                Paint.DrawText(ctx, group.LaneCount.ToString(), new Point(NumberWidth + ListWidth - MuteFromRight - 18, ty), 10, Paint.Dim, 0);
                DrawToggle(ctx, "M", NumberWidth + ListWidth - MuteFromRight, y, model.IsGroupMuted(group.Group) ? Paint.MuteOn : null, gh);
                DrawToggle(ctx, "S", NumberWidth + ListWidth - SoloFromRight, y, model.IsGroupSoloed(group.Group) ? Paint.SoloOn : null, gh);
                ctx.FillRectangle(GroupBand, new Rect(GridLeft, y, gridWidth, gh));
                Paint.DrawText(ctx, ArrangeModel.GroupName(group.Group), new Point(GridLeft + 6, ty), 10, Paint.Dim, 0, Paint.UiBold);
                break;
            }

            case UnassignedDrumRow empty:
                ctx.FillRectangle(Paint.LaneHeadA, new Rect(0, y, GridLeft, RowHeight));
                Paint.DrawText(ctx, "⠿", new Point(GripX, y + (RowHeight - 16) / 2), 12, Paint.Dim, 0);
                Paint.DrawText(ctx, empty.Entry.Name + " (unassigned — click to assign)", new Point(28, y + (RowHeight - LaneFont * 1.4) / 2), LaneFont, Paint.Dim, GridLeft - 40);
                break;
            case LaneRow lane:
            {
                bool greyed = !model.IsAudible(lane.Lane);
                // V6.39: grip, tree line to the group's first lane, "• name", then the
                // sound icon, + (add a lane), ● (hear it), M and S at the right edge.
                ctx.FillRectangle(Paint.LaneHeadA, new Rect(0, y, NumberWidth + ListWidth, RowHeight));
                if (model.SelectedLanes.Contains(lane.Lane))
                    ctx.FillRectangle(LaneSelectionFill, new Rect(0, y, NumberWidth + ListWidth, RowHeight));
                if (lane.Lane == model.ActiveLane)
                {
                    // V6.39's active lane: a brighter row with a blue bar at its left edge.
                    ctx.FillRectangle(ActiveFill, new Rect(0, y, NumberWidth + ListWidth, RowHeight));
                    ctx.FillRectangle(ActiveBar, new Rect(0, y, 3, RowHeight));
                }
                // Positions measured on V6.39 (list's left edge = 0): the first lane of a group has its grip at 17, a faint
                // disclosure dot at 36, the sample dot at 49 and the name at 55; the lanes under it shift 18 px right, with
                // the tree line at 21 (down to the row's middle for the last one, then across to 31).
                var rows = model.Rows;
                bool last = index + 1 >= rows.Count || rows[index + 1] is not LaneRow;
                double indent = (model.DepthOf(lane.Lane) - 1) * ChildIndent, cy = y + RowHeight / 2;
                Paint.DrawText(ctx, "⠿", new Point(GripX + indent, y + (RowHeight - 16) / 2), 12, Paint.Dim, 0);
                if (indent > 0)
                {
                    double branch = TreeX + indent - ChildIndent;
                    ctx.DrawLine(TreePen, new Point(branch, y), new Point(branch, last ? cy : y + RowHeight));
                    ctx.DrawLine(TreePen, new Point(branch, cy), new Point(branch + 10, cy));
                }
                else if (!last && !model.DrumMapMode) ctx.DrawLine(TreePen, new Point(TreeX, y + RowHeight * 0.6), new Point(TreeX, y + RowHeight));
                if (model.IsFamilyParent(lane.Lane))
                    Paint.DrawText(ctx, model.IsFamilyCollapsed(lane.Lane) ? "▸" : "▾", new Point(DisclosureX + indent - 4, cy - 8), 11, Paint.Text, 0);
                else ctx.DrawEllipse(DisclosureDot, null, new Point(DisclosureX + indent, cy), 1.2, 1.2);

                bool silent = lane.Lane.Missing || model.Silent.Contains(lane.Lane);
                string name = (lane.Lane.Source == LaneSource.StoryboardFile ? "SB " : "") + model.DrumRowName(lane);
                if (silent) name += lane.Lane.Missing ? "  (missing)" : "  (no sound)";
                if (!model.DrumMapMode && model.SlotOf(lane.Lane).Source == LaneSource.Skin) name += lane.Lane.Source == LaneSource.Skin ? " · skin" : " · slot 0";
                if (ShowIndexes && lane.Index is { } idx) name += $"  · idx {idx}";
                ctx.DrawEllipse(silent ? MissingDot : LoadedDot, null, new Point(DotX + indent, cy), 2.5, 2.5);
                double nameWidth = GridLeft - IconFromRight - 14 - NameX - indent;
                if (nameWidth > 12) // collapsed (‹): no room for names, the icon says which sound it is
                    Paint.DrawText(ctx, name, new Point(NameX + indent, y + (RowHeight - LaneFont * 1.4) / 2), LaneFont, silent ? Paint.MissingText : Paint.Text,
                        nameWidth, model.IsFamilyBase(lane.Lane) ? Paint.UiBold : Paint.Ui);
                DrawSoundIcon(ctx, model.SlotOf(lane.Lane).Sample?.Sound, new Point(NumberWidth + ListWidth - IconFromRight, cy), IconTone);
                DrawToggle(ctx, "+", NumberWidth + ListWidth - AddFromRight, y, AddFill);
                DrawToggle(ctx, "●", NumberWidth + ListWidth - PreviewFromRight, y, null);
                DrawToggle(ctx, "M", NumberWidth + ListWidth - MuteFromRight, y, model.IsMuted(lane.Lane) ? Paint.MuteOn : null);
                DrawToggle(ctx, "S", NumberWidth + ListWidth - SoloFromRight, y, model.IsSoloed(lane.Lane) ? Paint.SoloOn : null);

                break;
            }
        }

        double bottom = y + HeightOf(row) - 0.5;
        ctx.DrawLine(Paint.DividerPen, new Point(0, bottom), new Point(GridLeft + gridWidth, bottom));
    }

    // Lane row positions from the list's left edge, measured on V6.39.
    private const double GripX = 16, TreeX = 21, DisclosureX = 36, DotX = 49, NameX = 55, ChildIndent = 18;

    private static readonly IBrush DisclosureDot = Paint.Brush("#6a6a7a");

    private static readonly IPen TreePen = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#5a5a68"), 1);
    private static readonly Color IconTone = Color.Parse("#c9c9d6");
    private static readonly IBrush ActiveFill = Paint.Brush(Colors.White, 0.07), ActiveBar = Paint.Brush("#0a84ff");
    private static readonly IBrush LaneSelectionFill = Paint.Brush(Color.Parse("#73b7ff"), 0.18);
    private static readonly IBrush AddFill =Paint.Brush("#324761"); // V6.39 .addLayer: #324761 with a #526b8d border

    /// <summary>The song is muted (drawn on the Song audio row's M); clicking that M raises <see cref="SongMuteRequested"/>.</summary>
    public bool SongMuted { get; set; }

    public event Action? SongMuteRequested;

    private static readonly IBrush TitleBand = Paint.Brush("#303038"), GroupBand = Paint.Brush(Color.Parse("#000000"), 0.18),
        LoadedDot = Paint.Brush("#85b9a9"), MissingDot = Paint.Brush("#c6a772");



    /// <summary>A small icon for the sound (V6.39 shows one per lane): ring, wave, cymbal or hands; a note for other files.</summary>
    // V6.39's lane icons (src/icons/*.svg on tag V6.39): stroked outlines in a 34×32 box, drawn 24 px high at 84 % opacity.
    // Circles are (cx, cy, r, filled); paths are SVG path data, which Avalonia parses as is.
    private static readonly Dictionary<string, (string[] Paths, (double X, double Y, double R, bool Fill)[] Circles)> SoundIcons = new()
    {
        ["hitnormal"] = (["M17 2v2m0 24v2M3 16h2m24 0h2"], [(17, 16, 10, false), (17, 16, 6, false), (17, 16, 1.5, true)]),
        ["hitwhistle"] = (["M5 15.5 12 10l7 1.5a6 6 0 1 1-6 9l-5 1.8-3-3.8z", "m5 15.5 5 4 7-6M12 10v5l-4 3", "m19 6 1-3m3 5 3-1m-12 0-1-3"], [(19, 17, 2.5, false)]),
        ["hitfinish"] = (["M5 15c4-5 15-7 23-3-4 5-15 7-23 3z", "m14 11 2-3 4-.5 2 3M17 15l-2 10m-5 2h10M24 5l2-3m1 5 3-1", "m5 6 8 5"], []),
        ["hitclap"] = (["m8 18-2-6c-.8-2 1-3 2-1l4 6-3-10c-.5-2 1.6-2.7 2.3-.8L15 16 13.5 6c-.3-2 2-2.2 2.4-.3L18 16l.3-7c.1-2 2.3-2 2.4 0l.4 10 2-3c1.4-1.6 3 .1 1.8 1.6l-4 7c-2 3.5-6.7 4.8-10 1.8L5 20c-1.4-1.6.5-3.1 1.8-1.8l3 2.7", "m25 9 2-2m-3-1 1-3m3 10 3-1"], []),
    };

    private static readonly Dictionary<string, Geometry[]> IconGeometry = SoundIcons.ToDictionary(
        kv => kv.Key, kv => kv.Value.Paths.Select(p => (Geometry)StreamGeometry.Parse(p)).ToArray());

    private static readonly IBrush IconStroke = Paint.Brush(Color.Parse("#b6c8c3"), 0.84), WhistleStroke = Paint.Brush(Color.Parse("#c9c0a4"), 0.84);
    private static readonly IPen IconPen = new Pen(IconStroke, 1.45) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },
        WhistlePen = new Pen(WhistleStroke, 1.45) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

    private static void DrawSoundIcon(DrawingContext ctx, string? sound, Point c, Color tone)
    {
        if (sound is null || !SoundIcons.TryGetValue(sound, out var icon))
        {
            Paint.DrawText(ctx, "♪", new Point(c.X - 7, c.Y - 12), 18, Paint.Brush(tone, 0.84), 0);
            return;
        }

        const double scale = 24.0 / 32;
        var brush = sound == "hitwhistle" ? WhistleStroke : IconStroke; // V6.39 gives the whistle a light sepia
        var pen = sound == "hitwhistle" ? WhistlePen : IconPen;
        using (ctx.PushTransform(Matrix.CreateTranslation(-17, -16) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(c.X, c.Y)))
        {
            foreach (var geometry in IconGeometry[sound]) ctx.DrawGeometry(null, pen, geometry);
            foreach (var (x, y, r, fill) in icon.Circles) ctx.DrawEllipse(fill ? brush : null, fill ? null : pen, new Point(x, y), r, r);
        }
    }

    private void DrawToggle(DrawingContext ctx, string letter, double x, double y, IBrush? lit, double? rowHeight = null)
    {
        // V6.39's row buttons: 18×17 px, 4 px corners, 9 px text; the + (add) is 17×17 with 12 px text. Smaller in short rows.
        bool add = letter == "+";
        double row = rowHeight ?? RowHeight;
        double w = add ? 17 : ToggleWidth, h = Math.Min(17, row - 2), size = add ? 12 : 9;
        var rect = new Rect(x, y + (row - h) / 2, w, h);
        ctx.DrawRectangle(lit ?? Paint.Button, ToggleBorder, rect, add ? 3 : 4, add ? 3 : 4);
        Paint.DrawText(ctx, letter, new Point(x + (w - size * 0.62) / 2, rect.Y + (h - size * 1.35) / 2), size, lit is null ? Paint.Dim : Paint.White, 0, Paint.UiBold);
    }

    private static readonly IPen ToggleBorder = new Avalonia.Media.Immutable.ImmutablePen(Paint.Brush("#55556a"), 1);

    private void DrawNotes(DrawingContext ctx, ArrangeModel model, LaneRow row, double y, double from, double to)
    {
        // Draw summaries behind the parent's own notes, so real notes at the same time stay clear.
        using (ctx.PushOpacity(0.3))
            foreach (var child in model.CollapsedChildren(row.Lane))
                DrawLaneNotes(ctx, model, child, y, from, to);
        DrawLaneNotes(ctx, model, row, y, from, to);
    }

    private void DrawLaneNotes(DrawingContext ctx, ArrangeModel model, LaneRow row, double y, double from, double to, double? height = null)
    {
        if (!model.Notes.TryGetValue(row.Lane, out var notes)) return;
        bool greyed = !model.IsAudible(row.Lane);
        // Hits are coloured by volume (lanes have no colours of their own); muted lanes are grey.
        IBrush muted = Paint.MutedFill, mutedSpan = MutedSpans; // once per row, not per note
        var session = Session;
        double cy = y + (height ?? RowHeight) / 2;
        double noteSize = Math.Min(NoteSize, (height ?? RowHeight) * 0.5);

        // Notes are sorted by start; slider body spans can start before the view, so look back one visible width.
        for (int i = Sorted.LowerBound(notes, from - Axis.VisibleDuration, n => n.Time); i < notes.Count; i++)
        {
            var n = notes[i];
            if (n.Time > to + 50) break;
            if (n.EndTime < from - 50) continue; // spans end at EndTime; one-shot notes have EndTime == Time
            double x = GridLeft + Axis.X(n.Time);
            int volume = n.Hit is { } h && session is not null ? session.VolumeOf(h) : 100;
            var fill = greyed ? muted : n.Hit is null ? Paint.Dim : VolumeFill(volume);
            if (n.EndTime > n.Time)
                ctx.DrawRectangle(greyed ? mutedSpan : n.Hit is null ? NeutralSpan : VolumeSpans[Math.Clamp(volume, 0, 100)],
                    n.Hit is null && InSelectedSlider(n.Time, n.EndTime) ? Paint.SelectedRingPen : null, new Rect(x, cy - 3, Math.Max(2, (n.EndTime - n.Time) * Axis.Zoom), 6), 3, 3);
            else if (n.Passive)
                Paint.DrawDiamond(ctx, new Point(x, cy), Math.Max(2, noteSize - 4), fill, Paint.NoteRingPen);
            else if (n.Hit is { } hit && selection.Contains(hit))
                Paint.DrawDiamond(ctx, new Point(x, cy), noteSize, Paint.White, Paint.SelectedRingPen);
            else if (n.Hit is { SetPlacement: var linked } && linkedSelection.Contains(linked))
                Paint.DrawDiamond(ctx, new Point(x, cy), noteSize, fill, Paint.SelectedRingPen);
            else if (VisibleProblem(n.Hit!))
                Paint.DrawDiamond(ctx, new Point(x, cy), noteSize, Paint.White, ConflictRing);
            else if (model.IsPreviewMuted(n.Hit!))
                Paint.DrawDiamond(ctx, new Point(x, cy), noteSize, Paint.MutedFill, Paint.NoteRingPen);
            else
                Paint.DrawDiamond(ctx, new Point(x, cy), noteSize, fill, Paint.NoteRingPen);
        }
    }

    // Slider-body spans: the volume colour, faded.
    private static readonly IBrush[] VolumeSpans = [.. Enumerable.Range(0, 101).Select(v => Paint.Brush(VolumeColor(v), 0.45))];
    private static readonly IBrush NeutralSpan = Paint.Brush(Color.Parse("#bdbdbd"), 0.45);
    private static IBrush MutedSpans => UiTheme.Current.Cached("brush:mutedSpan", () => (IBrush)Paint.Brush(ArrangeModel.MutedColor, 0.45));

    // ---------------------------------------------------------------- scrolling and zoom

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var (action, steps) = WheelMode.For(WheelMode.Osu, e.KeyModifiers, e.Delta.X, e.Delta.Y, overHeaders: p.X < GridLeft);
        switch (action)
        {
            case WheelAction.ZoomRows:
            {
                // Vertical zoom, keeping the point under the mouse in place: the same row, the same fraction into it
                // (group rows don't grow with the lanes, so a plain ratio would drift).
                double old = RowHeight;
                RowHeight = Math.Round(RowHeight * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15));
                ScrollY = ScrollKeepingPoint(Model?.Rows ?? [], old, RowHeight, scrollY, p.Y - HeaderHeight);
                break;
            }
            case WheelAction.ZoomTime: Axis.ZoomAt(e.Delta.Y > 0 ? 1.25 : 0.8, Math.Max(0, p.X - GridLeft)); break;
            case WheelAction.Divisor: DivisorWheel?.Invoke(e.Delta.Y > 0 ? 1 : -1); break; // finer up, coarser down
            case WheelAction.Pan: Axis.Left -= (e.Delta.Y + e.Delta.X) * 120 / Axis.Zoom; break;
            case WheelAction.Step: PlayheadStepRequested?.Invoke(steps); break;
            default: ScrollY -= e.Delta.Y * RowHeight * 3; break;
        }
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>Alt+wheel: +1 for a finer snap, -1 for a coarser one (the window owns the divisor list).</summary>
    public event Action<int>? DivisorWheel;

    /// <summary>The osu! editor's wheel: move the playhead by this many snap steps (later = +).</summary>
    public event Action<int>? PlayheadStepRequested;

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

    /// <summary>Ctrl+Up/Down (Cubase's Zoom Tracks): taller or shorter rows, keeping the top row in place.</summary>
    public void ZoomRows(double factor)
    {
        double old = RowHeight;
        RowHeight = Math.Round(RowHeight * factor);
        ScrollY = ScrollKeepingPoint(Model?.Rows ?? [], old, RowHeight, scrollY, 0);
    }

    /// <summary>Lets Ctrl+Shift+wheel zoom go and follows the theme's row height again (after it's edited).</summary>
    public void FollowThemeRowHeight()
    {
        rowHeight = null;
        ScrollY = scrollY;
    }

    // ---------------------------------------------------------------- hit-testing

    // Where the lane list's buttons sit, from the number column's right edge. Drawing and hit-testing both use these.
    // How far back to look for objects that started before the view (long sliders and spinners).
    private const double ObjectLookBack = 60000;

    // The list's buttons, measured from its right edge (V6.39 keeps them there); names from the left.
    private const double ToggleWidth = 18, SoloFromRight = 25, MuteFromRight = 48, PreviewFromRight = 71, AddFromRight = 93, IconFromRight = 111,
        AddLaneFromRight = 70, RemoveLaneFromRight = 48, CollapseFromRight = 26, GroupNameX = 46;

    private enum ListTarget
    {
        Collapse,
        Mute,
        Solo,
        Add,
        Preview,
        Name,
    }

    // x is measured from the number column's right edge, like the drawing.
    private static ListTarget ListTargetAt(Row row, double x)
    {
        bool In(double fromRight) => x >= ListWidth - fromRight && x < ListWidth - fromRight + ToggleWidth;
        return row switch
        {
            _ when In(MuteFromRight) => ListTarget.Mute,
            _ when In(SoloFromRight) => ListTarget.Solo,
            GroupRow when x < GroupNameX - NumberWidth + 2 => ListTarget.Collapse,
            LaneRow when In(AddFromRight) => ListTarget.Add,
            LaneRow when In(PreviewFromRight) => ListTarget.Preview,
            _ => ListTarget.Name,
        };
    }

    private bool OnLaneGrip(Row row, Point p)
    {
        if (p.X >= GridLeft) return false;
        double x = row is GroupRow ? 12 : GripX + (row is LaneRow lane ? (Model!.DepthOf(lane.Lane) - 1) * ChildIndent : 0);
        return p.X >= x - 4 && p.X < x + 14;
    }

    private void UpdateLaneDrop(Point p)
    {
        dropRow = null;
        laneDropY = null;
        if (!layoutMoved || Model is not { } model || p.X < 0 || p.X >= GridLeft || p.Y < HeaderHeight || p.Y >= Bounds.Height || model.Rows.Count == 0) return;
        int i = Math.Clamp(RowIndexAt(p.Y), 0, model.Rows.Count - 1);
        var target = model.Rows[i];
        if (model.DrumMapMode && draggedRow is LaneRow or UnassignedDrumRow)
        {
            if (target is not (LaneRow or UnassignedDrumRow)) return;
            double top = HeaderHeight + RowTop(i) - scrollY;
            dropAfter = p.Y >= top + RowHeight / 2;
            laneDropY = top + (dropAfter ? RowHeight : 0); dropRow = target; return;
        }
        if (draggedRow is LaneRow lane)
        {
            if (target is not LaneRow other) return;
            string? family = model.FamilyKey(lane.Lane), targetFamily = model.FamilyKey(other.Lane);
            if (family == targetFamily && family is not null && (model.IsFamilyBase(lane.Lane) || model.IsFamilyBase(other.Lane))) return;
            if (family != targetFamily)
            {
                int start = i, end = i;
                while (targetFamily is not null && start > 0 && model.Rows[start - 1] is LaneRow a && model.FamilyKey(a.Lane) == targetFamily) start--;
                while (targetFamily is not null && end + 1 < model.Rows.Count && model.Rows[end + 1] is LaneRow b && model.FamilyKey(b.Lane) == targetFamily) end++;
                dropAfter = p.Y >= HeaderHeight + RowTop(i) - scrollY + RowHeight / 2;
                laneDropY = HeaderHeight + RowTop(dropAfter ? end + 1 : start) - scrollY;
                dropRow = target;
                return;
            }
            double top = HeaderHeight + RowTop(i) - scrollY;
            dropAfter = p.Y >= top + RowHeight / 2;
            laneDropY = top + (dropAfter ? RowHeight : 0);
        }
        else if (draggedRow is GroupRow group)
        {
            if (target.Group == group.Group) return;
            int first = i, last = i + 1;
            while (first > 0 && model.Rows[first - 1].Group == target.Group) first--;
            while (last < model.Rows.Count && model.Rows[last].Group == target.Group) last++;
            double top = HeaderHeight + RowTop(first) - scrollY, bottom = HeaderHeight + RowTop(last) - scrollY;
            dropAfter = p.Y >= (top + bottom) / 2;
            laneDropY = dropAfter ? bottom : top;
        }
        else return;
        dropRow = target;
    }

    private int RowIndexAt(double y)
    {
        double content = y - HeaderHeight + scrollY;
        var tops = Tops();
        if (content < 0) return (int)Math.Floor(content / RowHeight);
        if (content >= tops[^1]) return tops.Length - 1 + (int)Math.Floor((content - tops[^1]) / RowHeight);
        int lo = 0, hi = tops.Length - 2; // the last row whose top is at or above the point
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (tops[mid] <= content) lo = mid;
            else hi = mid - 1;
        }

        return lo;
    }

    private Row? RowAt(double y)
    {
        int i = RowIndexAt(y);
        return Model is { } model && y >= HeaderHeight && i >= 0 && i < model.Rows.Count ? model.Rows[i] : null;
    }

    /// <summary>The editable hit nearest a grid x in a lane, within a note's width.</summary>
    private double? lastPaintX; // where the paint stroke placed last, to fill the grid points it passed

    // The hit in a lane at exactly this time (within a millisecond), if any.
    private EditHit? HitAtTime(LaneRow row, double time)
    {
        if (Model?.Notes.TryGetValue(row.Lane, out var notes) != true) return null;
        int i = Sorted.Nearest(notes!, time, 1, n => n.Time, n => n.Hit is not null);
        return i >= 0 ? notes![i].Hit : null;
    }

    private EditHit? HitUnder(LaneRow row, double gridX)
    {
        if (Model?.Notes.TryGetValue(row.Lane, out var notes) != true) return null;
        int i = Sorted.Nearest(notes!, Axis.Time(gridX), NoteSize / Axis.Zoom, n => n.Time, n => n.Hit is not null);
        return i >= 0 ? notes![i].Hit : null;
    }

    internal Lane? SummaryLaneUnder(Row row, double gridX)
    {
        if (Model is not { } model) return null;
        var children = row is GroupRow folder ? model.FolderChildren(folder.Group)
            : row is LaneRow parent ? model.CollapsedChildren(parent.Lane) : [];
        double time = Axis.Time(gridX), tolerance = Math.Max(3, NoteSize) / Axis.Zoom;
        foreach (var child in children)
            if (model.Notes.TryGetValue(child.Lane, out var notes) && notes.Any(n =>
                Math.Abs(n.Time - time) <= tolerance || n.EndTime > n.Time && time >= n.Time && time <= n.EndTime))
                return child.Lane;
        return null;
    }

    private double SnapTime(double time) => Edits.SnapTime(time);

    /// <summary>The inspector part under a point, for Pick.</summary>
    public string PartAt(Point p)
    {
        if (p.Y < HeaderHeight)
            return p.X >= GridLeft && p.Y < SectionsHeight ? "sections" : p.X >= GridLeft && p.Y >= RulerBottom && p.Y < RulerBottom + WaveHeight ? "waveform" : "header";
        if (Math.Abs(p.X - (GridLeft + Axis.X(Playhead))) <= 4) return "playhead";
        if (RowAt(p.Y) is not { } row) return "arrange";

        if (p.X < NumberWidth) return row is LaneRow ? "lanelist" : "grouprow";
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
        var resizePoint = e.GetCurrentPoint(this);
        if (resizePoint.Properties.IsLeftButtonPressed && OnListEdge(resizePoint.Position))
        {
            if (e.ClickCount == 2)
            {
                UserListWidth = null; // back to the theme's width
                InvalidateVisual();
                LaneListResized?.Invoke();
            }
            else
            {
                drag = Drag.ResizeList;
                e.Pointer.Capture(this);
            }
            e.Handled = true;
            return;
        }
        if (Model is not { } model || Session is not { } session) return;
        var point = e.GetCurrentPoint(this);
        var p = point.Position;
        if (!IsPreview && Tool == Tool.Grab && p.X >= GridLeft && p.Y >= RulerBottom)
        {
            if (!point.Properties.IsLeftButtonPressed) { e.Handled = true; return; }
            drag = Drag.Pan; dragStart = p; panLeft = Axis.Left; panScroll = ScrollY;
            e.Pointer.Capture(this); e.Handled = true; return;
        }
        bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // Before a map is opened only the lane list's buttons work: there is nothing to draw on yet.
        // (The + − ‹ above the list still work.)
        if (IsPreview && ((p.Y < HeaderHeight && !(p.Y < 22 && p.X < GridLeft)) || p.X >= GridLeft || !point.Properties.IsLeftButtonPressed)) return;

        // The header, and just under it over the grid: the strip's bottom edge can be grabbed from either side.
        if (p.Y < HeaderHeight || (p.X >= GridLeft && p.Y < HeaderHeight + 4))
        {
            // A right-click on a slider in the object strip: what its body plays.
            if (point.Properties.IsRightButtonPressed && p.X >= GridLeft && p.Y >= RulerBottom && p.Y < RulerBottom + StripHeight)
            {
                SliderPressed(model, session, Math.Max(0, Axis.Time(p.X - GridLeft)));
                return;
            }

            if (!point.Properties.IsLeftButtonPressed) return;
            // The + − ‹ in the SAMPLE LANES title.
            bool On(double fromRight) => p.X >= GridLeft - fromRight && p.X < GridLeft - fromRight + ToggleWidth && p.Y < 22;
            if (On(AddLaneFromRight))
            {
                AddLaneRequested?.Invoke(null);
                return;
            }

            if (On(RemoveLaneFromRight))
            {
                RemoveLaneRequested?.Invoke();
                return;
            }

            if (On(CollapseFromRight))
            {
                CollapseNamesRequested?.Invoke();
                return;
            }

            if (p.Y >= RulerBottom && p.Y < RulerBottom + WaveHeight)
            {
                // The M and S on the Song audio row.
                if (p.X >= GridLeft - MuteFromRight && p.X < GridLeft - MuteFromRight + ToggleWidth)
                {
                    SongMuteRequested?.Invoke();
                    return;
                }

                if (p.X >= GridLeft - SoloFromRight && p.X < GridLeft - SoloFromRight + ToggleWidth)
                {
                    model.ToggleSongSolo();
                    return;
                }
            }

            if (p.X < GridLeft) return;
            double t = Math.Max(0, Axis.Time(p.X - GridLeft));
            double waveBottom = RulerBottom + StripHeight;
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
            else if (p.Y >= RulerBottom && p.Y < waveBottom && Math.Abs(p.Y - (RulerBottom + StripHeight / 2)) <= ObjectRadius && model.ObjectAt(t, ObjectRadius / Axis.Zoom) is not null
                     && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                ObjectPressed(model, session, t, e.ClickCount == 2); // an object over the waveform
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) SetLocators(SnapTime(t), LocatorRight ?? SnapTime(t));
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) SetLocators(LocatorLeft ?? SnapTime(t), SnapTime(t));
            else if (p.Y < SectionsHeight)
            {
                // The upper ruler, as in Cubase: drag the range's ends to resize it, inside it to move it (a click there
                // toggles cycling), anywhere else to draw a new range.
                drag = CycleDragAt(p.X);
                if (LocatorLeft is { } l0 && LocatorRight is { } r0) cycleAtStart = (l0, r0);
                dragStart = dragNow = p;
                e.Pointer.Capture(this);
            }
            else
            {
                // The ruler and the waveform move the playhead; a click near a bookmark flag lands exactly on it.
                double near = 5 / Axis.Zoom;
                var bookmark = session.NearestBookmark(t, near);
                SeekRequested?.Invoke(p.Y < RulerBottom && bookmark is { } b ? b : SnapTime(t));
            }

            InvalidateVisual();
            return;
        }

        if (RowAt(p.Y) is not { } row) return;
        // The lane touched last is the active one (its name clicked, or a hit drawn, picked or moved in it).
        if (row is LaneRow touched && point.Properties.IsLeftButtonPressed) model.SetActiveRow(touched);
        if (row is UnassignedDrumRow empty && p.X < GridLeft)
        {
            if (point.Properties.IsLeftButtonPressed && OnLaneGrip(row, p))
            { drag = Drag.Layout; draggedRow = row; dropRow = null; dragStart = dragNow = p; layoutMoved = false; e.Pointer.Capture(this); }
            else DrumEntrySetupRequested?.Invoke(empty.Entry.Id);
            return;
        }
        if (p.X < GridLeft)
        {
            if (point.Properties.IsLeftButtonPressed && row is LaneRow family && model.IsFamilyParent(family.Lane))
            {
                double disclosure = DisclosureX + (model.DepthOf(family.Lane) - 1) * ChildIndent;
                if (Math.Abs(p.X - disclosure) <= 7)
                {
                    model.ToggleFamily(family.Lane);
                    ScrollChanged?.Invoke();
                    LaneLayoutChanged?.Invoke();
                    return;
                }
            }
            if (point.Properties.IsLeftButtonPressed && OnLaneGrip(row, p))
            {
                drag = Drag.Layout;
                draggedRow = row;
                dropRow = null;
                laneDropY = null;
                layoutMoved = false;
                dragStart = dragNow = p;
                e.Pointer.Capture(this);
                return;
            }
            if (point.Properties.IsRightButtonPressed && row is LaneRow menuLane)
            {
                LaneMenuRequested?.Invoke(menuLane);
                return;
            }

            if (!point.Properties.IsLeftButtonPressed) return;
            var target = ListTargetAt(row, p.X - NumberWidth);
            if (row is LaneRow mixerLane && (target is ListTarget.Mute or ListTarget.Solo)
                && !model.SelectedLanes.Contains(mixerLane.Lane)) model.SelectLane(mixerLane.Lane);
            switch (row, target)
            {
                case (GroupRow g, ListTarget.Collapse): model.ToggleCollapsed(g.Group); break;
                case (GroupRow g, ListTarget.Mute): model.ToggleGroupMute(g.Group); break;
                case (GroupRow g, ListTarget.Solo): model.ToggleGroupSolo(g.Group); break;
                case (LaneRow l, ListTarget.Mute): model.ToggleMute(l.Lane); break;
                case (LaneRow l, ListTarget.Solo): model.ToggleSolo(l.Lane); break;
                case (LaneRow l, ListTarget.Add): AddLaneRequested?.Invoke(l.Lane); break;
                case (LaneRow l, ListTarget.Preview): LaneListPreviewRequested?.Invoke(l.Lane); break;
                case (LaneRow l, _):
                    model.SelectLane(l.Lane, e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Control));
                    if (!additive) LaneListPreviewRequested?.Invoke(l.Lane);
                    break;
            }

            return;
        }

        double gridX = p.X - GridLeft;
        var under = row is LaneRow lr ? HitUnder(lr, gridX) : null;
        // Draw erases when it starts on a hit, but zoomed out a diamond covers several grid points: with snapping, only a
        // hit on the grid point the click snaps to counts, so a free point next to a hit still gets drawn.
        var drawOver = under;
        if (under is not null && SnapOn && row is LaneRow lrSnap && HitAtTime(lrSnap, SnapTime(Axis.Time(gridX))) is null) drawOver = null;

        if ((point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed)
            && under is null && SummaryLaneUnder(row, gridX) is { } summary)
        {
            model.RevealLane(summary);
            int revealed = model.Rows.ToList().FindIndex(r => r is LaneRow l && l.Lane == summary);
            if (revealed >= 0) ScrollY = RowTop(revealed) - (p.Y - HeaderHeight);
            LaneLayoutChanged?.Invoke(); ScrollChanged?.Invoke(); InvalidateVisual();
            return;
        }

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

            case Tool.Draw when row is LaneRow lane && (drawOver is null || DrawSoundSetActive):
                session.BeginGesture("Draw");
                drag = Drag.Paint;
                paintRow = lane;
                lastPaintX = gridX;
                PlaceHit(lane.Lane, SnapTime(Axis.Time(gridX)));
                break;

            case Tool.Draw or Tool.Erase:
                // Erase strokes, and Draw strokes that start on a hit: Draw erases only in its own lane, Erase in any lane.
                session.BeginGesture("Erase");
                drag = Drag.Wipe;
                wipeLane = tool == Tool.Draw ? (row as LaneRow)?.Lane : null;
                if ((tool == Tool.Draw ? drawOver : under) is { } erased) EraseHit(erased);
                break;

            case Tool.Range:
                // A press starts a new range; a click without a drag clears it (OnPointerReleased).
                drag = Drag.Range;
                break;

            case Tool.Mute:
                // Preview-only: osu! can't store a muted sound, so this only silences it while listening.
                if (under is not null) model.TogglePreviewMute(under);
                drag = Drag.None;
                break;

            // The Ramp tool works in the volume lane; in the grid it selects like Select.
            case Tool.Select when under is not null && e.ClickCount == 2 && e.KeyModifiers == KeyModifiers.None:
                // Double-click: the playhead moves to the hit and it plays (playback doesn't start).
                selection.Clear();
                selection.Add(under);
                SelectionChanged?.Invoke();
                SeekRequested?.Invoke(under.Time);
                HitsPreviewRequested?.Invoke([under]);
                break;

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
                    dragPreviewLane = under.Lane;
                    dragHits = [.. selection];
                    session.BeginGesture("Move");
                }

                SelectionChanged?.Invoke();
                break;

            case Tool.Select or Tool.Ramp:
                drag = Drag.Marquee;
                seekOnClick = tool == Tool.Select && !additive;
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

    // ---- the tool cursor (V6.39): arrow, pen (Draw or Alt), eraser or crosshair over the grid; the arrow elsewhere

    private Point? pointer;

    internal Lane? HoveredLane => pointer is { } p && p.X >= GridLeft && p.X < Bounds.Width && p.Y < Bounds.Height
        ? (RowAt(p.Y) as LaneRow)?.Lane : null;

    /// <summary>Shows the cursor for the tool at the pointer; the window calls it when Alt goes down or up.</summary>
    public void UpdateCursor(KeyModifiers modifiers)
    {
        if (drag == Drag.ResizeList || drag == Drag.None && pointer is { } divider && OnListEdge(divider))
        {
            Cursor = new Cursor(StandardCursorType.SizeWestEast);
            return;
        }
        bool overGrid = pointer is { } p && p.X >= GridLeft && p.Y >= HeaderHeight;
        // The strip's bottom edge: drag to make the waveform and objects taller or shorter.
        if (pointer is { } edge && edge.X >= GridLeft && Math.Abs(edge.Y - HeaderHeight) <= 4 && drag is Drag.None or Drag.ResizeWave)
        {
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
            return;
        }
        // Over the cycle range in the upper ruler: resize arrows at its ends, a hand inside it.
        if (pointer is { } q && q.X >= GridLeft && q.Y < SectionsHeight && drag is Drag.None or Drag.CycleLeft or Drag.CycleRight or Drag.CycleMove)
        {
            var kind = drag != Drag.None ? drag : CycleDragAt(q.X);
            if (kind is Drag.CycleLeft or Drag.CycleRight) { Cursor = new Cursor(StandardCursorType.SizeWestEast); return; }
            if (kind is Drag.CycleMove) { Cursor = new Cursor(StandardCursorType.Hand); return; }
        }
        Cursor = ToolCursors.Get(ToolCursors.For(Tool, modifiers, overGrid));
    }

    /// <summary>What a press at this x in the upper ruler does with the cycle range: resize an end, move it, or draw a new one.</summary>
    private Drag CycleDragAt(double x)
    {
        if (LocatorLeft is not { } l || LocatorRight is not { } r) return Drag.Cycle;
        double xl = GridLeft + Axis.X(l), xr = GridLeft + Axis.X(r);
        const double grip = 5;
        if (Math.Abs(x - xl) <= grip) return Drag.CycleLeft;
        if (Math.Abs(x - xr) <= grip) return Drag.CycleRight;
        return x > Math.Min(xl, xr) && x < Math.Max(xl, xr) ? Drag.CycleMove : Drag.Cycle;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        pointer = null;
        InvalidateVisual();
        base.OnPointerExited(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var previousHover = HoveredLane;
        pointer = e.GetPosition(this);
        if (drag == Drag.ResizeList)
        {
            NamesCollapsed = false;
            double maximum = Math.Clamp(Bounds.Width - NumberWidth - 160, 160, 600);
            double width = Math.Clamp(pointer.Value.X - NumberWidth, 160, maximum);
            UserListWidth = width; // only this view and the controls aligned to it redraw, not the whole theme
            InvalidateVisual();
            LaneListResizing?.Invoke();
            UpdateCursor(e.KeyModifiers);
            e.Handled = true;
            return;
        }
        string? warningTip = null;
        if (pointer is { } hover && hover.X >= GridLeft && hover.Y >= HeaderHeight - 12 && hover.Y < HeaderHeight)
        {
            var warnings = VisibleWarnings().Where(w => Math.Abs(GridLeft + Axis.X(w.Time) - hover.X) <= 7).ToList();
            if (warnings.Count > 0) warningTip = string.Join("\n", warnings.Take(8).Select(w => $"{w.Time:0.###} ms: {w.Reason}")) + (warnings.Count > 8 ? $"\n…and {warnings.Count - 8} more." : "");
        }
        if (warningTip is null && pointer is { } bookmarkHover && bookmarkHover.X >= GridLeft && bookmarkHover.Y >= SectionsHeight && bookmarkHover.Y < RulerBottom
            && Session?.NearestBookmark(Axis.Time(bookmarkHover.X - GridLeft), 7 / Axis.Zoom) is { } bookmark)
            warningTip = (Session.BookmarkLabel(bookmark) is { Length: > 0 } label ? label + " · " : "Bookmark · ") + $"{bookmark:0} ms";
        ToolTip.SetTip(this, warningTip);
        if (previousHover != HoveredLane) InvalidateVisual();
        UpdateCursor(e.KeyModifiers);
        if (drag == Drag.None || Model is not { } model || Session is not { } session) return;
        var p = e.GetPosition(this);
        dragNow = p;
        double gridX = p.X - GridLeft;

        switch (drag)
        {
            case Drag.Pan: Axis.Left = panLeft - (p.X - dragStart.X) / Axis.Zoom; ScrollY = panScroll - (p.Y - dragStart.Y); InvalidateVisual(); break;
            case Drag.Layout:
                layoutMoved |= Math.Abs(p.Y - dragStart.Y) >= DragThreshold || Math.Abs(p.X - dragStart.X) >= DragThreshold;
                if (p.Y < HeaderHeight + 12) ScrollY -= RowHeight;
                else if (p.Y > Bounds.Height - 12) ScrollY += RowHeight;
                UpdateLaneDrop(p);
                break;
            case Drag.Paint when paintRow is not null:
            {
                if (SnapOn && lastPaintX is { } last)
                {
                    double step = Math.Sign(gridX - last);
                    double placed = double.NaN;
                    for (double x = last; step != 0 && (x - gridX) * step < 0; x += step)
                        if (SnapTime(Axis.Time(x)) is var t && t != placed) PlaceHit(paintRow.Lane, placed = t);
                }

                PlaceHit(paintRow.Lane, SnapTime(Axis.Time(gridX)));
                lastPaintX = gridX;
                break;
            }

            case Drag.Wipe:
            {
                var row = RowAt(p.Y) as LaneRow;
                if (row is not null && (wipeLane is null || row.Lane == wipeLane) && HitUnder(row, gridX) is { } under) EraseHit(under);
                break;
            }

            case Drag.Move when dragAnchor is not null:
            {
                // The grabbed hit snaps; the others keep their offsets to it. Rows move by whole visible lanes.
                double offset = Edits.SnapOffset(dragAnchor.Time, Axis.Time(gridX) - Axis.Time(dragStart.X - GridLeft));
                int rowShift = RowIndexAt(p.Y) - RowIndexAt(dragStart.Y);
                Lane Destination(EditHit h) => model.DrumMapMode ? model.ShiftDrumLane(h.Lane, rowShift, h.Lane == dragAnchor.Lane ? RowIndexAt(dragStart.Y) : null) : HitEditor.ShiftLane(model, h.Lane, rowShift);
                session.ResetToGestureStart();
                selection.Clear();
                selection.UnionWith(session.Move(dragHits, offset, Destination));
                var destination = Destination(dragAnchor);
                if (destination != dragPreviewLane)
                {
                    dragPreviewLane = destination;
                    PreviewLaneMove(selection.DistinctBy(h => h.Lane).ToList());
                }
                break;
            }

            case Drag.Range when Math.Abs(p.X - dragStart.X) >= DragThreshold:
                Edits.MarkRange(Axis.Time(dragStart.X - GridLeft), Axis.Time(gridX), RowIndexAt(dragStart.Y), RowIndexAt(p.Y));
                break;

            case Drag.Marquee:
            {
                if (Math.Abs(p.X - dragStart.X) >= DragThreshold || Math.Abs(p.Y - dragStart.Y) >= DragThreshold)
                    seekOnClick = false;
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

            case Drag.CycleMove when Math.Abs(p.X - dragStart.X) >= DragThreshold:
            {
                // The whole range moves by whole snap steps, keeping its length; it stops at the song's start.
                var (l0, r0) = cycleAtStart;
                double moved = SnapTime(l0 + Axis.Time(gridX) - Axis.Time(dragStart.X - GridLeft)) - l0;
                moved = Math.Max(moved, -Math.Min(l0, r0));
                SetLocators(l0 + moved, r0 + moved);
                break;
            }

            case Drag.CycleLeft:
                SetLocators(SnapTime(Math.Max(0, Axis.Time(gridX))), cycleAtStart.Right);
                break;

            case Drag.CycleRight:
                SetLocators(cycleAtStart.Left, SnapTime(Math.Max(0, Axis.Time(gridX))));
                break;

            case Drag.Cycle when Math.Abs(p.X - dragStart.X) >= DragThreshold:
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
        if (drag == Drag.ResizeList)
        {
            drag = Drag.None;
            e.Pointer.Capture(null);
            LaneListResized?.Invoke();
            e.Handled = true;
            return;
        }
        if (drag == Drag.None) return;
        if (drag == Drag.Layout && Model is { } layout)
        {
            UpdateLaneDrop(e.GetPosition(this));
            if (layout.DrumMapMode && draggedRow is { } drumRow && dropRow is { } drumTarget) layout.MoveDrumRow(drumRow, drumTarget, dropAfter);
            else if (draggedRow is LaneRow lane && dropRow is LaneRow target) layout.MoveLaneTo(lane.Lane, target.Lane, dropAfter);
            else if (draggedRow is GroupRow folder && dropRow is { } destination) layout.MoveFolderTo(folder.Group, destination.Group, dropAfter);
            draggedRow = dropRow = null;
            laneDropY = null;
        }
        if (drag == Drag.Marquee && seekOnClick)
        {
            var released = e.GetPosition(this);
            if (Math.Abs(released.X - dragStart.X) < DragThreshold && Math.Abs(released.Y - dragStart.Y) < DragThreshold)
                SeekRequested?.Invoke(SnapTime(Math.Max(0, Axis.Time(dragStart.X - GridLeft))));
        }
        if (drag is Drag.Cycle or Drag.CycleMove && Math.Abs(dragNow.X - dragStart.X) < DragThreshold && LocatorLeft is { } l && LocatorRight is { } r)
        {
            // A click (no drag) inside the band toggles cycling.
            double t = Axis.Time(dragStart.X - GridLeft);
            if (t >= Math.Min(l, r) && t <= Math.Max(l, r))
            {
                Cycle = !Cycle; // raises LocatorsChanged
            }
        }

        if (drag == Drag.Range && Math.Abs(dragNow.X - dragStart.X) < DragThreshold) Edits.ClearRange();

        bool finishedSetStroke = drag == Drag.Paint;
        if (drag != Drag.Pan) Session?.EndGesture();
        drag = Drag.None;
        dragAnchor = null;
        paintRow = null;
        e.Pointer.Capture(null);
        SelectionChanged?.Invoke();
        InvalidateVisual();
        if (finishedSetStroke) SoundSetStrokeFinished?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        bool resizedList = drag == Drag.ResizeList;
        bool finishedSetStroke = drag == Drag.Paint;
        if (drag is not Drag.Pan and not Drag.ResizeList) Session?.EndGesture();
        drag = Drag.None;
        draggedRow = dropRow = null;
        laneDropY = null;
        if (finishedSetStroke) SoundSetStrokeFinished?.Invoke();
        if (resizedList) LaneListResized?.Invoke();
        InvalidateVisual();
        base.OnPointerCaptureLost(e);
    }

    private IEnumerable<EditHit> HitsIn(Rect area)
    {
        if (Model is not { } model) yield break;
        // A hit counts when its diamond overlaps the box: zoomed out, a diamond spans far more time than its own instant.
        double radius = NoteSize / 2;
        double from = Axis.Time(area.Left - GridLeft - radius), to = Axis.Time(area.Right - GridLeft + radius);
        for (int i = 0; i < model.Rows.Count; i++)
        {
            double y = HeaderHeight + RowTop(i) - scrollY + HeightOf(model.Rows[i]) / 2;
            double r = Math.Min(radius, HeightOf(model.Rows[i]) / 2);
            if (y + r < area.Top || y - r > area.Bottom || model.Rows[i] is not LaneRow lane || !model.Notes.TryGetValue(lane.Lane, out var notes)) continue;
            foreach (var n in notes)
                if (n.Hit is { } hit && n.Time >= from && n.Time <= to) yield return hit;
        }
    }


    /// <summary>Double-clicking an object asks to hear its hits (at their moments' volumes).</summary>
    public event Action<IReadOnlyList<EditHit>>? HitsPreviewRequested;

    /// <summary>
    /// A click in the object strip: moves the playhead to the object (or the slider node clicked) and selects its hits;
    /// a click on a slider's body selects the hits of all its nodes. A double-click also plays them.
    /// </summary>
    private void ObjectPressed(ArrangeModel model, EditSession session, double time, bool doubleClick)
    {
        // Objects can be selected here (their hits), not moved or edited: they belong to the beatmap.
        double reach = ObjectRadius / Axis.Zoom;
        if (model.ObjectAt(time, reach) is not { } o) return;
        var node = o.Nodes.MinBy(n => Math.Abs(n.Time - time));
        bool onNode = Math.Abs(node.Time - time) <= reach; // on a circle: that node; on a slider's body: the whole slider
        var keys = onNode ? [node.Key] : o.Nodes.Select(n => n.Key).ToHashSet();
        var hits = session.State.Hits.Where(h => h.Anchor is { } k && keys.Contains(k)).ToList();
        SetSelection(hits);
        SeekRequested?.Invoke(onNode ? node.Time : o.Start);
        if (doubleClick) HitsPreviewRequested?.Invoke(hits);
    }
    /// <summary>A right-click on sliders in the object strip: change what their bodies play (the slide, its whistle, ticks).</summary>
    public event Action<IReadOnlyList<int>>? SliderMenuRequested;

    // On a slider that's selected whole: all such sliders; on another slider: that one, selected first.
    private void SliderPressed(ArrangeModel model, EditSession session, double time)
    {
        if (model.ObjectAt(time, ObjectRadius / Axis.Zoom) is not { Kind: HitObjectKind.Slider } slider || slider.Nodes[0].Key.ObjectIndex < 0) return;
        var keys = slider.Nodes.Select(n => n.Key).ToHashSet();
        CollectSelectedObjects(model);
        if (!slider.Nodes.All(n => selectedKeys.Contains(n.Key)))
        {
            SetSelection(session.State.Hits.Where(h => h.Anchor is { } k && keys.Contains(k)));
            selectedKeys = keys;
        }

        var sliders = model.Objects.Where(o => o.Kind == HitObjectKind.Slider && o.Nodes.Count > 0 && o.Nodes[0].Key.ObjectIndex >= 0
            && o.Nodes.All(n => selectedKeys.Contains(n.Key))).Select(o => o.Nodes[0].Key.ObjectIndex).ToList();
        InvalidateVisual();
        SliderMenuRequested?.Invoke(sliders.Count > 0 ? sliders : [slider.Nodes[0].Key.ObjectIndex]);
    }

    // ---------------------------------------------------------------- selection (the commands are in HitEditor)

    private void SetSelection(IEnumerable<EditHit> hits) => Edits.Select(hits);

    private void NotifySelection() => Edits.Notify();

    private void EraseHit(EditHit hit) => Edits.Erase(hit);
}

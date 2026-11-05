using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

internal enum KeyTool { Select, Draw, Erase }

/// <summary>
/// Keysound mode's key editor, after Cubase's Key Editor: a ruler, a strip with the reference's gameplay objects over the
/// song waveform, a piano keyboard beside the note grid, and a velocity lane. It edits the selected track of a
/// <see cref="KeysoundSession"/>; other tracks can show as ghost notes and are never edited here.
/// </summary>
internal sealed class PianoRollView : Avalonia.Controls.Control
{
    public const double KeyboardWidth = 64, RulerHeight = 24, StripHeight = 46, VelocityHeight = 88, MinRow = 6, MaxRow = 30;
    private const double EdgeGrab = 6, DragThreshold = 3;
    private const int DefaultVelocity = 100;

    private static readonly IBrush WhiteRow = Paint.Brush("#26272b"), BlackRow = Paint.Brush("#1d1e21");
    private static readonly IBrush WhiteKey = Paint.Brush("#e8e8ea"), BlackKey = Paint.Brush("#1a1a1c"), KeyText = Paint.Brush("#55565c");
    private static readonly IBrush Ghost = Paint.Brush(Color.Parse("#8a8f99"), 0.28), WaveFill = Paint.Brush(Color.Parse("#5b6b80"), 0.45);
    private static readonly IBrush BoxFill = Paint.Brush(Colors.White, 0.08), SpinnerFill = Paint.Brush(Color.Parse("#c7c9d1"), 0.55);
    private static readonly IPen MarkerPen = new ImmutablePen(Paint.Brush(Color.Parse("#ff66aa"), 0.24), 1);
    private static readonly IPen RowLine = new ImmutablePen(Paint.Brush(Colors.Black, 0.35), 1), OctaveLine = new ImmutablePen(Paint.Brush(Colors.Black, 0.7), 1);
    private static readonly IPen NoteEdge = new ImmutablePen(Paint.Brush(Colors.Black, 0.55), 1);
    private static readonly IPen WhiteRing = new ImmutablePen(Brushes.White, 1.5); // selected notes and object heads
    private static readonly IPen BoxPen = new ImmutablePen(Paint.Brush(Colors.White, 0.6), 1, new ImmutableDashStyle([3, 3], 0));
    // osu!'s default combo colours, for references that set none.
    private static readonly Color[] DefaultCombo = [Color.Parse("#ffc000"), Color.Parse("#00ca00"), Color.Parse("#127cff"), Color.Parse("#f21839")];
    private static readonly Color Soft = Color.Parse("#3a6cf0"), Middle = Color.Parse("#9a48d8"), Loud = Color.Parse("#ec3a3a");
    private static readonly IBrush[] VelocityFills = [.. Enumerable.Range(0, 128).Select(v => (IBrush)Paint.Brush(VelocityColor(v)))];
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);

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
    public int SnapDivisions { get; set; } = 4; // beats are split into this many grid steps
    public bool ShowGhosts { get; set; } = true;
    public bool ShowMarkers { get; set; } = true;

    /// <summary>Clicks on the ruler or strip move the playhead.</summary>
    public event Action<double>? SeekRequested;
    /// <summary>Selection, tool or view changes the editor shows (not edits: those come from the session).</summary>
    public event Action? ViewChanged;

    public PianoRollView()
    {
        Focusable = true;
        ClipToBounds = true;
        Axis.Changed += InvalidateVisual;
    }

    private KeysoundSession? Session => doc?.Session;
    private KeyTrack? Track => doc?.SelectedTrack is { } id ? Session?.State.Track(id) : null;
    private Rect Grid => new(KeyboardWidth, RulerHeight + StripHeight, Math.Max(0, Bounds.Width - KeyboardWidth), Math.Max(0, Bounds.Height - RulerHeight - StripHeight - VelocityHeight));
    private Rect VelocityArea => new(KeyboardWidth, Bounds.Height - VelocityHeight, Math.Max(0, Bounds.Width - KeyboardWidth), VelocityHeight);

    public void Attach(KeysoundDocument? document)
    {
        if (doc is not null) doc.Session.Changed -= OnSessionChanged;
        doc = document;
        selection.Clear();
        drag = null;
        if (doc is not null)
        {
            doc.Session.Changed += OnSessionChanged;
            Axis.End = Math.Max(doc.Objects.Count > 0 ? doc.Objects[^1].End + 2000 : 0, 60_000);
            Axis.Zoom = 0.45; // about 2.2 s per 1000 px, so objects and notes are readable from the start
            Axis.Left = (doc.Objects.Count > 0 ? doc.Objects[0].Start : doc.Tempo.Segments[0].Time) - 500;
            objectLookBack = doc.Objects.Select(o => o.End - o.Start).DefaultIfEmpty(0).Max();
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
        // Undo may remove notes that are selected.
        if (Track is { } t) selection.IntersectWith(t.Notes.Select(n => n.Id));
        else selection.Clear();
        InvalidateVisual();
    }

    protected override void OnSizeChanged(Avalonia.Controls.SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Axis.Width = Grid.Width;
    }

    // Coordinates.

    private double XOfTime(double time) => KeyboardWidth + Axis.X(time);
    private double XOfTick(double tick) => XOfTime(doc!.Tempo.TimeAt(tick));
    private double TimeOfX(double x) => Axis.Time(x - KeyboardWidth);
    private double YOfPitch(double pitch) => Grid.Top + (topPitch - pitch) * rowHeight;
    private int PitchOfY(double y) => Math.Clamp((int)Math.Floor(topPitch - (y - Grid.Top) / rowHeight) + 1, KeyNote.MinPitch, KeyNote.MaxPitch);

    private static bool IsBlack(int pitch) => (pitch % 12) is 1 or 3 or 6 or 8 or 10;
    // Cubase's names: middle C (60) is C3.
    private static string PitchName(int pitch) => $"{NoteNames[pitch % 12]}{pitch / 12 - 2}";

    /// <summary>Velocity colour, as in Cubase's default: blue (soft) through purple to red (loud).</summary>
    private static Color VelocityColor(int velocity)
    {
        double v = Math.Clamp((velocity - 1) / 126.0, 0, 1);
        (Color a, Color b, double t) = v < 0.5 ? (Soft, Middle, v * 2) : (Middle, Loud, (v - 0.5) * 2);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    private static IBrush VelocityFill(int velocity) => VelocityFills[Math.Clamp(velocity, 0, 127)];

    private (long From, long To) TickRange(double from, double to) =>
        ((long)Math.Floor(doc!.Tempo.TickAt(from)), (long)Math.Ceiling(doc.Tempo.TickAt(to)));

    /// <summary>The pitches with a row on screen, top to bottom, with each row's top edge.</summary>
    private IEnumerable<(int Pitch, double Y)> VisiblePitches()
    {
        int top = Math.Min(KeyNote.MaxPitch, (int)Math.Ceiling(topPitch)), bottom = Math.Max(KeyNote.MinPitch, PitchOfY(Grid.Bottom) - 1);
        for (int p = top; p >= bottom; p--) yield return (p, YOfPitch(p));
    }

    private double SnapTime(double time) => SnapOn ? doc!.Tempo.Snap(time, 1, SnapDivisions) : time;

    /// <summary>
    /// The grid cell's start at or before a time: where Draw puts a note. Ticks count beats, so one step is the same number
    /// of ticks under every red line.
    /// </summary>
    private double FloorSnap(double time)
    {
        if (!SnapOn) return time;
        double snapped = doc!.Tempo.Snap(time, 1, SnapDivisions);
        return snapped <= time + 0.01 ? snapped : doc.Tempo.Snap(time - doc.Tempo.SegmentAtTime(time).BeatLength / SnapDivisions, 1, SnapDivisions);
    }

    private long StepTicks => Math.Max(1, TempoMap.Ppq / SnapDivisions);

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
                ctx.DrawLine(pen, new Point(x, VelocityArea.Top), new Point(x, VelocityArea.Bottom));
            }

            if (ShowMarkers)
                foreach (var o in objects)
                    foreach (var (time, _) in o.Nodes)
                    {
                        double x = Math.Round(XOfTime(time)) + 0.5;
                        ctx.DrawLine(MarkerPen, new Point(x, grid.Top), new Point(x, grid.Bottom));
                    }
        }

        using (ctx.PushClip(grid)) DrawNotes(ctx, ticks);
        DrawKeyboard(ctx, grid);
        DrawRuler(ctx, lines, b.Width);
        DrawStrip(ctx, objects, b.Width);
        DrawVelocity(ctx, ticks);

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
        int d = SnapDivisions;
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
        foreach (var (p, y) in VisiblePitches())
        {
            ctx.FillRectangle(IsBlack(p) ? BlackRow : WhiteRow, new Rect(grid.Left, y, grid.Width, rowHeight));
            ctx.DrawLine(p % 12 == 0 ? OctaveLine : RowLine, new Point(grid.Left, y + rowHeight - 0.5), new Point(grid.Right, y + rowHeight - 0.5));
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
        Paint.DrawText(ctx, "Velocity", new Point(6, VelocityArea.Top + 4), 10, Paint.Dim, KeyboardWidth - 8);
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

        if (Track is not { } track) return;
        foreach (var n in track.Notes)
        {
            if (n.End < fromTick || n.Start > toTick) continue;
            var r = NoteRect(n);
            bool selected = selection.Contains(n.Id);
            ctx.DrawRectangle(VelocityFill(n.Velocity), selected ? WhiteRing : NoteEdge, r, 1.5, 1.5);
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
                if (level == 0 && x - lastLabel > 34)
                {
                    Paint.DrawText(ctx, doc!.Tempo.BarBeatAt(time).Bar.ToString(), new Point(x + 3, 3), 10, Paint.Text, 0);
                    lastLabel = x;
                }
            }
        }

        ctx.DrawLine(Paint.DividerPen, new Point(KeyboardWidth, RulerHeight - 0.5), new Point(width, RulerHeight - 0.5));
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
    private StreamGeometry? WaveGeometry(Rect area, double mid)
    {
        if (Wave is not { } wave) return null;
        var key = (Axis.Left, Axis.Zoom, area.Width, wave);
        if (waveGeometry is not null && waveKey == key) return waveGeometry;
        double msPerPx = 1 / Axis.Zoom, half = area.Height / 2 - 2;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
            for (double x = area.Left; x < area.Right; x++)
            {
                double t = TimeOfX(x);
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

    private enum DragKind { Move, Resize, Draw, Box, Erase, Velocity }

    private sealed class Drag(DragKind kind, Point start)
    {
        public DragKind Kind { get; } = kind;
        public Point Start { get; } = start;
        public Point Current { get; set; } = start;
        public bool Moved { get; set; }
        public Dictionary<Guid, KeyNote> Originals { get; } = [];
        public KeyNote? Anchor { get; set; } // the note under the pointer
        public HashSet<Guid> BoxBase { get; } = [];
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

        if (p.Y < RulerHeight + StripHeight && p.X >= KeyboardWidth)
        {
            SeekRequested?.Invoke(Math.Max(0, TimeOfX(p.X)));
            e.Handled = true;
            return;
        }

        if (Track is not { } track || p.X < KeyboardWidth) return;
        var session = doc.Session;

        if (VelocityArea.Contains(p))
        {
            if (VelocityNoteAt(p) is not { } vn) return;
            if (!selection.Contains(vn.Id)) { selection.Clear(); selection.Add(vn.Id); }
            StartGesture(DragKind.Velocity, p, vn, track, "Velocity");
            ApplyDrag(p, mods);
            e.Pointer.Capture(this);
            e.Handled = true;
            ViewChanged?.Invoke();
            return;
        }

        if (!Grid.Contains(p)) return;
        var hit = NoteAt(p);

        if (point.IsRightButtonPressed)
        {
            if (hit is not null) RemoveNote(track, hit, "Delete note");
            e.Handled = true;
            return;
        }

        var tool = mods.HasFlag(KeyModifiers.Alt) ? KeyTool.Draw : Tool;
        switch (tool)
        {
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
                var note = new KeyNote(Guid.NewGuid(), startTick, StepTicks, PitchOfY(p.Y), LastVelocity());
                selection.Clear();
                selection.Add(note.Id);
                StartGesture(DragKind.Draw, p, note, track, "Draw note");
                session.AddNotes(track.Id, [note]);
                break;
            }

            case KeyTool.Select when hit is not null:
            {
                if (mods.HasFlag(KeyModifiers.Control)) { if (!selection.Remove(hit.Id)) selection.Add(hit.Id); }
                else if (mods.HasFlag(KeyModifiers.Shift)) selection.Add(hit.Id);
                else if (!selection.Contains(hit.Id)) { selection.Clear(); selection.Add(hit.Id); }
                if (!selection.Contains(hit.Id)) break;
                bool resize = OnRightEdge(hit, p);
                StartGesture(resize ? DragKind.Resize : DragKind.Move, p, hit, track, resize ? "Resize notes" : "Move notes");
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
        foreach (var n in track.Notes.Where(n => selection.Contains(n.Id))) drag.Originals[n.Id] = n;
        drag.Originals[anchor.Id] = anchor;
        doc!.Session.BeginGesture(label);
    }

    private void RemoveNote(KeyTrack track, KeyNote note, string label)
    {
        doc!.Session.RemoveNotes(track.Id, new HashSet<Guid> { note.Id }, label);
        selection.Remove(note.Id);
    }

    // The resize handle: the last few pixels, at most a quarter of a short note so it can still be moved.
    private bool OnRightEdge(KeyNote n, Point p) => NoteRect(n) is var r && p.X >= r.Right - Math.Min(EdgeGrab, r.Width / 4);

    private int LastVelocity() => Track?.Notes.Where(n => selection.Contains(n.Id)).Select(n => n.Velocity).DefaultIfEmpty(DefaultVelocity).First() ?? DefaultVelocity;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (drag is null)
        {
            Cursor = Tool == KeyTool.Select && NoteAt(p) is { } n && OnRightEdge(n, p) ? ResizeCursor : Cursor.Default;
            return;
        }

        ApplyDrag(p, e.KeyModifiers);
    }

    private void ApplyDrag(Point p, KeyModifiers mods)
    {
        if (drag is null || doc is null || Track is not { } track) return;
        drag.Current = p;
        if (!drag.Moved && Math.Abs(p.X - drag.Start.X) + Math.Abs(p.Y - drag.Start.Y) < DragThreshold && drag.Kind is DragKind.Move or DragKind.Resize) return;
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

            case DragKind.Move when drag.Anchor is { } anchor:
            {
                // The anchor note's start follows the pointer (snapped); the others keep their distance to it.
                double anchorTime = tempo.TimeAt(anchor.Start);
                double target = SnapTime(anchorTime + (TimeOfX(p.X) - TimeOfX(drag.Start.X)));
                long dt = tempo.RoundTick(target) - anchor.Start;
                int dp = (int)Math.Round((drag.Start.Y - p.Y) / rowHeight);
                if (mods.HasFlag(KeyModifiers.Control)) // Ctrl keeps the move to one direction, whichever is larger
                {
                    if (Math.Abs(p.X - drag.Start.X) > Math.Abs(p.Y - drag.Start.Y)) dp = 0; else dt = 0;
                }

                int lowest = drag.Originals.Values.Min(n => n.Pitch), highest = drag.Originals.Values.Max(n => n.Pitch);
                dp = Math.Clamp(dp, KeyNote.MinPitch - lowest, KeyNote.MaxPitch - highest);
                session.UpdateNotes(track.Id, [.. drag.Originals.Values.Select(n => n with { Start = n.Start + dt, Pitch = n.Pitch + dp })], "Move notes");
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
        var mods = e.KeyModifiers;
        if (mods.HasFlag(KeyModifiers.Control) && mods.HasFlag(KeyModifiers.Shift))
            ZoomRows(delta > 0 ? 1.15 : 1 / 1.15, p.Y);
        else if (mods.HasFlag(KeyModifiers.Control))
            Axis.ZoomAt(delta > 0 ? 1.25 : 0.8, Math.Max(0, p.X - KeyboardWidth));
        else if (mods.HasFlag(KeyModifiers.Shift) || e.Delta.X != 0)
            Axis.Left -= delta * 120 / Axis.Zoom;
        else
            ScrollPitch(delta * 3);
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
        var notes = t.Notes.Where(n => selection.Contains(n.Id)).ToList();
        semitones = Math.Clamp(semitones, KeyNote.MinPitch - notes.Min(n => n.Pitch), KeyNote.MaxPitch - notes.Max(n => n.Pitch));
        if (semitones == 0) return;
        doc!.Session.UpdateNotes(t.Id, [.. notes.Select(n => n with { Pitch = n.Pitch + semitones })], "Transpose");
    }

    /// <summary>Moves the selected notes by whole grid steps.</summary>
    public void Nudge(int steps)
    {
        if (Track is not { } t || selection.Count == 0) return;
        long dt = steps * StepTicks;
        doc!.Session.UpdateNotes(t.Id, [.. t.Notes.Where(n => selection.Contains(n.Id)).Select(n => n with { Start = n.Start + dt })], "Move notes");
    }

    public string SelectionText()
    {
        if (Track is not { } t || selection.Count == 0) return "No notes selected";
        var notes = t.Notes.Where(n => selection.Contains(n.Id)).ToList();
        if (notes.Count != 1) return $"{notes.Count} notes selected";
        var n = notes[0];
        var (bar, beat) = doc!.Tempo.BarBeatAt(doc.Tempo.TimeAt(n.Start));
        return $"{PitchName(n.Pitch)} · bar {bar}.{beat} · {n.Length / (double)TempoMap.Ppq:0.###} beats · velocity {n.Velocity}";
    }
}

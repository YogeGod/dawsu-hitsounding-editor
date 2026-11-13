using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// The map preview: osu!'s playfield with the objects on screen at the playhead, drawn as osu!'s editor shows them
/// (circles with approach circles, slider bodies with their ball, ticks and reverse arrows, spinners) and nothing
/// else. It only reads the map; it follows <see cref="Playhead"/>.
/// </summary>
internal sealed class PlayfieldView : Control
{
    /// <summary>How long an object stays, fading, after its time (the editor keeps hit objects visible a moment).</summary>
    private const double FadeOut = 700; // lazer's editor: HitCircleOverlapMarker.FADE_OUT_EXTENSION

    // osu!'s 4:3 screen around the 512 × 384 playfield: objects near the edge still fit.
    private const double ScreenWidth = 640, ScreenHeight = 480;

    private static readonly IBrush Background = Paint.Brush("#0b0b0f");
    private static readonly IBrush FieldFill = Paint.Brush("#121218");
    private static readonly IPen FieldPen = new ImmutablePen(Paint.Brush(Colors.White, 0.14), 1);
    private static readonly IBrush Message = Paint.Brush("#9a9aa2");
    private static readonly IBrush TickBrush = Paint.Brush(Colors.White, 0.85);
    private static readonly ImmutableSolidColorBrush BallFill =Paint.Brush(Colors.White, 0.9);
    private static readonly IBrush SpinnerFill = Paint.Brush(Colors.White, 0.06);

    private readonly Dictionary<PlayfieldObject, Geometry> bodies = [];
    private readonly Dictionary<(int Combo, double Size), FormattedText> numbers = [];
    private Playfield? field;
    private IReadOnlyList<Color> colours = [Colors.Orange];
    private double playhead;

    public PlayfieldView() => ClipToBounds = true;

    /// <summary>The time shown, in editor ms.</summary>
    public double Playhead
    {
        get => playhead;
        set
        {
            if (playhead == value) return;
            playhead = value;
            InvalidateVisual();
        }
    }

    /// <summary>Shows a map's objects in its combo colours; null shows the "no map" message.</summary>
    public void SetMap(Playfield? map, IReadOnlyList<Color>? comboColours)
    {
        field = map;
        if (comboColours is { Count: > 0 }) colours = comboColours;
        bodies.Clear();
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        ctx.FillRectangle(Background, bounds);
        if (bounds.Width < 40 || bounds.Height < 30) return;

        double scale = Math.Min(bounds.Width / ScreenWidth, bounds.Height / ScreenHeight);
        var origin = new Point((bounds.Width - Playfield.Width * scale) / 2, (bounds.Height - Playfield.Height * scale) / 2);
        ctx.DrawRectangle(FieldFill, FieldPen, new Rect(origin, new Size(Playfield.Width * scale, Playfield.Height * scale)));

        if (field is null) { DrawMessage(ctx, bounds, "Open a map to see its objects here."); return; }
        if (field.Mode != 0) { DrawMessage(ctx, bounds, "The map preview shows osu!standard maps only."); return; }

        double time = playhead;
        // Later objects first, so the next one to hit is on top, as in osu!.
        var visible = field.Between(time - FadeOut, time + field.Preempt).Reverse().ToList();
        foreach (var o in visible)
        {
            double alpha = Alpha(o, time);
            if (alpha <= 0) continue;
            using (ctx.PushOpacity(alpha))
                switch (o.Kind)
                {
                    case HitObjectKind.Slider: DrawSlider(ctx, o, time, scale, origin); break;
                    case HitObjectKind.Spinner: DrawSpinner(ctx, o, time, scale, origin); break;
                    default: DrawHead(ctx, o, time, scale, ToScreen(o.Position, o, scale, origin)); break;
                }
        }

        // Approach circles over everything.
        foreach (var o in visible)
        {
            if (o.Kind == HitObjectKind.Spinner || time >= o.Start) continue;
            double appeared = time - (o.Start - field.Preempt);
            if (appeared <= 0) continue;
            double size = 1 + 3 * (o.Start - time) / field.Preempt;
            double radius = field.Radius * scale;
            var pen = new ImmutablePen(Paint.Brush(Colour(o), Math.Min(1, appeared / Math.Min(field.FadeIn * 2, field.Preempt)) * 0.9), Math.Max(1.5, radius * 0.09));
            ctx.DrawEllipse(null, pen, ToScreen(o.Position, o, scale, origin), radius * size, radius * size);
        }
    }

    // Fading in from when the object appears; whole until it ends; then fading away.
    private double Alpha(PlayfieldObject o, double time)
    {
        if (time > o.End) return Math.Clamp(1 - (time - o.End) / FadeOut, 0, 1);
        return Math.Clamp((time - (o.Start - field!.Preempt)) / field.FadeIn, 0, 1);
    }

    private Color Colour(PlayfieldObject o) => colours[o.Colour % colours.Count];

    private static Point ToScreen(PlayfieldPoint p, PlayfieldObject o, double scale, Point origin) =>
        new(origin.X + (p.X - o.StackOffset) * scale, origin.Y + (p.Y - o.StackOffset) * scale);

    // A hit circle (or a slider's head): whole until its time, then fading faster than the rest of its object.
    private void DrawHead(DrawingContext ctx, PlayfieldObject o, double time, double scale, Point centre)
    {
        double radius = field!.Radius * scale;
        double alpha = o.Kind == HitObjectKind.Slider && time > o.Start ? Math.Clamp(1 - (time - o.Start) / 250, 0, 1) : 1;
        if (alpha <= 0) return;
        using (ctx.PushOpacity(alpha))
            DrawCircle(ctx, centre, radius, Colour(o), o.Combo);
    }

    private void DrawCircle(DrawingContext ctx, Point centre, double radius, Color colour, int combo)
    {
        if (SkinTextures.Loaded)
        {
            SkinTextures.DrawCircle(ctx, centre, radius, colour, combo);
            return;
        }

        ctx.DrawEllipse(Paint.Brush(colour), new ImmutablePen(Brushes.White, Math.Max(1.5, radius * 0.11)), centre, radius * 0.94, radius * 0.94);
        if (combo <= 0 || radius < 6) return;
        var key = (combo, Math.Round(radius));
        if (!numbers.TryGetValue(key, out var text))
        {
            if (numbers.Count > 400) numbers.Clear(); // sizes change while the window is resized
            numbers[key] = text = Paint.Format(combo.ToString(System.Globalization.CultureInfo.InvariantCulture), radius * 0.95, Brushes.White, Paint.UiBold);
        }

        ctx.DrawText(text, new Point(centre.X - text.Width / 2, centre.Y - text.Height / 2));
    }

    private void DrawSlider(DrawingContext ctx, PlayfieldObject o, double time, double scale, Point origin)
    {
        double radius = field!.Radius;
        var colour = Colour(o);
        if (!bodies.TryGetValue(o, out var body)) bodies[o] = body = BodyGeometry(o);

        // The body in playfield units: a white border with the combo colour, darkened, inside it.
        var toScreen = Matrix.CreateTranslation(-o.StackOffset, -o.StackOffset) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y);
        using (ctx.PushOpacity(0.86))
        using (ctx.PushTransform(toScreen))
        {
            ctx.DrawGeometry(null, new ImmutablePen(Brushes.White, radius * 2 * 0.94, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), body);
            ctx.DrawGeometry(null, new ImmutablePen(Paint.Brush(Darken(colour, 0.5)), radius * 2 * 0.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), body);
        }

        Point Screen(PlayfieldPoint p) => ToScreen(p, o, scale, origin);
        double r = radius * scale;

        // Ticks still to come.
        foreach (double tick in o.TickTimes)
            if (tick > time) ctx.DrawEllipse(TickBrush, null, Screen(o.BallAt(tick)), r * 0.13, r * 0.13);

        // Reverse arrows at the ends the ball still turns round at: odd repeats at the far end, even ones at the head.
        bool farArrow = false, headArrow = false;
        for (int node = 1; node < o.Spans; node++)
        {
            if (o.Start + node * o.SpanDuration <= time) continue;
            if (node % 2 == 1) farArrow = true;
            else headArrow = true;
        }

        var path = o.Path;
        if (farArrow && path.Count > 1) DrawArrow(ctx, Screen(path[^1]), Screen(path[^2]), r);
        DrawHead(ctx, o, time, scale, Screen(o.Position));
        if (headArrow && path.Count > 1 && time > o.Start) DrawArrow(ctx, Screen(path[0]), Screen(path[1]), r);

        // The ball and its follow circle while the slider runs.
        if (time >= o.Start && time <= o.End)
        {
            var ball = Screen(o.BallAt(time));
            ctx.DrawEllipse(null, new ImmutablePen(Paint.Brush(Colors.White, 0.55), Math.Max(1.5, r * 0.07)), ball, r * 2.4, r * 2.4); // lazer's follow area
            ctx.DrawEllipse(Paint.Brush(colour), new ImmutablePen(BallFill, Math.Max(1.5, r * 0.12)), ball, r * 0.9, r * 0.9);
        }
    }

    private static Geometry BodyGeometry(PlayfieldObject o)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(new Point(o.Path[0].X, o.Path[0].Y), false);
        // A slider with no length is still drawn as a dot: a line needs two points.
        if (o.Path.Count == 1) g.LineTo(new Point(o.Path[0].X + 0.01, o.Path[0].Y));
        for (int i = 1; i < o.Path.Count; i++) g.LineTo(new Point(o.Path[i].X, o.Path[i].Y));
        g.EndFigure(false);
        return geometry;
    }

    // A chevron at an end of the slider, pointing back along its body.
    private static void DrawArrow(DrawingContext ctx, Point at, Point towards, double radius)
    {
        double angle = Math.Atan2(towards.Y - at.Y, towards.X - at.X);
        var pen = new ImmutablePen(Brushes.White, Math.Max(2, radius * 0.2), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (ctx.PushTransform(Matrix.CreateRotation(angle) * Matrix.CreateTranslation(at.X, at.Y)))
        {
            ctx.DrawLine(pen, new Point(-radius * 0.28, -radius * 0.42), new Point(radius * 0.3, 0));
            ctx.DrawLine(pen, new Point(radius * 0.3, 0), new Point(-radius * 0.28, radius * 0.42));
        }
    }

    // A spinner: its disc in the middle, with a ring that closes in as it runs out.
    private static void DrawSpinner(DrawingContext ctx, PlayfieldObject o, double time, double scale, Point origin)
    {
        var centre = new Point(origin.X + Playfield.Width / 2 * scale, origin.Y + Playfield.Height / 2 * scale);
        double outer = 150 * scale;
        ctx.DrawEllipse(SpinnerFill, new ImmutablePen(Paint.Brush(Colors.White, 0.8), Math.Max(2, 4 * scale)), centre, outer, outer);
        ctx.DrawEllipse(Brushes.White, null, centre, 5 * scale, 5 * scale);
        double left = o.End > o.Start ? Math.Clamp((o.End - time) / (o.End - o.Start), 0, 1) : 0;
        double ring = (12 + (150 - 12) * left) * scale;
        ctx.DrawEllipse(null, new ImmutablePen(Paint.Brush(Color.Parse("#7fd4ff"), 0.9), Math.Max(1.5, 2.5 * scale)), centre, ring, ring);
    }

    private static void DrawMessage(DrawingContext ctx, Rect bounds, string message)
    {
        var text = Paint.Format(message, 13, Message);
        text.MaxTextWidth = Math.Max(40, bounds.Width - 32);
        text.TextAlignment = TextAlignment.Center;
        ctx.DrawText(text, new Point(16, (bounds.Height - text.Height) / 2));
    }

    private static Color Darken(Color c, double amount) => Color.FromRgb((byte)(c.R * amount), (byte)(c.G * amount), (byte)(c.B * amount));
}

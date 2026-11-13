using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// The map preview: osu!'s playfield with the objects on screen at the playhead, drawn as osu!lazer's editor shows
/// them with its default (Argon) skin and nothing else. Sizes, colours and timings are lazer's
/// (osu.Game.Rulesets.Osu: Skinning/Argon, Objects/Drawables and the editor's SuppressHitAnimations); pieces are laid
/// out in lazer's own units, where a hit circle is 128 across. It only reads the map; it follows <see cref="Playhead"/>.
/// </summary>
internal sealed partial class PlayfieldView : Control
{
    // ArgonMainCirclePiece's sizes, for a 128-unit circle.
    private const double Diameter = 128;
    private const double BorderThickness = Diameter * 2 / 58;
    private const double GradientThickness = BorderThickness * 2.5;
    private const double OuterGradientSize = Diameter - BorderThickness * 4;
    private const double InnerGradientSize = OuterGradientSize - GradientThickness * 2;
    private const double InnerFillSize = InnerGradientSize - GradientThickness * 2;

    /// <summary>How long a hit object stays, fading to white, after its time in lazer's editor.</summary>
    private const double EditorFadeOut = 700;
    private const double SliderFadeOut = 240; // a slider's body after its end

    // osu!'s 4:3 screen around the 512 × 384 playfield (lazer's playfield is 0.8 of it): objects near the edge still fit.
    private const double ScreenWidth = 640, ScreenHeight = 480;

    /// <summary>lazer's Argon combo colours, used when the map has none of its own.</summary>
    public static readonly IReadOnlyList<Color> ArgonComboColours =
    [
        Color.FromRgb(241, 116, 0), Color.FromRgb(0, 241, 53), Color.FromRgb(0, 82, 241),
        Color.FromRgb(241, 0, 0), Color.FromRgb(232, 235, 0), Color.FromRgb(92, 0, 241),
    ];

    private static readonly IBrush Background = Paint.Brush("#0b0b0f");
    private static readonly IBrush FieldFill = Paint.Brush("#121218");
    private static readonly IPen FieldPen = new ImmutablePen(Paint.Brush(Colors.White, 0.14), 1);
    private static readonly IBrush Message = Paint.Brush("#9a9aa2");
    private static readonly Color SpinnerPink = Color.Parse("#FC618F");
    private static readonly RelativePoint Top = new(0.5, 0, RelativeUnit.Relative), Bottom = new(0.5, 1, RelativeUnit.Relative);

    // A circle's four fills in one accent colour: dark fill, outer gradient, inner gradient (the dark fill is used twice).
    private sealed record CircleBrushes(IImmutableBrush Fill, IImmutableBrush Outer, IImmutableBrush Inner, IImmutableBrush Ball);

    private readonly Dictionary<Color, CircleBrushes> brushes = [];
    private readonly Dictionary<PlayfieldObject, Geometry> bodies = [];
    private readonly Dictionary<int, FormattedText> numbers = [];
    private Playfield? field;
    private IReadOnlyList<Color> colours = ArgonComboColours;
    private double playhead;
    private LegacySkin? skin; // null draws lazer's Argon

    public PlayfieldView() => ClipToBounds = true;

    /// <summary>The skin objects are drawn with: an osu!stable-format skin, or null for lazer's Argon.</summary>
    public LegacySkin? Skin
    {
        get => skin;
        set
        {
            skin = value;
            legacyBodies.Clear();
            InvalidateVisual();
        }
    }

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
        colours = comboColours is { Count: > 0 } ? comboColours : ArgonComboColours;
        bodies.Clear();
        legacyBodies.Clear();
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
        var frame = new Frame(ctx, field, time, scale, origin);
        // Later objects first, so the next one to hit is on top, as in osu!.
        var visible = field.Between(time - EditorFadeOut, time + field.Preempt).Reverse().ToList();
        foreach (var o in visible)
            switch (o.Kind)
            {
                case HitObjectKind.Slider: DrawSlider(frame, o); break;
                case HitObjectKind.Spinner: DrawSpinner(frame, o); break;
                default: DrawCircle(frame, o); break;
            }

        // Approach circles over everything (lazer proxies them above the objects): 4× shrinking to 1× at the object's
        // time, fading in to 0.9.
        foreach (var o in visible)
        {
            if (o.Kind == HitObjectKind.Spinner || time >= o.Start) continue;
            double appeared = time - (o.Start - field.Preempt);
            if (appeared <= 0) continue;
            double size = 1 + 3 * (o.Start - time) / field.Preempt;
            using (ctx.PushOpacity(Math.Min(1, appeared / Math.Min(field.FadeIn * 2, field.Preempt)) * 0.9))
            using (frame.Object(o, o.Position))
            {
                // A legacy skin's approachcircle at its own size; Argon's is 128 wide, enlarged by 128/118 so its
                // ring meets the circle's edge.
                if (skin is not null) Sprite(ctx, skin.Get("approachcircle"), Colour(o), size: size);
                else if (ArgonPieces.HasApproachCircle) ArgonPieces.DrawApproachCircle(ctx, default, Diameter * (Diameter / 118) * size, Colour(o));
                else ctx.DrawEllipse(null, new ImmutablePen(Paint.Brush(Colour(o)), 4), default, Diameter / 2 * size - 2, Diameter / 2 * size - 2);
            }
        }
    }

    /// <summary>One frame's drawing state: where playfield points land on screen and how big a 128-unit circle is.</summary>
    private readonly record struct Frame(DrawingContext Ctx, Playfield Field, double Time, double Scale, Point Origin)
    {
        public Point Screen(PlayfieldObject o, PlayfieldPoint p) =>
            new(Origin.X + (p.X - o.StackOffset) * Scale, Origin.Y + (p.Y - o.StackOffset) * Scale);

        /// <summary>Draws in lazer's object units (a circle is 128 across) centred on a playfield point, optionally turned and resized.</summary>
        public DrawingContext.PushedState Object(PlayfieldObject o, PlayfieldPoint at, double rotation = 0, double size = 1)
        {
            var centre = Screen(o, at);
            double s = Field.Radius / (Diameter / 2) * Scale * size;
            return Ctx.PushTransform(Matrix.CreateRotation(rotation) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(centre.X, centre.Y));
        }

        public double FadeIn(PlayfieldObject o) => Math.Clamp((Time - (o.Start - Field.Preempt)) / Field.FadeIn, 0, 1);
    }

    private Color Colour(PlayfieldObject o) => colours[o.Colour % colours.Count];

    // lazer's Darken: every channel divided by 1 + amount.
    private static Color Darken(Color c, double amount) =>
        Color.FromRgb((byte)(c.R / (1 + amount)), (byte)(c.G / (1 + amount)), (byte)(c.B / (1 + amount)));

    private static double OutQuint(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 5);

    private static double OutQuad(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 2);

    private CircleBrushes Brushes(Color accent)
    {
        if (brushes.TryGetValue(accent, out var made)) return made;
        if (brushes.Count > 64) brushes.Clear();
        static IImmutableBrush Vertical(Color top, Color bottom) =>
            new ImmutableLinearGradientBrush([new ImmutableGradientStop(0, top), new ImmutableGradientStop(1, bottom)], startPoint: Top, endPoint: Bottom);
        return brushes[accent] = new(
            Paint.Brush(Darken(accent, 4)),
            Vertical(accent, Darken(accent, 0.1)),
            Vertical(Darken(accent, 0.5), Darken(accent, 0.6)),
            Vertical(accent, Darken(accent, 0.5)));
    }

    /// <summary>
    /// ArgonMainCirclePiece at the origin, in object units: dark fill, bright outer gradient, darker inner gradient,
    /// dark centre with the combo number in Torus Bold 52, and the white border ring on top.
    /// </summary>
    private void DrawArgonCircle(DrawingContext ctx, Color accent, int number, bool outerFill)
    {
        var b = Brushes(accent);
        if (outerFill) ctx.DrawEllipse(b.Fill, null, default, (Diameter - 1) / 2, (Diameter - 1) / 2);
        ctx.DrawEllipse(b.Outer, null, default, OuterGradientSize / 2, OuterGradientSize / 2);
        ctx.DrawEllipse(b.Inner, null, default, InnerGradientSize / 2, InnerGradientSize / 2);
        ctx.DrawEllipse(b.Fill, null, default, InnerFillSize / 2, InnerFillSize / 2);
        if (number > 0)
        {
            if (ArgonPieces.HasDigits) ArgonPieces.DrawNumber(ctx, new Point(0, -2), 52, number);
            else
            {
                if (!numbers.TryGetValue(number, out var text))
                {
                    if (numbers.Count > 400) numbers.Clear();
                    numbers[number] = text = Paint.Format(number.ToString(System.Globalization.CultureInfo.InvariantCulture), 44, Avalonia.Media.Brushes.White, Paint.UiBold);
                }

                ctx.DrawText(text, new Point(-text.Width / 2, -2 - text.Height / 2));
            }
        }

        Ring(ctx, Avalonia.Media.Brushes.White, Diameter, BorderThickness);
    }

    // A ring as lazer's masked border: the stroke lies inside the given diameter.
    private static void Ring(DrawingContext ctx, IImmutableBrush brush, double diameter, double thickness) =>
        ctx.DrawEllipse(null, new ImmutablePen(brush, thickness), default, (diameter - thickness) / 2, (diameter - thickness) / 2);

    // A circle, or a slider's head. In lazer's editor an object past its time turns white and fades over 700 ms.
    private void DrawHead(Frame f, PlayfieldObject o, CirclePiece piece) =>
        DrawPiece(f, o, o.Position, piece, o.Start, f.FadeIn(o));

    // A circle piece at a point: as given before its time, then white and fading over 700 ms.
    private void DrawPiece(Frame f, PlayfieldObject o, PlayfieldPoint at, CirclePiece piece, double hitTime, double alphaBefore)
    {
        bool passed = f.Time > hitTime;
        double alpha = passed ? Math.Clamp(1 - (f.Time - hitTime) / EditorFadeOut, 0, 1) : alphaBefore;
        if (alpha <= 0) return;
        var accent = passed ? Colors.White : Colour(o);
        using (f.Ctx.PushOpacity(alpha))
        using (f.Object(o, at))
        {
            if (skin is not null) DrawLegacyCircle(f.Ctx, skin, accent, o.Combo, piece);
            else if (piece != CirclePiece.SliderEnd) DrawArgonCircle(f.Ctx, accent, o.Combo, outerFill: piece == CirclePiece.HitCircle); // Argon has no end circle
        }
    }

    private void DrawCircle(Frame f, PlayfieldObject o)
    {
        DrawHead(f, o, CirclePiece.HitCircle);
        if (f.Time < o.Start) return;

        // The editor's hit marker (HitCircleOverlapMarker): a ring in the combo colour that grows a little and fades.
        double passed = (f.Time - o.Start) / EditorFadeOut;
        double alpha = 0.9 * (1 - passed * passed);
        if (alpha <= 0) return;
        using (f.Ctx.PushOpacity(alpha))
        using (f.Object(o, o.Position, size: 1 + 0.1 * OutQuint((f.Time - o.Start) / (EditorFadeOut / 2))))
            Ring(f.Ctx, Paint.Brush(Colour(o)), Diameter, 4);
    }

    private void DrawSlider(Frame f, PlayfieldObject o)
    {
        var ctx = f.Ctx;
        double time = f.Time;
        var colour = Colour(o);

        // The body (ArgonSliderBody): the accent colour as its border, the darkened accent inside, snaking in over
        // the first third of the approach time. After the slider's end only the body fades, over 240 ms.
        double bodyAlpha = time > o.End ? Math.Clamp(1 - (time - o.End) / SliderFadeOut, 0, 1) : f.FadeIn(o);
        double snaked = Math.Clamp((time - (o.Start - f.Field.Preempt)) / (f.Field.Preempt / 3), 0, 1);
        if (bodyAlpha > 0 && snaked > 0 && skin is not null) DrawLegacyBody(f, o, skin, colour, bodyAlpha, snaked);
        else if (bodyAlpha > 0 && snaked > 0)
        {
            Geometry body;
            if (snaked >= 1)
            {
                if (!bodies.TryGetValue(o, out body!)) bodies[o] = body = BodyGeometry(o.Path);
            }
            else body = BodyGeometry(o.PathTo(snaked));

            double unit = f.Field.Radius / (Diameter / 2); // playfield units per object unit
            double pathRadius = OuterGradientSize / 2 * unit;
            var toScreen = Matrix.CreateTranslation(-o.StackOffset, -o.StackOffset) * Matrix.CreateScale(f.Scale, f.Scale) * Matrix.CreateTranslation(f.Origin.X, f.Origin.Y);
            using (ctx.PushOpacity(bodyAlpha))
            using (ctx.PushTransform(toScreen))
            {
                ctx.DrawGeometry(null, new ImmutablePen(Paint.Brush(colour), pathRadius * 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), body);
                ctx.DrawGeometry(null, new ImmutablePen(Paint.Brush(Darken(colour, 4), 0.98), (pathRadius - GradientThickness * unit) * 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), body);
            }
        }

        // The end circle (legacy skins only): it waits for the body to snake in, or appears two spans ahead.
        if (skin is not null && o.Spans > 0)
        {
            double appears = o.Spans == 1 ? o.Start - f.Field.Preempt + f.Field.Preempt / 3 : o.End - o.SpanDuration * 2;
            double before = Math.Clamp((time - appears) / (o.Spans == 1 ? f.Field.FadeIn : 1), 0, 1);
            DrawPiece(f, o, o.EndPosition, CirclePiece.SliderEnd, o.End, before);
        }

        // Ticks still to come (ArgonSliderScorePoint): a 12-unit ring in the accent colour. Each appears partway
        // through its span (SliderTick.TimePreempt) and pops in over 150 ms.
        foreach (double tick in o.TickTimes)
        {
            if (tick <= time || o.SpanDuration <= 0) continue;
            int span = Math.Clamp((int)((tick - o.Start) / o.SpanDuration), 0, o.Spans - 1);
            double spanStart = o.Start + span * o.SpanDuration;
            double appears = tick - ((tick - spanStart) / 2 + (span > 0 ? 200 : f.Field.Preempt * 0.66));
            if (time < appears) continue;
            using (ctx.PushOpacity(Math.Clamp((time - appears) / 150, 0, 1)))
            using (f.Object(o, o.BallAt(tick), size: 0.5 + 0.5 * OutQuint((time - appears) / 600)))
            {
                if (skin is not null) Sprite(ctx, skin.Get("sliderscorepoint"), Colors.White, Diameter);
                else Ring(ctx, Paint.Brush(colour), 12, 3);
            }
        }

        // Reverse arrows at the ends the ball still turns round at. The first waits for the body to snake in; later
        // ones appear two spans ahead. Each is gone at its own time.
        for (int node = o.Spans - 1; node >= 1; node--)
        {
            double nodeTime = o.Start + node * o.SpanDuration;
            if (o.Path.Count < 2) continue;
            double appears = node == 1 ? o.Start - f.Field.Preempt + f.Field.Preempt / 3 : nodeTime - o.SpanDuration * 2;
            if (time < appears) continue;
            bool atEnd = node % 2 == 1;
            var at = atEnd ? o.Path[^1] : o.Path[0];
            // A legacy skin's end circle sits under each arrow, and stays, whitening, after the ball has turned.
            if (skin is not null) DrawPiece(f, o, at, CirclePiece.SliderEnd, nodeTime, Math.Clamp((time - appears) / (node == 1 ? f.Field.FadeIn : 1), 0, 1));
            if (time >= nodeTime) continue;
            var towards = at;
            for (int i = atEnd ? o.Path.Count - 1 : 0; i >= 0 && i < o.Path.Count; i += atEnd ? -1 : 1)
                if (o.Path[i].DistanceTo(at) > 1e-3) { towards = o.Path[i]; break; }
            using (ctx.PushOpacity(Math.Clamp((time - appears) / 150, 0, 1)))
            using (f.Object(o, at, rotation: Math.Atan2(towards.Y - at.Y, towards.X - at.X)))
            {
                if (skin is not null) DrawLegacyArrow(ctx, skin, colour, time - appears);
                else DrawReverseArrow(ctx, colour, time - appears);
            }
        }

        DrawHead(f, o, CirclePiece.SliderHead);

        // The ball (ArgonSliderBall) while the slider runs.
        if (time >= o.Start && time <= o.End)
        {
            var ball = o.BallAt(time);
            var ahead = o.BallAt(time + 1);
            var behind = o.BallAt(time - 1);
            double angle = Math.Atan2(ahead.Y - behind.Y, ahead.X - behind.X);
            if (skin is not null)
            {
                using (f.Object(o, ball, rotation: angle))
                    DrawLegacyBall(ctx, skin, colour, o, time);
            }
            else using (ctx.PushOpacity(Math.Clamp((time - o.Start) / 50, 0, 1)))
            {
                using (f.Object(o, ball))
                {
                    ctx.DrawEllipse(Brushes(colour).Ball, null, default, OuterGradientSize / 2, OuterGradientSize / 2);
                    Ring(ctx, Avalonia.Media.Brushes.White, OuterGradientSize, GradientThickness);
                }

                // Font Awesome's angle-right, 48 units, squeezed to 0.6 × 0.8 and turned the way the ball travels.
                using (f.Object(o, ball, rotation: angle))
                using (ctx.PushTransform(Matrix.CreateScale(0.6, 0.8)))
                    Chevron(ctx, Avalonia.Media.Brushes.White, 0, 48);
            }
        }
    }

    private static Geometry BodyGeometry(IReadOnlyList<PlayfieldPoint> path)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(new Point(path[0].X, path[0].Y), false);
        // A slider with no length is still drawn as a dot: a line needs two points.
        if (path.Count == 1) g.LineTo(new Point(path[0].X + 0.01, path[0].Y));
        for (int i = 1; i < path.Count; i++) g.LineTo(new Point(path[i].X, path[i].Y));
        g.EndFigure(false);
        return geometry;
    }

    /// <summary>
    /// ArgonReverseArrow, pointing along +X in object units: a white 40 × 20 pill with a dark double chevron, and the
    /// edge piece round the circle. Every 300 ms the pill jumps to 1.3× and the edge 12 units back, then both settle.
    /// </summary>
    private void DrawReverseArrow(DrawingContext ctx, Color accent, double sinceAppeared)
    {
        const double moveOut = 35, moveIn = 250, total = 300;
        double loop = sinceAppeared % total;
        double pulse = loop < moveOut ? OutQuad(loop / moveOut) : 1 - OutQuad((loop - moveOut) / moveIn);

        if (ArgonPieces.HasRepeatEdge) ArgonPieces.DrawRepeatEdge(ctx, new Point(-12 * pulse, 0), OuterGradientSize);
        using (ctx.PushTransform(Matrix.CreateScale(1 + 0.3 * pulse, 1 + 0.3 * pulse)))
        {
            ctx.DrawRectangle(Avalonia.Media.Brushes.White, null, new Rect(-20, -10, 40, 20), 10, 10);
            var dark = Brushes(accent).Fill;
            Chevron(ctx, dark, -3.2, 16);
            Chevron(ctx, dark, 3.2, 16);
        }
    }

    // One Font Awesome "angle" chevron pointing along +X, for an icon of the given size centred on the origin.
    private static void Chevron(DrawingContext ctx, IImmutableBrush brush, double x, double size)
    {
        var pen = new ImmutablePen(brush, size * 0.13, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        double w = size * 0.14, h = size * 0.27;
        ctx.DrawLine(pen, new Point(x - w, -h), new Point(x + w, 0));
        ctx.DrawLine(pen, new Point(x + w, 0), new Point(x - w, h));
    }

    /// <summary>
    /// A spinner as Argon's disc looks before anyone spins it (the editor never does): the small pink fill, the ring
    /// of 25 ticks, the two thin ring arcs above and below, the two wide arcs at the sides and the white centre.
    /// It grows in during the approach time and fades over 240 ms after its end.
    /// </summary>
    private void DrawSpinner(Frame f, PlayfieldObject o)
    {
        var ctx = f.Ctx;
        double alpha = f.Time > o.End ? Math.Clamp(1 - (f.Time - o.End) / SliderFadeOut, 0, 1) : f.FadeIn(o);
        if (alpha <= 0) return;
        if (skin is not null) { DrawLegacySpinner(f, o, skin, alpha); return; }

        // The disc opens to 0.2 in the first quarter of the approach time, then to full size over the next half.
        double preempt = f.Field.Preempt, since = f.Time - (o.Start - preempt);
        double grown = since < preempt / 4 ? 0.2 * OutQuint(since / (preempt / 4)) : 0.2 + 0.8 * OutQuint((since - preempt / 4) / (preempt / 2));
        double radius = Playfield.Height * 0.5 * f.Scale * grown;
        var centre = new Point(f.Origin.X + Playfield.Width / 2 * f.Scale, f.Origin.Y + Playfield.Height / 2 * f.Scale);
        if (radius < 1) return;

        using (ctx.PushOpacity(alpha))
        using (ctx.PushTransform(Matrix.CreateTranslation(centre.X, centre.Y)))
        {
            ctx.DrawEllipse(Paint.Brush(SpinnerPink, 0.2), null, default, radius * 0.1, radius * 0.1);

            var tick = Paint.Brush(Colors.White, 0.2);
            double unit = radius / 192;
            for (int i = 0; i < 25; i++)
                using (ctx.PushTransform(Matrix.CreateRotation(-(i / 25.0 * 360 + 120) * Math.PI / 180)))
                    ctx.DrawRectangle(tick, null, new Rect(radius - 34 * unit, -2.5 * unit, 30 * unit, 5 * unit), 2.5 * unit, 2.5 * unit);

            // Thin ring arcs (31 % of the circle each, top and bottom) and wide ones (15 % each, left and right).
            Arc(ctx, new ImmutablePen(Avalonia.Media.Brushes.White, radius * 0.02), radius * 0.99, -90, 0.31 * 360);
            Arc(ctx, new ImmutablePen(Avalonia.Media.Brushes.White, radius * 0.02), radius * 0.99, 90, 0.31 * 360);
            Arc(ctx, new ImmutablePen(Paint.Brush(Colors.White, 0.25), radius * 0.12), radius * 0.94, 0, 0.15 * 360);
            Arc(ctx, new ImmutablePen(Paint.Brush(Colors.White, 0.25), radius * 0.12), radius * 0.94, 180, 0.15 * 360);

            double middle = 40 * unit * 0.8;
            ctx.DrawEllipse(null, new ImmutablePen(Avalonia.Media.Brushes.White, Math.Max(1.5, 4 * unit)), default, middle, middle);
        }
    }

    // An arc centred on an angle (degrees, 0 = right, 90 = down), sweeping the given degrees.
    private static void Arc(DrawingContext ctx, IPen pen, double radius, double centreAngle, double sweep)
    {
        double from = (centreAngle - sweep / 2) * Math.PI / 180, to = (centreAngle + sweep / 2) * Math.PI / 180;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(radius * Math.Cos(from), radius * Math.Sin(from)), false);
            g.ArcTo(new Point(radius * Math.Cos(to), radius * Math.Sin(to)), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        ctx.DrawGeometry(null, pen, geometry);
    }

    private static void DrawMessage(DrawingContext ctx, Rect bounds, string message)
    {
        var text = Paint.Format(message, 13, Message);
        text.MaxTextWidth = Math.Max(40, bounds.Width - 32);
        text.TextAlignment = TextAlignment.Center;
        ctx.DrawText(text, new Point(16, (bounds.Height - text.Height) / 2));
    }
}

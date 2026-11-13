using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// The map preview with an osu!stable-format skin (lazer's Classic, or a folder from osu!'s Skins directory), drawn
/// as lazer draws legacy skins (osu.Game.Rulesets.Osu/Skinning/Legacy). Sprites are laid out in object units, where
/// a hit circle image is 128 across.
/// </summary>
internal sealed partial class PlayfieldView
{
    private const double LegacyCircleRadius = 59; // of 64: where a legacy slider's border starts
    private const double LegacyBorderPortion = 0.1875;

    private enum CirclePiece { HitCircle, SliderHead, SliderEnd }

    // A slider body's shapes in playfield units: the border as a ring (the track inside it is see-through).
    private sealed record LegacyBody(Geometry Path, Geometry Border);

    private readonly Dictionary<PlayfieldObject, LegacyBody> legacyBodies = [];

    // A sprite centred on the origin at osu!'s size for it, no larger than max on either side.
    private static void Sprite(DrawingContext ctx, SkinTexture? texture, Color tint, double max = Diameter * 2, double size = 1)
    {
        if (texture is null) return;
        double fit = Math.Min(1, Math.Min(max / texture.Width, max / texture.Height)) * size;
        double w = texture.Width * fit, h = texture.Height * fit;
        var image = texture.In(tint);
        ctx.DrawImage(image, new Rect(0, 0, image.PixelSize.Width, image.PixelSize.Height), new Rect(-w / 2, -h / 2, w, h));
    }

    /// <summary>
    /// LegacyMainCirclePiece: the circle tinted with the combo colour, then its overlay and the combo number in the
    /// skin's digits (the overlay on top unless the skin says otherwise). Slider heads and ends use the skin's own
    /// sliderstartcircle / sliderendcircle when it has them.
    /// </summary>
    private static void DrawLegacyCircle(DrawingContext ctx, LegacySkin skin, Color accent, int number, CirclePiece piece)
    {
        string? prefix = piece switch { CirclePiece.SliderHead => "sliderstartcircle", CirclePiece.SliderEnd => "sliderendcircle", _ => null };
        var provider = skin.Provider("hitcircle") ?? skin;
        string name = prefix is not null && provider.Own(prefix) is not null ? prefix : "hitcircle";

        Sprite(ctx, skin.Get(name), accent);
        var overlay = skin.Get(name + "overlay");
        bool numbered = piece != CirclePiece.SliderEnd && number > 0;
        if (!skin.OverlayAboveNumber) Sprite(ctx, overlay, Colors.White);
        if (numbered) DrawLegacyNumber(ctx, skin, number);
        if (skin.OverlayAboveNumber) Sprite(ctx, overlay, Colors.White);
    }

    // The combo number in the skin's hit circle font (HitCirclePrefix-0…9) at 0.8 size, digits overlapping by HitCircleOverlap.
    private static void DrawLegacyNumber(DrawingContext ctx, LegacySkin skin, int number)
    {
        const double textScale = 0.8, maxGlyph = Diameter * 2 / textScale;
        // The digits come from the first skin that has them, with its own prefix and overlap.
        LegacySkin? font = skin;
        while (font is not null && font.Own(font.HitCirclePrefix + "-0") is null) font = font.Fallback;
        if (font is null) return;

        string text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var glyphs = new List<(SkinTexture Texture, double Width, double Height)>();
        foreach (char c in text)
        {
            if (font.Own($"{font.HitCirclePrefix}-{c}") is not { } glyph) return;
            double fit = Math.Min(1, Math.Min(maxGlyph / glyph.Width, maxGlyph / glyph.Height)) * textScale;
            glyphs.Add((glyph, glyph.Width * fit, glyph.Height * fit));
        }

        double overlap = font.HitCircleOverlap * textScale;
        double x = -(glyphs.Sum(g => g.Width) - overlap * (glyphs.Count - 1)) / 2;
        foreach (var (texture, w, h) in glyphs)
        {
            var image = texture.In(Colors.White);
            ctx.DrawImage(image, new Rect(0, 0, image.PixelSize.Width, image.PixelSize.Height), new Rect(x, -h / 2, w, h));
            x += w - overlap;
        }
    }

    /// <summary>
    /// LegacySliderBody: a soft shadow at the very edge, the skin's border colour, then the track (the skin's
    /// SliderTrackOverride or the combo colour) at 70 %, a little darker at its edge and lighter in the middle.
    /// </summary>
    private void DrawLegacyBody(Frame f, PlayfieldObject o, LegacySkin skin, Color accent, double alpha, double snaked)
    {
        double radius = f.Field.Radius; // the path is as wide as the circle
        LegacyBody body;
        if (snaked >= 1)
        {
            if (!legacyBodies.TryGetValue(o, out body!)) legacyBodies[o] = body = BuildLegacyBody(o.Path, radius);
        }
        else body = BuildLegacyBody(o.PathTo(snaked), radius);

        var track = skin.SliderTrackOverride ?? accent;
        Color outer = Darken(track, 0.1), inner = LegacyLighten(track, 0.5);
        var ctx = f.Ctx;
        var toScreen = Matrix.CreateTranslation(-o.StackOffset, -o.StackOffset) * Matrix.CreateScale(f.Scale, f.Scale) * Matrix.CreateTranslation(f.Origin.X, f.Origin.Y);
        using (ctx.PushOpacity(alpha))
        using (ctx.PushTransform(toScreen))
        {
            static IPen Pen(Color colour, double opacity, double width) =>
                new ImmutablePen(Paint.Brush(colour, opacity), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

            // The shadow: transparent at the edge to a quarter black where the border starts.
            double shadowPortion = 1 - LegacyCircleRadius / (Diameter / 2);
            ctx.DrawGeometry(null, Pen(Colors.Black, 0.08, radius * 2), body.Path);
            ctx.DrawGeometry(null, Pen(Colors.Black, 0.1, radius * 2 * (1 - shadowPortion / 2)), body.Path);

            // The track, in steps from its edge to its middle, all at 70 %.
            using (ctx.PushOpacity(0.7))
            {
                const int steps = 28;
                for (int i = 0; i < steps; i++)
                {
                    double t = (double)i / steps;
                    double portion = LegacyBorderPortion + (1 - LegacyBorderPortion) * t;
                    ctx.DrawGeometry(null, Pen(MixLinear(outer, inner, t), 1, radius * 2 * (1 - portion)), body.Path);
                }
            }

            ctx.DrawGeometry(Paint.Brush(skin.SliderBorder ?? Colors.White), null, body.Border);
        }
    }

    private static LegacyBody BuildLegacyBody(IReadOnlyList<PlayfieldPoint> points, double radius)
    {
        var path = BodyGeometry(points);
        static IPen Wide(double width) => new ImmutablePen(Avalonia.Media.Brushes.Black, width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        double shadowPortion = 1 - LegacyCircleRadius / (Diameter / 2);
        var border = new CombinedGeometry(GeometryCombineMode.Exclude,
            path.GetWidenedGeometry(Wide(radius * 2 * (1 - shadowPortion))),
            path.GetWidenedGeometry(Wide(radius * 2 * (1 - LegacyBorderPortion))));
        return new LegacyBody(path, border);
    }

    // LegacySliderBody's lighten: friendlier to dark or strong colours than a plain multiply.
    private static Color LegacyLighten(Color c, double amount)
    {
        amount *= 0.5;
        static byte Channel(byte v, double amount) => (byte)Math.Min(255, v * (1 + 0.5 * amount) + 255 * amount);
        return Color.FromRgb(Channel(c.R, amount), Channel(c.G, amount), Channel(c.B, amount));
    }

    // A mix in linear light, as lazer interpolates colours.
    private static Color MixLinear(Color a, Color b, double t)
    {
        static double ToLinear(byte v) => Math.Pow(v / 255.0, 2.2);
        static byte ToSrgb(double v) => (byte)Math.Round(Math.Pow(Math.Clamp(v, 0, 1), 1 / 2.2) * 255);
        return Color.FromRgb(
            ToSrgb(ToLinear(a.R) + (ToLinear(b.R) - ToLinear(a.R)) * t),
            ToSrgb(ToLinear(a.G) + (ToLinear(b.G) - ToLinear(a.G)) * t),
            ToSrgb(ToLinear(a.B) + (ToLinear(b.B) - ToLinear(a.B)) * t));
    }

    /// <summary>
    /// LegacySliderBall, already turned the way the ball travels: sliderb (its frames stepping with the ball's speed)
    /// in the skin's SliderBall colour, or the combo colour when the skin allows tinting, over the dark sliderb-nd.
    /// </summary>
    private static void DrawLegacyBall(DrawingContext ctx, LegacySkin skin, Color accent, PlayfieldObject o, double time)
    {
        var provider = skin.Provider("sliderb", "sliderb0");
        if (provider is null) return;
        var frames = provider.Frames("sliderb");
        if (frames.Count == 0) return;

        // A frame lasts 0.15 / velocity sixtieths of a second, and at least one.
        double length = 0;
        for (int i = 1; i < o.Path.Count; i++) length += o.Path[i - 1].DistanceTo(o.Path[i]);
        double velocity = o.SpanDuration > 0 ? length / o.SpanDuration : 1;
        double frameTime = Math.Max(0.15 / Math.Max(velocity, 1e-6) * (1000 / 60.0), 1000 / 60.0);
        var frame = frames[(int)Math.Max(0, (time - o.Start) / frameTime) % frames.Count];

        const double max = Diameter * 3;
        Sprite(ctx, provider.Get("sliderb-nd"), Color.FromRgb(5, 5, 5), max);
        Sprite(ctx, frame, provider.AllowSliderBallTint ? accent : provider.SliderBall ?? Colors.White, max);
    }

    /// <summary>
    /// LegacyReverseArrow, pointing along +X: the skin's reversearrow, shrinking from 1.3× every 300 ms (old skins
    /// also rock it a few degrees). The classic skin's arrow turns black on a bright circle.
    /// </summary>
    private static void DrawLegacyArrow(DrawingContext ctx, LegacySkin skin, Color accent, double sinceAppeared)
    {
        var provider = skin.Provider("reversearrow");
        if (provider is null) return;
        const double duration = 300;
        double loop = sinceAppeared % duration / duration;
        bool rocks = skin.Version <= 1;
        double size = rocks ? 1.3 - 0.3 * loop : 1.3 - 0.3 * OutQuad(loop);
        double turn = rocks ? (5.625 - 11.25 * loop) * Math.PI / 180 : 0;
        bool dark = provider.Fallback is null && provider.Name == "Classic" && accent.R + accent.G + accent.B > 600;
        using (ctx.PushTransform(Matrix.CreateRotation(turn)))
            Sprite(ctx, provider.Get("reversearrow"), dark ? Colors.Black : Colors.White, size: size);
    }

    /// <summary>
    /// A legacy spinner, still (nobody spins it in the editor): the newer disc (spinner-bottom, -top, -middle2,
    /// -middle) or the older one (spinner-background, spinner-circle), with the approach circle closing in.
    /// </summary>
    private static void DrawLegacySpinner(Frame f, PlayfieldObject o, LegacySkin skin, double alpha)
    {
        const double spriteScale = 0.625; // stable's 1024 × 768 sprites on its 640 × 480 field
        var ctx = f.Ctx;
        var provider = skin.Provider("spinner-background", "spinner-top");
        if (provider is null) return;
        bool oldStyle = provider.Own("spinner-background") is not null;

        var centre = new Point(f.Origin.X + Playfield.Width / 2 * f.Scale, f.Origin.Y + Playfield.Height / 2 * f.Scale);
        double unit = f.Scale * spriteScale; // screen pixels per sprite pixel
        const double any = double.MaxValue;
        using (ctx.PushOpacity(alpha))
        using (ctx.PushTransform(Matrix.CreateScale(unit, unit) * Matrix.CreateTranslation(centre.X, centre.Y)))
        {
            if (oldStyle)
            {
                Sprite(ctx, provider.Get("spinner-background"), provider.SpinnerBackground ?? Color.FromRgb(100, 100, 100), any);
                Sprite(ctx, provider.Get("spinner-circle"), Colors.White, any);
            }
            else
            {
                // Before any spin the disc sits at 0.8 of its size, and the glow is not lit.
                Sprite(ctx, provider.Get("spinner-bottom"), Colors.White, any, 0.8);
                Sprite(ctx, provider.Get("spinner-top"), Colors.White, any, 0.8);
                Sprite(ctx, provider.Get("spinner-middle2"), Colors.White, any, 0.8);
                Sprite(ctx, provider.Get("spinner-middle"), Colors.White, any, 0.8);
            }

            // The approach circle: from 1.86× down to 0.1× over the spinner's length.
            if (f.Time >= o.Start && o.End > o.Start)
                Sprite(ctx, skin.Get("spinner-approachcircle"), Colors.White, any, 1.86 - 1.76 * Math.Clamp((f.Time - o.Start) / (o.End - o.Start), 0, 1));
        }
    }
}

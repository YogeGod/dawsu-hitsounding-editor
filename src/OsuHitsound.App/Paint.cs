using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace OsuHitsound.App;

/// <summary>Colours, fonts and sizes from docs/ui-design.md (variation G: light chrome around a dark arrange area, 22 px rows).</summary>
internal static class Paint
{
    // Every value comes from the UI theme (ui-theme.json, editable in the inspector); brushes and pens are cached until it changes.
    private static UiTheme T => UiTheme.Current;

    private static IBrush B(string key) => T.Cached("brush:" + key, () => (IBrush)Brush(T.Color(key)));

    private static IPen P(string key, double width) => T.Cached($"pen:{key}:{width}", () => (IPen)new ImmutablePen(Brush(T.Color(key)), width));

    public static IBrush Bg => B("arrange.bg");
    public static IBrush Chrome2 => B("header.bg");
    public static IBrush VolumeSide => B("volume.side");
    public static IBrush OverviewBg => B("overview.bg");
    public static IBrush PanelLow => B("volume.bg");
    public static IBrush Divider => B("divider");
    public static IBrush LaneHeadA => B("lane.headA");
    public static IBrush LaneHeadB => B("lane.headB");
    public static IBrush GroupHead => B("group.head");
    public static IBrush Button => B("toggle.bg");
    public static IBrush Text => B("lane.text");
    public static IBrush Dim => B("lane.dim");
    public static IBrush RulerBar => B("ruler.bar");
    public static IBrush RulerBeat => B("ruler.beat");
    public static IBrush MuteOn => B("toggle.mute");
    public static IBrush SoloOn => B("toggle.solo");
    public static IBrush Selection => B("note.selected");
    public static IBrush White => B("playhead.color");
    public static IBrush MissingText => B("lane.missing");
    public static IBrush MutedFill => B("colors.muted");

    public static IPen BeatPen => P("line.beat", 1);
    public static IPen BarPen => P("line.bar", 1);

    /// <summary>
    /// Snap line colour by beat fraction, osu!'s convention: 1/2 red, 1/4 blue, 1/8 yellow, 1/3 and 1/6 purple, finer
    /// ones grey; 1/5, 1/7 and 1/9 (not in osu!) teal. Dimmed so the lanes stay readable.
    /// </summary>
    public static IPen SnapPen(int fraction) => SnapPens.TryGetValue(SnapColour(fraction), out var pen) ? pen : SnapPens[SnapColour(fraction)] = new ImmutablePen(Brush(Color.Parse(SnapColour(fraction)), 0.22), 1);

    public static string SnapColour(int fraction) => fraction switch
    {
        2 => "#ff4b4b",
        4 => "#4b8cff",
        8 => "#f0d24b",
        3 => "#b46ef0",
        6 => "#8a5ad0",
        5 or 7 or 9 => "#3cc8b4",
        _ => "#6e6e74",
    };

    private static readonly Dictionary<string, IPen> SnapPens = [];
    public static IPen DividerPen => P("divider", 1);
    public static IPen PlayheadPen => P("playhead.color", T.Number("playhead.width"));
    public static IPen NoteRingPen => P("note.ring", 1);
    public static IPen SelectedRingPen => P("note.selected", 2);
    public static IPen OutlinePen => P("playhead.color", 1);

    public static readonly Typeface Ui = new("Inter");
    public static readonly Typeface UiBold = new("Inter", FontStyle.Normal, FontWeight.Bold);
    public static readonly Typeface Mono = new("Consolas, Cascadia Mono, monospace");

    public const double BaseSize = 13;

    public static ImmutableSolidColorBrush Brush(string hex) => new ImmutableSolidColorBrush(Color.Parse(hex));

    public static ImmutableSolidColorBrush Brush(Color color, double opacity = 1) => new ImmutableSolidColorBrush(color, opacity);

    /// <summary>The lane colour brightened, for notes.</summary>
    public static Color Brighten(Color c) => Brighten(c, T.Number("note.brighten"));

    public static Color Brighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

    public static FormattedText Format(string text, double size, IBrush brush, Typeface? face = null) =>
        new(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face ?? Ui, size, brush);

    /// <summary>Draws text cut to a width with an ellipsis.</summary>
    public static void DrawText(DrawingContext ctx, string text, Point at, double size, IBrush brush, double maxWidth, Typeface? face = null)
    {
        var ft = Format(text, size, brush, face);
        if (maxWidth > 0)
        {
            ft.MaxTextWidth = maxWidth;
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
        }

        ctx.DrawText(ft, at);
    }

    /// <summary>The design's note: a diamond (a square turned 45°).</summary>
    public static void DrawDiamond(DrawingContext ctx, Point centre, double size, IBrush fill, IPen ring)
    {
        // One geometry per size, centred on 0,0 and moved into place: notes are drawn every frame.
        var geometry = T.Cached($"diamond:{size}", () =>
        {
            double r = size / 2;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, -r), true);
                c.LineTo(new Point(r, 0));
                c.LineTo(new Point(0, r));
                c.LineTo(new Point(-r, 0));
                c.EndFigure(true);
            }

            return g;
        });

        using (ctx.PushTransform(Matrix.CreateTranslation(centre.X, centre.Y)))
            ctx.DrawGeometry(fill, ring, geometry);
    }
}

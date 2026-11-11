using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace OsuHitsound.App;

/// <summary>The mouse cursor over the grid and the volume bars.</summary>
internal enum ToolCursor
{
    Arrow,
    Hand,
    Pen,
    Eraser,
    Cross,
}

/// <summary>
/// The arrow for Select, a Cubase-style drumstick for Draw and while Alt is held,
/// an eraser in the same style for Erase, and a crosshair for the other tools. Elsewhere it is the normal arrow.
/// </summary>
internal static class ToolCursors
{
    /// <summary>Which cursor to show: over the grid (or bars) by tool, with Alt (not Ctrl+Alt or AltGr) meaning Draw.</summary>
    public static ToolCursor For(Tool tool, KeyModifiers modifiers, bool overGrid)
    {
        if (!overGrid) return ToolCursor.Arrow;
        if (tool == Tool.Grab) return ToolCursor.Hand;
        bool altDraw = modifiers.HasFlag(KeyModifiers.Alt) && !modifiers.HasFlag(KeyModifiers.Control) && !modifiers.HasFlag(KeyModifiers.Meta);
        return (altDraw ? Tool.Draw : tool) switch
        {
            Tool.Select => ToolCursor.Arrow,
            Tool.Draw => ToolCursor.Pen,
            Tool.Erase => ToolCursor.Eraser,
            _ => ToolCursor.Cross,
        };
    }

    private static readonly Dictionary<ToolCursor, Cursor> Made = [];

    /// <summary>The Avalonia cursor for a kind, made once (the pen and eraser are drawn from vector paths).</summary>
    public static Cursor Get(ToolCursor kind)
    {
        if (Made.TryGetValue(kind, out var cursor)) return cursor;
        try
        {
            cursor = kind switch
            {
                ToolCursor.Pen => Drawn(PenParts, new PixelPoint(4, 4)),
                ToolCursor.Eraser => Drawn(EraserParts, new PixelPoint(5, 27)),
                ToolCursor.Hand => new Cursor(StandardCursorType.Hand),
                ToolCursor.Cross => new Cursor(StandardCursorType.Cross),
                _ => Cursor.Default,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or PlatformNotSupportedException)
        {
            cursor = kind == ToolCursor.Arrow ? Cursor.Default : new Cursor(StandardCursorType.Cross); // no custom cursors on this platform
        }

        return Made[kind] = cursor;
    }

    // Rounded bead at the upper left, tapering neck and slim handle, as in Cubase's Drum Editor.
    // Shared with the toolbar so the button and cursor have the same silhouette.
    public const string DrumstickPath = "M3 3C1 5 3 9 6 10C8 11 9 10 10 11L26 28Q28 30 29 28Q30 27 28 25L12 9C11 8 12 7 10 5C8 2 5 1 3 3Z";

    // (SVG path data, fill, stroke, stroke width) in a 32×32 box.
    private static readonly (string Path, string? Fill, string? Stroke, double Width)[] PenParts =
    [
        (DrumstickPath, "#ffffff", "#151515", 1.25),
    ];

    // An eraser in the pen's style: a white block tilted the same way, its rubber end (bottom left) grey; the tip at (5, 27).
    private static readonly (string Path, string? Fill, string? Stroke, double Width)[] EraserParts =
    [
        ("M5 27 15 17 25 7Q26 6 27 7L30 10Q31 11 30 12L15 27Z", "#ffffff", "#151515", 1.5),
        ("M5 27 15 17 20 22 15 27Z", "#b8bcc2", "#151515", 1.2),
        ("M17 15 28 4", null, "#303030", 1),
        ("M3 29H17", null, "#151515", 1.5),
    ];

    /// <summary>The pen or eraser picture (32×32), for the snapshot tool.</summary>
    internal static RenderTargetBitmap Picture(ToolCursor kind) => Render(kind == ToolCursor.Eraser ? EraserParts : PenParts);

    private static Cursor Drawn((string Path, string? Fill, string? Stroke, double Width)[] parts, PixelPoint hotspot) => new(Render(parts), hotspot);

    private static RenderTargetBitmap Render((string Path, string? Fill, string? Stroke, double Width)[] parts)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            foreach (var (path, fill, stroke, width) in parts)
            {
                var geometry = StreamGeometry.Parse(path);
                IBrush? brush = fill is null ? null : new SolidColorBrush(Color.Parse(fill));
                IPen? pen = stroke is null ? null : new Pen(new SolidColorBrush(Color.Parse(stroke)), width) { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round };
                ctx.DrawGeometry(brush, pen, geometry);
            }
        }

        return bitmap;
    }
}

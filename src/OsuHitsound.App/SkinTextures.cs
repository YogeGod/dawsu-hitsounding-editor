using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using OsuHitsound.Audio;

namespace OsuHitsound.App;

/// <summary>
/// osu!'s classic hit-circle textures (hitcircle, hitcircleoverlay, default-0…9, reversearrow), read from the
/// osu!lazer install as the default sounds are: the files are copied out as data, nothing from lazer is run, and none
/// are bundled with this app (their licence doesn't allow it). Without lazer, <see cref="Loaded"/> stays false and the
/// strip draws its own circles.
/// </summary>
internal static class SkinTextures
{
    private static WriteableBitmap? hitCircle;
    private static Bitmap? overlay, reverseArrow;
    private static readonly Bitmap?[] digits = new Bitmap?[10];
    private static readonly Dictionary<Color, Bitmap> tinted = [];
    private static double circleFill = 1; // how much of the hit circle's width its opaque circle covers

    public static bool Loaded => hitCircle is not null && overlay is not null;

    /// <summary>Reads the textures in the background; <paramref name="done"/> runs on the UI thread when they are ready.</summary>
    public static void LoadAsync(string? preferredDll, Action done) => _ = Task.Run(() =>
    {
        try
        {
            if (DefaultSkin.FindLazerResources(preferredDll) is not { } dll) return;
            var files = DefaultSkin.LoadTextures(dll);
            Bitmap? Open(string name) => files.TryGetValue(name, out var data) ? new Bitmap(new MemoryStream(data)) : null;
            var circle = files.TryGetValue("hitcircle@2x", out var circleData) ? WriteableBitmap.Decode(new MemoryStream(circleData)) : null;
            var top = Open("hitcircleoverlay@2x");
            var arrow = Open("reversearrow@2x");
            var numbers = Enumerable.Range(0, 10).Select(i => Open($"default-{i}@2x")).ToArray();
            Dispatcher.UIThread.Post(() =>
            {
                hitCircle = circle;
                overlay = top;
                reverseArrow = arrow;
                for (int i = 0; i < 10; i++) digits[i] = numbers[i];
                tinted.Clear();
                if (circle is not null) circleFill = OpaqueWidth(circle) / (double)circle.PixelSize.Width;
                done();
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            // No textures: the strip keeps drawing its own circles.
        }
    });

    /// <summary>
    /// Draws a hit circle as osu! does: the circle tinted with the combo colour, the overlay on top, then the combo
    /// number in the skin's digits. <paramref name="radius"/> is the visible circle's radius.
    /// </summary>
    public static void DrawCircle(DrawingContext ctx, Point centre, double radius, Color colour, int combo, bool arrow = false)
    {
        if (hitCircle is null || overlay is null) return;
        double side = radius * 2 / circleFill, scale = side / hitCircle.PixelSize.Width;
        Draw(ctx, Tinted(colour), centre, scale);
        Draw(ctx, overlay, centre, scale);
        if (arrow && reverseArrow is not null) Draw(ctx, reverseArrow, centre, scale);
        else if (combo > 0) DrawNumber(ctx, centre, scale * 0.8, combo);
    }

    // The skin's digits side by side, centred, overlapping by osu!'s default HitCircleOverlap (2 px at 1×).
    private static void DrawNumber(DrawingContext ctx, Point centre, double scale, int number)
    {
        const double overlap = 4; // @2x pixels
        Span<int> places = stackalloc int[10];
        int count = 0;
        for (int n = number; count == 0 || n > 0; n /= 10) places[count++] = n % 10;
        double width = overlap;
        for (int i = 0; i < count; i++)
            if (digits[places[i]] is { } d) width += d.PixelSize.Width - overlap;
            else return;
        double x = centre.X - width * scale / 2;
        for (int i = count - 1; i >= 0; i--)
        {
            var g = digits[places[i]];
            double w = g!.PixelSize.Width * scale, h = g.PixelSize.Height * scale;
            ctx.DrawImage(g, new Rect(0, 0, g.PixelSize.Width, g.PixelSize.Height), new Rect(x, centre.Y - h / 2, w, h));
            x += (g.PixelSize.Width - overlap) * scale;
        }
    }

    private static void Draw(DrawingContext ctx, Bitmap bitmap, Point centre, double scale)
    {
        double w = bitmap.PixelSize.Width * scale, h = bitmap.PixelSize.Height * scale;
        ctx.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), new Rect(centre.X - w / 2, centre.Y - h / 2, w, h));
    }

    // The grey hit circle multiplied by a combo colour (osu!'s tint), made once per colour.
    private static Bitmap Tinted(Color colour)
    {
        if (tinted.TryGetValue(colour, out var done)) return done;
        var source = hitCircle!;
        var size = source.PixelSize;
        var result = new WriteableBitmap(size, source.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var from = source.Lock())
        using (var to = result.Lock())
        {
            bool rgba = from.Format == PixelFormat.Rgba8888;
            var row = new byte[from.RowBytes];
            var outRow = new byte[to.RowBytes];
            for (int y = 0; y < size.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(from.Address + y * from.RowBytes, row, 0, from.RowBytes);
                for (int x = 0; x < size.Width; x++)
                {
                    int i = x * 4;
                    byte r = row[i + (rgba ? 0 : 2)], g = row[i + 1], b = row[i + (rgba ? 2 : 0)], a = row[i + 3];
                    if (source.AlphaFormat == AlphaFormat.Unpremul) (r, g, b) = ((byte)(r * a / 255), (byte)(g * a / 255), (byte)(b * a / 255));
                    outRow[i] = (byte)(b * colour.B / 255);
                    outRow[i + 1] = (byte)(g * colour.G / 255);
                    outRow[i + 2] = (byte)(r * colour.R / 255);
                    outRow[i + 3] = a;
                }

                System.Runtime.InteropServices.Marshal.Copy(outRow, 0, to.Address + y * to.RowBytes, Math.Min(outRow.Length, to.RowBytes));
            }
        }

        return tinted[colour] = result;
    }

    // The width of the circle's mostly opaque part along its middle row, so the drawn circle matches the given radius.
    private static int OpaqueWidth(WriteableBitmap bitmap)
    {
        using var fb = bitmap.Lock();
        var row = new byte[fb.RowBytes];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address + fb.Size.Height / 2 * fb.RowBytes, row, 0, fb.RowBytes);
        int first = -1, last = -1;
        for (int x = 0; x < fb.Size.Width; x++)
            if (row[x * 4 + 3] > 128) { if (first < 0) first = x; last = x; }
        return first < 0 ? fb.Size.Width : last - first + 1;
    }
}

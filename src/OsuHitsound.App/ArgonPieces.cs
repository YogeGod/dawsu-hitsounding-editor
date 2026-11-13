using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using OsuHitsound.Audio;

namespace OsuHitsound.App;

/// <summary>
/// The parts of osu!lazer's default (Argon) look that lazer keeps as files rather than drawing in code: the approach
/// circle, the reverse arrow's edge piece and the Torus Bold digits. Read from the lazer install as the default sounds
/// are (copied out as data, nothing run, nothing bundled). Without lazer the map preview draws its own stand-ins.
/// </summary>
internal static class ArgonPieces
{
    private readonly record struct Glyph(Rect Source, double XOffset, double YOffset, double Advance);

    private static WriteableBitmap? approach;
    private static Bitmap? repeatEdge, fontPage;
    private static readonly Dictionary<char, Glyph> digits = [];
    private static double fontSize = 100;
    private static readonly Dictionary<Color, Bitmap> tintedApproach = [];

    public static bool HasApproachCircle => approach is not null;
    public static bool HasRepeatEdge => repeatEdge is not null;
    public static bool HasDigits => fontPage is not null && digits.Count == 10;

    /// <summary>Reads the pieces in the background; <paramref name="done"/> runs on the UI thread when they are ready.</summary>
    public static void LoadAsync(string? preferredDll, Action done) => _ = Task.Run(() =>
    {
        try
        {
            if (DefaultSkin.FindLazerResources(preferredDll) is not { } dll) return;
            var files = DefaultSkin.LoadArgonPieces(dll);
            var ring = files.TryGetValue("approachcircle.png", out var ringData) ? WriteableBitmap.Decode(new MemoryStream(ringData)) : null;
            var edge = files.TryGetValue("repeat-edge-piece.png", out var edgeData) ? new Bitmap(new MemoryStream(edgeData)) : null;
            var (glyphs, size, page) = files.TryGetValue("Torus-Bold.bin", out var font) ? ReadDigits(font) : ([], 100, 0);
            var sheet = glyphs.Count == 10 && files.TryGetValue($"Torus-Bold_{page}.png", out var sheetData) ? new Bitmap(new MemoryStream(sheetData)) : null;
            Dispatcher.UIThread.Post(() =>
            {
                (approach, repeatEdge, fontPage, fontSize) = (ring, edge, sheet, size);
                digits.Clear();
                foreach (var (c, g) in glyphs) digits[c] = g;
                tintedApproach.Clear();
                done();
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or EndOfStreamException)
        {
            // No pieces: the preview keeps its stand-ins.
        }
    });

    // The digits 0–9 from a BMFont binary file (the format lazer's glyph stores use): font size from the info block,
    // then each char's place on its page, offsets and advance. All ten must be on one page.
    private static (Dictionary<char, Glyph> Glyphs, double Size, int Page) ReadDigits(byte[] data)
    {
        var glyphs = new Dictionary<char, Glyph>();
        double size = 100;
        int page = -1;
        using var reader = new BinaryReader(new MemoryStream(data));
        if (reader.ReadByte() != 'B' || reader.ReadByte() != 'M' || reader.ReadByte() != 'F' || reader.ReadByte() != 3) return (glyphs, size, 0);
        while (reader.BaseStream.Position + 5 <= data.Length)
        {
            byte type = reader.ReadByte();
            int length = reader.ReadInt32();
            long next = reader.BaseStream.Position + length;
            if (length < 0 || next > data.Length) break;
            if (type == 1) size = Math.Abs(reader.ReadInt16());
            else if (type == 4)
                for (int i = 0; i + 20 <= length; i += 20)
                {
                    uint id = reader.ReadUInt32();
                    ushort x = reader.ReadUInt16(), y = reader.ReadUInt16(), w = reader.ReadUInt16(), h = reader.ReadUInt16();
                    short xOffset = reader.ReadInt16(), yOffset = reader.ReadInt16(), advance = reader.ReadInt16();
                    byte glyphPage = reader.ReadByte();
                    reader.ReadByte(); // channel
                    if (id is < '0' or > '9') continue;
                    if (page >= 0 && glyphPage != page) return ([], size, 0);
                    page = glyphPage;
                    glyphs[(char)id] = new Glyph(new Rect(x, y, w, h), xOffset, yOffset, advance);
                }

            reader.BaseStream.Position = next;
        }

        return (glyphs, size > 0 ? size : 100, Math.Max(page, 0));
    }

    /// <summary>
    /// Draws a number in white Torus Bold, centred on <paramref name="centre"/>, as lazer's sprite text lays it out:
    /// glyphs side by side by their advances, in a line as tall as the font size.
    /// </summary>
    public static void DrawNumber(DrawingContext ctx, Point centre, double size, int number)
    {
        if (fontPage is null) return;
        string text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        double k = size / fontSize, width = 0;
        foreach (char c in text)
            if (digits.TryGetValue(c, out var g)) width += g.Advance * k;
            else return;
        double x = centre.X - width / 2, top = centre.Y - size / 2;
        foreach (char c in text)
        {
            var g = digits[c];
            ctx.DrawImage(fontPage, g.Source, new Rect(x + g.XOffset * k, top + g.YOffset * k, g.Source.Width * k, g.Source.Height * k));
            x += g.Advance * k;
        }
    }

    /// <summary>The approach circle in a colour, <paramref name="side"/> wide and high (its image, not the ring inside it).</summary>
    public static void DrawApproachCircle(DrawingContext ctx, Point centre, double side, Color colour)
    {
        if (approach is null) return;
        if (!tintedApproach.TryGetValue(colour, out var tinted))
        {
            if (tintedApproach.Count > 64) tintedApproach.Clear();
            tintedApproach[colour] = tinted = Tint(approach, colour);
        }

        ctx.DrawImage(tinted, new Rect(0, 0, tinted.PixelSize.Width, tinted.PixelSize.Height), new Rect(centre.X - side / 2, centre.Y - side / 2, side, side));
    }

    /// <summary>The bright edge lazer draws round a reverse arrow, <paramref name="side"/> wide and high.</summary>
    public static void DrawRepeatEdge(DrawingContext ctx, Point centre, double side)
    {
        if (repeatEdge is null) return;
        ctx.DrawImage(repeatEdge, new Rect(0, 0, repeatEdge.PixelSize.Width, repeatEdge.PixelSize.Height), new Rect(centre.X - side / 2, centre.Y - side / 2, side, side));
    }

    // The image multiplied by a colour (lazer's sprite colour), made once per colour.
    private static Bitmap Tint(WriteableBitmap source, Color colour)
    {
        var size = source.PixelSize;
        var result = new WriteableBitmap(size, source.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var from = source.Lock();
        using var to = result.Lock();
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

        return result;
    }
}

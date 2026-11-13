using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OsuHitsound.Audio;

namespace OsuHitsound.App;

/// <summary>One skin image: its pixels and the size osu! draws it at (an @2x image is half its pixel size).</summary>
internal sealed class SkinTexture(WriteableBitmap image, double scale)
{
    private readonly Dictionary<Color, Bitmap> tinted = [];

    public double Width { get; } = image.PixelSize.Width / scale;

    public double Height { get; } = image.PixelSize.Height / scale;

    /// <summary>The image multiplied by a colour (osu!'s sprite tint); white is the image itself.</summary>
    public Bitmap In(Color colour)
    {
        if (colour == Colors.White) return image;
        if (tinted.TryGetValue(colour, out var done)) return done;
        if (tinted.Count > 24) tinted.Clear();
        return tinted[colour] = Tint(image, colour);
    }

    public static SkinTexture? Decode(byte[] data, double scale)
    {
        try
        {
            return new SkinTexture(WriteableBitmap.Decode(new MemoryStream(data)), scale);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or NotSupportedException)
        {
            return null; // not an image osu! could show either
        }
    }

    public static Bitmap Tint(WriteableBitmap source, Color colour)
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

/// <summary>
/// An osu!stable-format skin for the map preview: its images (name@2x.png before name.png) and the skin.ini settings
/// that change how objects look. What a skin leaves out comes from <see cref="Fallback"/>, as in osu! (lazer's classic
/// default skin). Images are read when first drawn; nothing is written.
/// </summary>
internal sealed class LegacySkin
{
    private readonly Func<string, byte[]?> file; // by name without extension, e.g. "hitcircle@2x"
    private readonly Dictionary<string, SkinTexture?> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> settings = new(StringComparer.OrdinalIgnoreCase);

    private LegacySkin(string name, Func<string, byte[]?> file, LegacySkin? fallback)
    {
        (Name, this.file, Fallback) = (name, file, fallback);
    }

    public string Name { get; }

    public LegacySkin? Fallback { get; }

    /// <summary>The skin's own combo colours, in order; empty when it sets none.</summary>
    public IReadOnlyList<Color> ComboColours { get; private set; } = [];

    public Color? SliderBorder => Colour("SliderBorder");
    public Color? SliderTrackOverride => Colour("SliderTrackOverride");
    public Color? SliderBall => Colour("SliderBall");
    public Color? SpinnerBackground => Colour("SpinnerBackground");
    public bool AllowSliderBallTint => Flag("AllowSliderBallTint", false);
    public bool OverlayAboveNumber => Flag("HitCircleOverlayAboveNumber", Flag("HitCircleOverlayAboveNumer", true)); // stable accepts the misspelling too
    public string HitCirclePrefix => settings.GetValueOrDefault("HitCirclePrefix") is { Length: > 0 } p ? p : "default";
    public double HitCircleOverlap => settings.TryGetValue("HitCircleOverlap", out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : -2;

    /// <summary>The skin.ini Version: 1.0 when the file has none, the newest for "latest" or no skin.ini at all.</summary>
    public decimal Version { get; private set; } = 2.7m;

    /// <summary>A skin folder from osu!'s Skins directory.</summary>
    public static LegacySkin FromFolder(string folder, LegacySkin? fallback)
    {
        var skin = new LegacySkin(Path.GetFileName(folder.TrimEnd('\\', '/')), name =>
        {
            try
            {
                string path = Path.Combine(folder, name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar) + ".png");
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }, fallback);
        try
        {
            string ini = Path.Combine(folder, "skin.ini");
            if (File.Exists(ini)) skin.ReadIni(File.ReadAllLines(ini, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable skin.ini: osu!'s defaults apply.
        }

        return skin;
    }

    /// <summary>lazer's classic default skin, from its resources (see <see cref="DefaultSkin.LoadTextures"/>); null without lazer.</summary>
    public static LegacySkin? LazerClassic(string? preferredDll)
    {
        try
        {
            if (DefaultSkin.FindLazerResources(preferredDll) is not { } dll) return null;
            var files = DefaultSkin.LoadTextures(dll);
            if (!files.ContainsKey("hitcircle@2x") && !files.ContainsKey("hitcircle")) return null;
            var skin = new LegacySkin("Classic", name => files.GetValueOrDefault(name), null);
            // DefaultLegacySkin's own settings.
            skin.settings["AllowSliderBallTint"] = "1";
            skin.settings["SliderBall"] = "2,170,255";
            return skin;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private void ReadIni(IEnumerable<string> lines)
    {
        var combos = new SortedDictionary<int, Color>();
        bool sawVersion = false;
        foreach (var raw in lines)
        {
            string line = raw;
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment];
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
            if (key.Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                sawVersion = true;
                Version = decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) ? v : 2.7m;
            }
            else if (key.StartsWith("Combo", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[5..], out int n) && n is >= 1 and <= 8)
            {
                if (ParseColour(value) is { } c) combos[n] = c;
            }
            else settings[key] = value;
        }

        if (!sawVersion) Version = 1.0m;
        ComboColours = [.. combos.Values];
    }

    private Color? Colour(string key) => settings.TryGetValue(key, out var s) ? ParseColour(s) : null;

    private static Color? ParseColour(string text)
    {
        var parts = text.Split(',');
        return parts.Length >= 3 && byte.TryParse(parts[0].Trim(), out byte r) && byte.TryParse(parts[1].Trim(), out byte g) && byte.TryParse(parts[2].Trim(), out byte b)
            ? Color.FromRgb(r, g, b) : null;
    }

    private bool Flag(string key, bool fallback) =>
        settings.TryGetValue(key, out var s) ? s.Trim() is "1" || s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) : fallback;

    /// <summary>An image this skin has itself (not from the fallback).</summary>
    public SkinTexture? Own(string name)
    {
        if (textures.TryGetValue(name, out var known)) return known;
        var texture = file(name + "@2x") is { } hd ? SkinTexture.Decode(hd, 2) : null;
        texture ??= file(name) is { } sd ? SkinTexture.Decode(sd, 1) : null;
        return textures[name] = texture;
    }

    /// <summary>An image from this skin, or from the fallback when this one has none.</summary>
    public SkinTexture? Get(string name) => Own(name) ?? Fallback?.Get(name);

    /// <summary>The skin (this one or a fallback) that has an image itself: osu! takes related images and settings from the same skin.</summary>
    public LegacySkin? Provider(params string[] names) => names.Any(n => Own(n) is not null) ? this : Fallback?.Provider(names);

    /// <summary>An animation's frames (name0, name1, …), or the single image.</summary>
    public IReadOnlyList<SkinTexture> Frames(string name)
    {
        var frames = new List<SkinTexture>();
        for (int i = 0; i < 120 && Own(name + i.ToString(CultureInfo.InvariantCulture)) is { } frame; i++) frames.Add(frame);
        if (frames.Count == 0 && Own(name) is { } single) frames.Add(single);
        return frames;
    }
}

/// <summary>A skin the map preview can use: lazer's Argon (drawn in code), lazer's Classic, or a folder of osu!'s Skins directory.</summary>
internal sealed record SkinChoice(string Id, string Name, string? Folder = null)
{
    public const string ArgonId = "argon", ClassicId = "classic";

    public override string ToString() => Name;

    /// <summary>The built-in choices and every skin folder, by name.</summary>
    public static List<SkinChoice> All(string? skinsFolder, bool lazerInstalled)
    {
        var choices = new List<SkinChoice> { new(ArgonId, "Argon (osu!lazer's default)") };
        if (lazerInstalled) choices.Add(new(ClassicId, "Classic (osu!lazer)"));
        try
        {
            if (skinsFolder is { Length: > 0 } && Directory.Exists(skinsFolder))
                choices.AddRange(Directory.GetDirectories(skinsFolder).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(d => new SkinChoice("folder:" + Path.GetFileName(d), Path.GetFileName(d), d)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder can't be listed: only the built-in skins are offered.
        }

        return choices;
    }

    /// <summary>osu!'s Skins folder: beside the Songs folder, else in a usual place.</summary>
    public static string? FindSkinsFolder(string? songsFolder)
    {
        var candidates = new List<string>();
        if (songsFolder is { Length: > 0 } && Path.GetDirectoryName(songsFolder.TrimEnd('\\', '/')) is { } osu) candidates.Add(Path.Combine(osu, "Skins"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!", "Skins"));
        return candidates.FirstOrDefault(Directory.Exists);
    }
}

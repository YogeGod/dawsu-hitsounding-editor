using System.Globalization;
using Avalonia.Media;

namespace OsuHitsound.App;

/// <summary>A colour as hue (0–360), saturation (0–1) and brightness (0–1), as the colour sliders show it.</summary>
internal readonly record struct Hsb(double H, double S, double B)
{
    public static Hsb FromColor(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return new Hsb(h < 0 ? h + 360 : h, max == 0 ? 0 : d / max, max);
    }

    public Color ToColor(byte alpha = 255)
    {
        double h = ((H % 360) + 360) % 360 / 60, s = Math.Clamp(S, 0, 1), v = Math.Clamp(B, 0, 1);
        double c = v * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
        var (r, g, b) = (int)h switch { 0 => (c, x, 0d), 1 => (x, c, 0d), 2 => (0d, c, x), 3 => (0d, x, c), 4 => (x, 0d, c), _ => (c, 0d, x) };
        static byte Byte(double f) => (byte)Math.Round(Math.Clamp(f, 0, 1) * 255);
        return Color.FromArgb(alpha, Byte(r + m), Byte(g + m), Byte(b + m));
    }

    public string ToHex(byte alpha = 255)
    {
        var c = ToColor(alpha);
        return alpha == 255 ? $"#{c.R:x2}{c.G:x2}{c.B:x2}" : $"#{c.A:x2}{c.R:x2}{c.G:x2}{c.B:x2}";
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{H:0} / {S * 100:0} / {B * 100:0}");
}

/// <summary>A row of the Colours page: what it colours, and whether its swatch opens hue + saturation + brightness or only the last two.</summary>
internal sealed record ColorRow(string Id, string Group, string Label, string MainKey, bool HasHue, string? Requires = null);

/// <summary>The user's colours from Preferences, Colours (Cubase's User Interface – General Colors).</summary>
internal sealed record UserColors
{
    /// <summary>The Default Color Scheme swatch (0–4); null before one is picked (the old dark-mode switch decides).</summary>
    public int? Scheme { get; init; }

    /// <summary>The ticked rows and their colours, by <see cref="ColorRow.Id"/>.</summary>
    public Dictionary<string, Hsb> Rows { get; init; } = [];

    public UserColors With(string row, Hsb? color)
    {
        var rows = new Dictionary<string, Hsb>(Rows);
        if (color is { } c) rows[row] = c; else rows.Remove(row);
        return this with { Rows = rows };
    }

    /// <summary>Equal by value: the same scheme and the same rows with the same colours (so Preferences compare by value).</summary>
    public bool Equals(UserColors? other) => other is not null && Scheme == other.Scheme && Rows.Count == other.Rows.Count
        && Rows.All(kv => other.Rows.TryGetValue(kv.Key, out var o) && o == kv.Value);

    public override int GetHashCode() => HashCode.Combine(Scheme, Rows.Count);
}

/// <summary>Turns the user's colours into theme values: the scheme first, then each ticked row.</summary>
internal static class ThemeColors
{
    public const string UserScheme = "scheme", Accent = "accent", GridBackground = "gridBg", GridLines = "gridLines",
        GridCycle = "gridCycle", RulerBackground = "rulerBg", RulerInactive = "rulerInactive", RulerActive = "rulerActive",
        RulerReverse = "rulerReverse";

    public static readonly IReadOnlyList<ColorRow> Rows =
    [
        new(UserScheme, "Custom Colors", "User Color Scheme", "panel.bg", HasHue: true),
        new(Accent, "Custom Colors", "Accent Color", "button.active", HasHue: true),
        new(GridBackground, "Grid Colors", "Background", "arrange.bg", HasHue: false),
        new(GridLines, "Grid Colors", "Grid Lines", "line.bar", HasHue: false, Requires: GridBackground),
        new(GridCycle, "Grid Colors", "Cycle Region", "cycle.active", HasHue: false, Requires: GridBackground),
        new(RulerBackground, "Ruler Colors", "Background", "header.bg", HasHue: false),
        new(RulerInactive, "Ruler Colors", "Inactive Cycle", "cycle.inactive", HasHue: true),
        new(RulerActive, "Ruler Colors", "Active Cycle", "cycle.active", HasHue: true),
        new(RulerReverse, "Ruler Colors", "Reverse Cycle", "cycle.reverse", HasHue: true),
    ];

    /// <summary>The bars, panels and grid greys: what a scheme and the User Color Scheme tint.</summary>
    public static readonly IReadOnlySet<string> ChromeKeys = new HashSet<string>
    {
        "window.bg", "title.bg", "control.bg", "edit.bg", "status.bg", "footer.bg", "chrome.border", "line.divider", "panel.bg",
        "panel.raised", "button.bg", "button.border", "tool.active", "field.bg", "slider.track", "lcd.bg", "arrange.bg", "header.bg",
        "lane.headA", "lane.headB", "group.head", "toggle.bg", "line.beat", "line.bar", "line.snap", "divider", "overview.bg",
        "volume.bg", "volume.side", "roll.white", "roll.black",
    };

    /// <summary>Text drawn on the chrome: turned dark when the User Color Scheme is made light.</summary>
    private static readonly string[] TextKeys =
    [
        "window.text", "title.text", "status.text", "button.text", "text.secondary", "text.dim", "lane.text", "lane.dim",
        "ruler.bar", "ruler.beat", "lcd.text", "lcd.caption",
    ];

    private static readonly string[] AccentKeys = ["button.active", "button.activeBorder", "tool.activeIcon", "note.selected"];

    /// <summary>A row's colour as the theme draws it now without the user's layer: where its sliders start when ticked.</summary>
    public static Hsb Start(ColorRow row, Func<string, string> baseValue) => Hsb.FromColor(Color.Parse(baseValue(row.MainKey)));

    /// <summary>The theme values for these colours, given the values underneath (the theme without the user layer).</summary>
    public static Dictionary<string, string> Build(UserColors colors, Func<string, string> baseValue)
    {
        var result = new Dictionary<string, string>();
        Hsb Base(string key) => Hsb.FromColor(Color.Parse(result.TryGetValue(key, out var v) ? v : baseValue(key)));
        bool On(string row, out Hsb c) => colors.Rows.TryGetValue(row, out c);
        // Two-slider rows take the scheme's hue (the User Color Scheme's when ticked).
        double schemeHue = On(UserScheme, out var userScheme) ? userScheme.H : Base("panel.bg").H;
        Hsb Tinted(Hsb c) => c with { H = schemeHue };

        if (On(UserScheme, out var scheme))
        {
            // Every grey moves by the same brightness step, so the bars keep their contrast with each other.
            double step = scheme.B - Base("panel.bg").B;
            foreach (var key in ChromeKeys)
                result[key] = new Hsb(scheme.H, scheme.S, Math.Clamp(Base(key).B + step, 0, 1)).ToHex();
            if (Base("panel.bg").B > 0.62)
                foreach (var key in TextKeys)
                    result[key] = key is "text.secondary" or "text.dim" or "lane.dim" or "ruler.beat" or "lcd.caption" ? "#4a4a50" : "#141416";
        }

        if (On(Accent, out var accent))
            foreach (var key in AccentKeys) result[key] = accent.ToHex();

        if (On(GridBackground, out var gridBg))
        {
            var bg = Tinted(gridBg);
            result["arrange.bg"] = result["roll.white"] = bg.ToHex();
            result["roll.black"] = (bg with { B = Math.Max(0, bg.B - 0.03) }).ToHex();
            if (On(GridLines, out var lines))
            {
                var line = Tinted(lines);
                result["line.bar"] = line.ToHex();
                result["line.beat"] = Mix(line, bg, 0.4).ToHex();
                result["line.snap"] = Mix(line, bg, 0.7).ToHex();
            }
            if (On(GridCycle, out var cycle)) result["cycle.grid"] = Tinted(cycle).ToHex(0x40);
        }

        if (On(RulerBackground, out var ruler)) result["header.bg"] = Tinted(ruler).ToHex();
        if (On(RulerInactive, out var inactive)) result["cycle.inactive"] = inactive.ToHex();
        if (On(RulerActive, out var active)) result["cycle.active"] = active.ToHex();
        if (On(RulerReverse, out var reverse)) result["cycle.reverse"] = reverse.ToHex();
        return result;
    }

    /// <summary>Applies the user's colours to a theme: the scheme, then the user layer.</summary>
    public static void Apply(UiTheme theme, UserColors colors, bool darkModeFallback)
    {
        theme.SetScheme(colors.Scheme ?? (darkModeFallback ? UiTheme.SlateScheme : 0)); // Slate is the default look (2026-11-13)
        theme.SetUserColors(Build(colors, theme.BaseValue)); // BaseValue never includes the old user layer
    }

    // Partway from a towards b, in RGB.
    private static Hsb Mix(Hsb a, Hsb b, double t)
    {
        Color ca = a.ToColor(), cb = b.ToColor();
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Hsb.FromColor(Color.FromRgb(L(ca.R, cb.R), L(ca.G, cb.G), L(ca.B, cb.B)));
    }
}

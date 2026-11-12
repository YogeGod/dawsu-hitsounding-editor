using System.Globalization;
using System.Text.Json;
using Avalonia.Media;

namespace OsuHitsound.App;

internal enum SettingKind
{
    Color,
    Number,
    Text,
    ColorList,
}

/// <summary>One editable look setting of a part, e.g. the lane row's height.</summary>
internal sealed record Setting(string Key, string Label, SettingKind Kind, string Default, double Min = 0, double Max = 0);

/// <summary>A part of the UI the inspector can pick and edit: a bar, a control, or a drawn piece of the grid.</summary>
internal sealed record UiPart(string Id, string Group, string Name, IReadOnlyList<Setting> Settings);

/// <summary>
/// The design system's tokens: every colour, size, font size and label of the app, with the Figma design's values
/// (docs/design-system.md) as defaults. DesignSystem.axaml styles every control from these through ThemeResources.
/// Changes made in the inspector are kept in ui-theme.json in the project's source folder, so they become part of the
/// code when committed. Only values that differ from the defaults are written.
/// </summary>
internal sealed class UiTheme
{
    public const string FileName = "ui-theme.json";

    public static readonly IReadOnlyList<UiPart> Parts =
    [
        // Bars, top to bottom
        new("window", "Bars", "Window title bar", [
            C("window.bg", "Background", "#adadad"), C("window.text", "Text", "#252525"),
            N("window.height", "Height", 36, 28, 60), N("window.fontSize", "Font size", 13, 8, 22), C("window.close", "Close under the pointer", "#c42b1c")]),
        new("titlebar", "Bars", "Menu bar", [
            C("title.bg", "Background", "#555555"), C("title.text", "Text", "#c1c7cc"),
            N("title.height", "Height", 40, 16, 60), N("title.fontSize", "Font size", 12, 8, 24)]),
        new("controlbar", "Bars", "Toolbar", [
            C("control.bg", "Background", "#494949"), N("control.height", "Height", 44, 40, 90)]),
        new("editbar", "Bars", "Info bar", [
            C("edit.bg", "Background", "#2b2b2b"), N("edit.height", "Height", 40, 40, 110)]),
        new("statusbar", "Bars", "Transport bar", [
            C("status.bg", "Background", "#383838"), C("status.text", "Text", "#bdbdbd"),
            N("status.height", "Height", 56, 18, 80), N("status.fontSize", "Font size", 12, 8, 22)]),
        new("footer", "Bars", "Footer", [C("footer.bg", "Background", "#2b2b2b")]),
        new("chromeborder", "Bars", "Bar borders", [
            C("chrome.border", "Edges between bars", "#171a1c"), C("line.divider", "Dividers inside bars", "#5e5e5e")]),
        new("panels", "Bars", "Panels", [
            C("panel.bg", "Background", "#383838"), C("panel.raised", "Headers and cards", "#515151")]),

        // Text
        new("text", "Text", "Text colours", [
            C("button.text", "Primary", "#edf0f1"), C("text.secondary", "Secondary", "#c1c7cc"), C("text.dim", "Dim", "#bdbdbd"),
            C("text.onAccent", "On coloured buttons", "#ffffff"), C("brand", "App mark (pink)", "#ff66aa")]),

        // Controls
        new("button", "Controls", "Buttons", [
            C("button.bg", "Background", "#4d4d4d"), C("button.border", "Border", "#414649"),
            C("button.active", "Primary and switched on", "#287ac3"), C("button.activeBorder", "Primary border", "#7358df"),
            N("button.fontSize", "Font size", 12, 9, 22), N("button.height", "Height", 30, 20, 44), N("button.radius", "Corner radius", 4, 0, 12)]),
        new("tools", "Controls", "Tool buttons", [
            C("tool.active", "Selected tool", "#bfc5c8"), C("tool.activeIcon", "Selected tool icon", "#252525")]),
        new("fields", "Controls", "Text boxes", [C("field.bg", "Background", "#2b2b2b")]),
        new("sliders", "Controls", "Sliders", [C("slider.track", "Track", "#5e5e5e"), C("slider.thumb", "Handle", "#c1c7cc")]),
        new("playbutton", "Controls", "Transport buttons", [
            C("play.bg", "Playing", "#1cac46"), C("cycle.bg", "Cycle on", "#b19443"),
            C("record.bg", "Recording", "#c8393d")]),
        new("lcd", "Controls", "Time display", [
            C("lcd.bg", "Background", "#242a32"), C("lcd.text", "Numbers", "#ccdbea"), C("lcd.caption", "Captions", "#afbfce"),
            N("lcd.fontSize", "Number size", 18, 10, 36)]),
        new("labels", "Controls", "Labels", [
            T("label.open", "Open button", "Open…"), T("label.save", "Save button", "Save .osu"),
            T("label.laneDetails", "Lane details button", "Info"), T("label.volume", "Volume button", "Controller")]),

        // Grid
        new("arrange", "Grid", "Arrange background", [C("arrange.bg", "Background", "#383838")]),
        new("header", "Grid", "Header and ruler", [
            C("header.bg", "Background", "#2e2e2e"), C("ruler.bar", "Bar numbers", "#edf0f1"), C("ruler.beat", "Beat numbers", "#bdbdbd"),
            N("ruler.fontSize", "Font size", 12, 8, 20), N("header.sections", "Sections row height", 18, 0, 40)]),
        new("cycle", "Grid", "Cycle range", [
            C("cycle.active", "Ruler band, cycling", "#2f6fd0"), C("cycle.inactive", "Ruler band, cycle off", "#8e8e93"),
            C("cycle.reverse", "Ruler band, R before L (skips)", "#ff453a"),
            C("cycle.grid", "Grid between L and R (transparent: none)", "#00000000")]),
        new("sections", "Grid", "Song sections", [C("section.fill", "Fill", "#8a6d1e"), N("section.opacity", "Opacity", 0.55, 0, 1)]),
        new("waveform", "Grid", "Song waveform", [
            C("wave.base", "Loudness", "#8fb7e8"), C("wave.low", "Low: kicks, bass", "#4f8dff"), C("wave.mid", "Mid: snares, vocals", "#c27bff"),
            C("wave.high", "High: hi-hats, cymbals", "#ffd166"), N("wave.opacity", "Opacity", 0.85, 0, 1)]),
        new("lanelist", "Grid", "Lane list", [
            N("list.width", "Width", 224, 160, 600), C("lane.text", "Name text", "#edf0f1"), C("lane.dim", "Index text", "#bdbdbd"),
            N("lane.fontSize", "Font size", 12, 8, 22), C("lane.missing", "Missing / no sound", "#ff9a92")]),
        new("lanerow", "Grid", "Lane rows", [
            N("row.height", "Height", 31, 16, 64), C("lane.headA", "List colour (odd)", "#515151"), C("lane.headB", "List colour (even)", "#4a4a4a"),
            N("lane.tint", "Grid tint opacity", 0.09, 0, 1)]),
        new("grouprow", "Grid", "Group rows", [
            C("group.head", "List colour", "#424242"), N("group.tint", "Grid tint opacity", 0.10, 0, 1)]),
        new("lanecolours", "Grid", "Lane colours", [
            L("colors.soft", "Soft set", "#c04fd0,#a24fd6,#c24dc4,#d0499e,#d4507a"),
            L("colors.normal", "Normal set", "#3fb5b0,#3fb5b0,#2fa38f,#4cc3d1"),
            L("colors.drum", "Drum set", "#d98a2b,#d9772b,#c9a42a"),
            L("colors.own", "Own files", "#4cb05e,#4cb05e,#7cbf3a,#3a9e7a"),
            L("colors.skin", "Skin (no file)", "#3d8fd1,#3d8fd1"),
            C("colors.muted", "Muted lane", "#5a6a7a")]),
        new("toggles", "Grid", "M / S buttons", [
            C("toggle.bg", "Off", "#4d4d4d"), C("toggle.mute", "Mute on", "#3d8fd1"), C("toggle.solo", "Solo on", "#ffcc33")]),
        new("note", "Grid", "Notes", [
            N("note.size", "Size", 15, 5, 30), C("note.ring", "Ring", "#111113"), C("note.selected", "Selected ring", "#73b7ff"),
            N("note.brighten", "Brighten lane colour", 0.3, 0, 1)]),
        new("gridlines", "Grid", "Beat and bar lines", [C("line.beat", "Beat lines", "#4d4d4d"), C("line.bar", "Bar lines", "#5e5e5e"), C("line.snap", "Snap lines", "#424242")]),
        new("pianoroll", "Grid", "Key editor rows", [
            C("roll.white", "White-key rows", "#333333"), C("roll.black", "Black-key rows", "#2b2b2b")]),
        new("playhead", "Grid", "Playhead", [C("playhead.color", "Colour", "#ffffff"), N("playhead.width", "Width", 2, 1, 6)]),
        new("divider", "Grid", "Separators", [C("divider", "Colour", "#171a1c")]),
        new("overview", "Grid", "Overview strip", [C("overview.bg", "Background", "#2b2b2b"), N("overview.height", "Height", 20, 12, 80)]),
        new("volume", "Grid", "Volume panel", [
            C("volume.bg", "Background", "#2e2e2e"), C("volume.side", "Side panel", "#383838"), N("volume.height", "Height", 160, 60, 400)]),
    ];

    private static readonly Dictionary<string, Setting> ByKey = Parts.SelectMany(p => p.Settings).ToDictionary(s => s.Key);

    private readonly Dictionary<string, string> values = [];
    private readonly Dictionary<string, object> cache = [];

    /// <summary>The theme the app draws with.</summary>
    public static UiTheme Current { get; set; } = new();

    /// <summary>Raised after any value changes, so the window and drawn parts refresh.</summary>
    public event Action? Changed;

    // Switching the palette never rewrites user theme customizations, layout or map state.
    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["window.bg"] = "#1b1b1e", ["window.text"] = "#9999a2",
        ["title.bg"] = "#1b1b1e", ["title.text"] = "#9999a2", ["control.bg"] = "#27272b",
        ["edit.bg"] = "#1b1b1e", ["status.bg"] = "#1b1b1e", ["status.text"] = "#9999a2",
        ["footer.bg"] = "#1b1b1e", ["chrome.border"] = "#333338", ["line.divider"] = "#333338",
        ["panel.bg"] = "#1b1b1e", ["panel.raised"] = "#27272b", ["button.text"] = "#f4f4f6",
        ["text.secondary"] = "#9999a2", ["text.dim"] = "#9999a2", ["button.bg"] = "#27272b",
        ["button.border"] = "#333338", ["button.active"] = "#0787f8", ["button.activeBorder"] = "#0787f8",
        ["tool.active"] = "#202e34", ["tool.activeIcon"] = "#72b9ff", ["field.bg"] = "#111113",
        ["slider.track"] = "#414147", ["slider.thumb"] = "#f4f4f6", ["play.bg"] = "#08ad18",
        ["lcd.bg"] = "#111113", ["lcd.text"] = "#f4f4f6", ["lcd.caption"] = "#9999a2",
        ["arrange.bg"] = "#111113", ["header.bg"] = "#27272b", ["ruler.bar"] = "#f4f4f6",
        ["ruler.beat"] = "#9999a2", ["lane.text"] = "#f4f4f6", ["lane.dim"] = "#9999a2",
        ["lane.headA"] = "#1b1b1e", ["lane.headB"] = "#111113", ["group.head"] = "#27272b",
        ["toggle.bg"] = "#27272b", ["line.beat"] = "#333338", ["line.bar"] = "#414147",
        ["line.snap"] = "#242427", ["divider"] = "#333338", ["overview.bg"] = "#1b1b1e",
        ["volume.bg"] = "#111113", ["volume.side"] = "#1b1b1e", ["roll.white"] = "#1b1b1e", ["roll.black"] = "#111113",
    };

    /// <summary>The colour scheme (Preferences, Colours): 0 grey, 1 dark, 2 blue-grey dark, 3 teal dark.</summary>
    public int Scheme { get; private set; }

    public const int SchemeCount = 4;

    public bool DarkMode => Scheme != 0;

    public void SetDarkMode(bool dark) => SetScheme(dark ? 1 : 0);

    public void SetScheme(int scheme)
    {
        scheme = Math.Clamp(scheme, 0, SchemeCount - 1);
        if (Scheme == scheme) return;
        Scheme = scheme;
        cache.Clear();
        Changed?.Invoke();
    }

    // The two tinted dark schemes: the dark palette's greys, tinted (hue, saturation, brightness lift), so their panels
    // match Cubase's second and fourth Default Color Schemes swatches (#273137 and #284c5a).
    private static readonly (double Hue, double Saturation, double Lift)[] SchemeTints = [(0, 0, 0), (0, 0, 0), (202, 0.29, 0.05), (197, 0.56, 0.18)];
    private static readonly Dictionary<int, IReadOnlyDictionary<string, string>> SchemePalettes = [];

    private static IReadOnlyDictionary<string, string>? SchemePalette(int scheme)
    {
        if (scheme == 0) return null;
        if (scheme == 1) return DarkPalette;
        lock (SchemePalettes)
        {
            if (SchemePalettes.TryGetValue(scheme, out var made)) return made;
            var (hue, saturation, lift) = SchemeTints[scheme];
            // Only the greys are tinted: blue buttons, green Play and the text keep their colours.
            var palette = DarkPalette.ToDictionary(kv => kv.Key, kv => Hsb.FromColor(Avalonia.Media.Color.Parse(kv.Value)) is var c && c.S < 0.12 && ThemeColors.ChromeKeys.Contains(kv.Key)
                ? new Hsb(hue, saturation, Math.Min(1, c.B + lift)).ToHex() : kv.Value);
            return SchemePalettes[scheme] = palette;
        }
    }

    /// <summary>A scheme's panel colour, for its swatch in Preferences.</summary>
    public static Color SchemeSwatch(int scheme) =>
        Avalonia.Media.Color.Parse(SchemePalette(scheme) is { } p && p.TryGetValue("panel.raised", out var v) ? v : ByKey["panel.raised"].Default);

    // The user's colours (Preferences, Colours), above everything else. They are not written to ui-theme.json.
    private IReadOnlyDictionary<string, string> userColors = new Dictionary<string, string>();

    /// <summary>Replaces the user's colour layer: each key's value wins over the theme file and the scheme.</summary>
    public void SetUserColors(IReadOnlyDictionary<string, string> colors)
    {
        userColors = colors.Where(kv => ByKey.TryGetValue(kv.Key, out var s) && IsValid(s, kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);
        cache.Clear();
        Changed?.Invoke();
    }

    public string Get(string key) => userColors.TryGetValue(key, out var user) ? user : BaseValue(key);

    /// <summary>The value without the user's colour layer: the theme file, else the scheme, else the default.</summary>
    public string BaseValue(string key) => values.TryGetValue(key, out var v) ? v
        : SchemePalette(Scheme) is { } palette && palette.TryGetValue(key, out var schemed) ? schemed : ByKey[key].Default;

    public void Set(string key, string value)
    {
        var setting = ByKey[key];
        if (!IsValid(setting, value)) throw new FormatException($"\"{value}\" is not a valid {setting.Kind.ToString().ToLowerInvariant()} for {setting.Label}.");
        if (value == setting.Default && !DarkPalette.ContainsKey(key)) values.Remove(key);
        else values[key] = value;
        cache.Clear();
        Changed?.Invoke();
    }

    public void Reset(string key)
    {
        _ = ByKey[key];
        values.Remove(key);
        cache.Clear();
        Changed?.Invoke();
    }

    public Color Color(string key) => Cached(key, () => Avalonia.Media.Color.Parse(Get(key)));

    public double Number(string key) => Cached(key, () => double.Parse(Get(key), CultureInfo.InvariantCulture));

    public Color[] Colors(string key) => Cached(key, () => Get(key).Split(',').Select(c => Avalonia.Media.Color.Parse(c.Trim())).ToArray());

    /// <summary>A value derived from settings (a brush, a pen), rebuilt after any change.</summary>
    public T Cached<T>(string key, Func<T> make) where T : notnull
    {
        if (cache.TryGetValue(key, out var v) && v is T typed) return typed;
        var made = make();
        cache[key] = made;
        return made;
    }

    public static bool IsValid(Setting setting, string value) => setting.Kind switch
    {
        SettingKind.Color => Avalonia.Media.Color.TryParse(value, out _),
        SettingKind.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                              && d >= setting.Min && d <= setting.Max,
        SettingKind.ColorList => value.Split(',') is { Length: > 0 } parts && parts.All(p => Avalonia.Media.Color.TryParse(p.Trim(), out _)),
        _ => value.Length is > 0 and <= 60,
    };

    /// <summary>Loads changed values from JSON; unknown keys and invalid values are ignored.</summary>
    public static UiTheme FromJson(string json)
    {
        var theme = new UiTheme();
        // Values may be strings or (hand-edited) bare numbers.
        var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        foreach (var (key, element) in map)
            if (ByKey.TryGetValue(key, out var setting) && element.ValueKind is JsonValueKind.String or JsonValueKind.Number
                && (element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText()) is var value
                && IsValid(setting, value) && (value != setting.Default || DarkPalette.ContainsKey(key)))
                theme.values[key] = value;
        return theme;
    }

    public string ToJson() =>
        JsonSerializer.Serialize(new SortedDictionary<string, string>(values, StringComparer.Ordinal), new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// Where the theme file lives: src/OsuHitsound.App/ui-theme.json in the repository (found by walking up to the
    /// .slnx), so saved changes land in the source; next to the program when it runs outside the repository.
    /// </summary>
    public static string DefaultPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (dir.EnumerateFiles("*.slnx").Any())
                return Path.Combine(dir.FullName, "src", "OsuHitsound.App", FileName);
        return Path.Combine(AppContext.BaseDirectory, FileName);
    }

    public static UiTheme Load(string path)
    {
        try
        {
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new UiTheme();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UiTheme();
        }
    }

    public void Save(string path)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, ToJson() + Environment.NewLine);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Copies every value from another theme (Revert).</summary>
    public void ReplaceWith(UiTheme other)
    {
        values.Clear();
        foreach (var (k, v) in other.values) values[k] = v;
        cache.Clear();
        Changed?.Invoke();
    }

    private static Setting C(string key, string label, string value) => new(key, label, SettingKind.Color, value);

    private static Setting N(string key, string label, double value, double min, double max) =>
        new(key, label, SettingKind.Number, value.ToString(CultureInfo.InvariantCulture), min, max);

    private static Setting T(string key, string label, string value) => new(key, label, SettingKind.Text, value);

    private static Setting L(string key, string label, string value) => new(key, label, SettingKind.ColorList, value);
}

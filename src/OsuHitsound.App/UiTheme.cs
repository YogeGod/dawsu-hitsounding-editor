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
/// Every colour, size, font size and label of the main screen, with design G's values (docs/ui-design.md) as defaults.
/// Changes made in the inspector are kept in ui-theme.json in the project's source folder, so they become part of the
/// code when committed. Only values that differ from the defaults are written.
/// </summary>
internal sealed class UiTheme
{
    public const string FileName = "ui-theme.json";

    public static readonly IReadOnlyList<UiPart> Parts =
    [
        // Bars
        new("titlebar", "Bars", "Title bar", [
            C("title.bg", "Background", "#d6d6da"), C("title.text", "Text", "#1d1d1f"),
            N("title.height", "Height", 24, 16, 60), N("title.fontSize", "Font size", 11, 8, 24)]),
        new("controlbar", "Bars", "Control bar", [
            C("control.bg", "Background", "#d6d6da"), N("control.height", "Height", 46, 30, 90)]),
        new("editbar", "Bars", "Edit bar", [
            C("edit.bg", "Background", "#d6d6da"), N("edit.height", "Height", 34, 24, 70)]),
        new("statusbar", "Bars", "Status bar", [
            C("status.bg", "Background", "#d6d6da"), C("status.text", "Text", "#1d1d1f"),
            N("status.height", "Height", 26, 18, 60), N("status.fontSize", "Font size", 12, 8, 22)]),
        new("chromeborder", "Bars", "Bar borders", [C("chrome.border", "Colour", "#a9a9ae")]),

        // Controls
        new("button", "Controls", "Buttons", [
            C("button.bg", "Background", "#c3c3c8"), C("button.text", "Text", "#1d1d1f"),
            C("button.active", "Switched on", "#2f6fd0"), N("button.fontSize", "Font size", 13, 9, 22)]),
        new("playbutton", "Controls", "Play button", [C("play.icon", "Icon", "#2f9a3a")]),
        new("lcd", "Controls", "Time display", [
            C("lcd.bg", "Background", "#161618"), C("lcd.text", "Numbers", "#e8eef6"), C("lcd.caption", "Captions", "#8e8e93"),
            N("lcd.fontSize", "Number size", 19, 10, 36)]),
        new("labels", "Controls", "Labels", [
            T("label.open", "Open button", "Open .osu…"), T("label.save", "Save button", "Save .osu"),
            T("label.laneDetails", "Lane details button", "Lane details"), T("label.volume", "Volume button", "Volume lane")]),

        // Grid
        new("arrange", "Grid", "Arrange background", [C("arrange.bg", "Background", "#262628")]),
        new("header", "Grid", "Header and ruler", [
            C("header.bg", "Background", "#2f2f31"), C("ruler.bar", "Bar numbers", "#e4e4e6"), C("ruler.beat", "Beat numbers", "#a4a4a9"),
            N("ruler.fontSize", "Font size", 11, 8, 20), N("header.sections", "Sections row height", 18, 0, 40)]),
        new("sections", "Grid", "Song sections", [C("section.fill", "Fill", "#8a6d1e"), N("section.opacity", "Opacity", 0.55, 0, 1)]),
        new("numbers", "Grid", "Number column", [N("numbers.width", "Width", 28, 16, 60)]),
        new("lanelist", "Grid", "Lane list", [
            N("list.width", "Width", 300, 160, 600), C("lane.text", "Name text", "#e4e4e6"), C("lane.dim", "Index text", "#a4a4a9"),
            N("lane.fontSize", "Font size", 12, 8, 22), C("lane.missing", "Missing / no sound", "#ff9a92")]),
        new("lanerow", "Grid", "Lane rows", [
            N("row.height", "Height", 22, 16, 64), C("lane.headA", "List colour (odd)", "#343437"), C("lane.headB", "List colour (even)", "#323235"),
            N("lane.tint", "Grid tint opacity", 0.22, 0, 1)]),
        new("grouprow", "Grid", "Group rows", [
            C("group.head", "List colour", "#39393c"), N("group.tint", "Grid tint opacity", 0.10, 0, 1)]),
        new("lanecolours", "Grid", "Lane colours", [
            L("colors.soft", "Soft set", "#c04fd0,#a24fd6,#c24dc4,#d0499e,#d4507a"),
            L("colors.normal", "Normal set", "#3fb5b0,#3fb5b0,#2fa38f,#4cc3d1"),
            L("colors.drum", "Drum set", "#d98a2b,#d9772b,#c9a42a"),
            L("colors.own", "Own files", "#4cb05e,#4cb05e,#7cbf3a,#3a9e7a"),
            L("colors.skin", "Skin (no file)", "#3d8fd1,#3d8fd1"),
            C("colors.muted", "Muted lane", "#5a6a7a")]),
        new("toggles", "Grid", "M / S buttons", [
            C("toggle.bg", "Off", "#48484c"), C("toggle.mute", "Mute on", "#3d8fd1"), C("toggle.solo", "Solo on", "#ffcc33")]),
        new("note", "Grid", "Notes", [
            N("note.size", "Size", 11, 5, 30), C("note.ring", "Ring", "#111113"), C("note.selected", "Selected ring", "#2f6fd0"),
            N("note.brighten", "Brighten lane colour", 0.25, 0, 1)]),
        new("gridlines", "Grid", "Beat and bar lines", [C("line.beat", "Beat lines", "#3e3e42"), C("line.bar", "Bar lines", "#5a5a5f")]),
        new("playhead", "Grid", "Playhead", [C("playhead.color", "Colour", "#ffffff"), N("playhead.width", "Width", 2, 1, 6)]),
        new("divider", "Grid", "Separators", [C("divider", "Colour", "#1c1c1e")]),
        new("overview", "Grid", "Overview strip", [C("overview.bg", "Background", "#2b2b2d"), N("overview.height", "Height", 30, 12, 80)]),
        new("volume", "Grid", "Volume panel", [
            C("volume.bg", "Background", "#212123"), C("volume.side", "Side panel", "#2f2f31"), N("volume.height", "Height", 150, 60, 400)]),
    ];

    private static readonly Dictionary<string, Setting> ByKey = Parts.SelectMany(p => p.Settings).ToDictionary(s => s.Key);

    private readonly Dictionary<string, string> values = [];
    private readonly Dictionary<string, object> cache = [];

    /// <summary>The theme the app draws with.</summary>
    public static UiTheme Current { get; set; } = new();

    /// <summary>Raised after any value changes, so the window and drawn parts refresh.</summary>
    public event Action? Changed;

    public string Get(string key) => values.TryGetValue(key, out var v) ? v : ByKey[key].Default;

    public void Set(string key, string value)
    {
        var setting = ByKey[key];
        if (!IsValid(setting, value)) throw new FormatException($"\"{value}\" is not a valid {setting.Kind.ToString().ToLowerInvariant()} for {setting.Label}.");
        if (value == setting.Default) values.Remove(key);
        else values[key] = value;
        cache.Clear();
        Changed?.Invoke();
    }

    public void Reset(string key) => Set(key, ByKey[key].Default);

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
                && IsValid(setting, value) && value != setting.Default)
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

using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Avalonia.Input;

namespace OsuHitsound.App;

/// <summary>Which key set the editor starts from: the user's Cubase Key Commands file, or osu!'s editor keys.</summary>
internal enum KeyPreset { Cubase, Osu }

/// <summary>
/// Reads Cubase's "Key Commands.xml" (%AppData%\Steinberg\Cubase N_64): every category/command pair with its keys,
/// e.g. "Edit|Undo" → Ctrl+Z. Keys Avalonia can't tell apart (Return and numpad Enter) become the same gesture.
/// </summary>
internal static partial class CubaseKeyFile
{
    /// <summary>Editor command → the Cubase command whose keys it takes. Cubase commands not listed here are ignored.</summary>
    public static readonly IReadOnlyDictionary<string, string> CommandMap = new Dictionary<string, string>
    {
        ["open"] = "File|Open",
        ["save"] = "File|Save",
        ["play"] = "Transport|StartStop",
        ["start"] = "Transport|Return to Zero",
        ["stop"] = "Transport|Stop",
        ["cycle"] = "Transport|Cycle",
        ["locatorsToSelection"] = "Transport|Locators to Selection",
        ["playLocatorRange"] = "Transport|Loop Selection",
        ["jumpLeft"] = "Transport|To Left Locator",
        ["jumpRight"] = "Transport|To Right Locator",
        ["setLeft"] = "Transport|Set Left Locator",
        ["setRight"] = "Transport|Set Right Locator",
        ["toolSelect"] = "Tool|Select Tool",
        ["toolDraw"] = "Tool|Draw Tool",
        ["toolErase"] = "Tool|Delete Tool",
        ["toolMute"] = "Tool|Mute Tool",
        ["snap"] = "Edit|Snap On/Off",
        ["undo"] = "Edit|Undo",
        ["redo"] = "Edit|Redo",
        ["copy"] = "Edit|Copy",
        ["cut"] = "Edit|Cut",
        ["paste"] = "Edit|Paste",
        ["duplicate"] = "Edit|Duplicate",
        ["selectAll"] = "Edit|Select All",
        ["deselect"] = "Edit|Select None",
        ["delete"] = "Edit|Delete",
        ["nudgeLeft"] = "Nudge|Left",
        ["nudgeRight"] = "Nudge|Right",
        ["zoomIn"] = "Zoom|Zoom In",
        ["zoomOut"] = "Zoom|Zoom Out",
        ["filter"] = "Edit|Find Track/Channel",
        ["addBookmark"] = "Transport|Insert Marker",
        ["previousBookmark"] = "Transport|Locate Previous Marker",
        ["nextBookmark"] = "Transport|Locate Next Marker",
    };

    /// <summary>The newest Cubase's key file under %AppData%\Steinberg, or null when there is none.</summary>
    public static string? FindNewest(string? steinbergFolder = null)
    {
        steinbergFolder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Steinberg");
        if (!Directory.Exists(steinbergFolder)) return null;
        return Directory.EnumerateDirectories(steinbergFolder, "Cubase *")
            .Select(dir => (dir, match: CubaseFolder().Match(Path.GetFileName(dir))))
            .Where(d => d.match.Success && File.Exists(Path.Combine(d.dir, "Key Commands.xml")))
            .OrderByDescending(d => Version.TryParse(d.match.Groups[1].Value.Contains('.') ? d.match.Groups[1].Value : d.match.Groups[1].Value + ".0", out var v) ? v : new Version())
            .Select(d => Path.Combine(d.dir, "Key Commands.xml"))
            .FirstOrDefault();
    }

    /// <summary>"Category|Command" → its keys, for every command with at least one key Avalonia understands.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> Read(string path)
    {
        // The Keys tab rebuilds the keymap on every edit; parse the ~340 KB file again only when it changes on disk.
        var stamp = (Path.GetFullPath(path), File.GetLastWriteTimeUtc(path));
        if (cache is { } c && c.Stamp == stamp) return c.Keys;
        if (!File.Exists(path)) throw new FileNotFoundException($"{path} doesn't exist.");

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(path, settings);
        var keys = Parse(XDocument.Load(reader));
        cache = (stamp, keys);
        return keys;
    }

    private static ((string Path, DateTime Written) Stamp, IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> Keys)? cache;

    /// <summary>Makes the next <see cref="Read"/> parse the file again, even if its write time is unchanged.</summary>
    public static void Forget() => cache = null;

    public static IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> Parse(XDocument doc)
    {
        static string? Named(XElement e, string name) =>
            e.Elements("string").FirstOrDefault(s => (string?)s.Attribute("name") == name)?.Attribute("value")?.Value;

        var result = new Dictionary<string, IReadOnlyList<KeyGesture>>();
        var categories = doc.Root?.Elements("list").FirstOrDefault(l => (string?)l.Attribute("name") == "Categories");
        foreach (var category in categories?.Elements("item") ?? [])
        {
            var commands = category.Elements("list").FirstOrDefault(l => (string?)l.Attribute("name") == "Commands");
            foreach (var command in commands?.Elements("item") ?? [])
            {
                // One key is a <string name="Key">, several are a <list name="Key"> of <item value>.
                var texts = command.Elements("string").Where(s => (string?)s.Attribute("name") == "Key").Select(s => (string?)s.Attribute("value"))
                    .Concat(command.Elements("list").Where(l => (string?)l.Attribute("name") == "Key").Elements("item").Select(i => (string?)i.Attribute("value")));
                var keys = texts.Select(t => t is null ? null : ParseKey(t)).OfType<KeyGesture>().Distinct().ToList();
                if (keys.Count > 0) result[$"{Named(category, "Name")}|{Named(command, "Name")}"] = keys;
            }
        }

        return result;
    }

    /// <summary>A Cubase key text like "Ctrl+Shift+Pad +" or "Alt+Left Arrow"; null for keys Avalonia has no name for.</summary>
    public static KeyGesture? ParseKey(string text)
    {
        var mods = KeyModifiers.None;
        while (true)
        {
            // "Pad +" ends in a plus, so take modifiers off the front rather than splitting on '+'.
            if (text.StartsWith("Ctrl+", StringComparison.Ordinal)) (mods, text) = (mods | KeyModifiers.Control, text[5..]);
            else if (text.StartsWith("Shift+", StringComparison.Ordinal)) (mods, text) = (mods | KeyModifiers.Shift, text[6..]);
            else if (text.StartsWith("Alt+", StringComparison.Ordinal)) (mods, text) = (mods | KeyModifiers.Alt, text[4..]);
            else break;
        }

        Key? key = text switch
        {
            [>= 'A' and <= 'Z'] => Key.A + (text[0] - 'A'),
            [>= '0' and <= '9'] => Key.D0 + (text[0] - '0'),
            ['P', 'a', 'd', >= '0' and <= '9'] => Key.NumPad0 + (text[3] - '0'),
            ['F', ..] when int.TryParse(text[1..], out int f) && f is >= 1 and <= 24 => Key.F1 + (f - 1),
            "Pad /" => Key.Divide,
            "Pad *" => Key.Multiply,
            "Pad -" => Key.Subtract,
            "Pad +" => Key.Add,
            "Pad ." => Key.Decimal,
            "," => Key.OemComma,
            "." => Key.OemPeriod,
            "-" => Key.OemMinus,
            "/" => Key.Oem2,
            "[" => Key.OemOpenBrackets,
            "]" => Key.OemCloseBrackets,
            "Space" => Key.Space,
            "Return" or "Enter" => Key.Enter,
            "Tab" => Key.Tab,
            "Esc" or "Escape" => Key.Escape,
            "Del" => Key.Delete,
            "Backspace" => Key.Back,
            "Insert" => Key.Insert,
            "Home" => Key.Home,
            "End" => Key.End,
            "PgUp" => Key.PageUp,
            "PgDown" => Key.PageDown,
            "Left Arrow" => Key.Left,
            "Right Arrow" => Key.Right,
            "Up Arrow" => Key.Up,
            "Down Arrow" => Key.Down,
            _ => null,
        };
        return key is { } k ? new KeyGesture(k, mods) : null;
    }

    [GeneratedRegex(@"^Cubase (?:.* )?([0-9.]+)_64$")]
    private static partial Regex CubaseFolder();
}

/// <summary>
/// The keys in force: a preset, then the built-in keys for commands the preset leaves without one (when no preset key
/// already uses them), then the user's own changes, which take their keys away from any other command.
/// </summary>
internal static class Keymap
{
    /// <summary>osu!stable editor keys that differ from the built-in ones; everything else keeps its built-in keys.</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> OsuKeys = new Dictionary<string, IReadOnlyList<KeyGesture>>
    {
        ["play"] = [new(Key.Space), new(Key.X)],
        ["toolDraw"] = [new(Key.D2)],
    };

    /// <param name="cubase">The Cubase file's keys; null when the preset is osu! or the file couldn't be read.</param>
    /// <param name="overrides">Command id → its keys as changed by the user (empty: no key).</param>
    public static Dictionary<string, IReadOnlyList<KeyGesture>> Build(
        KeyPreset preset, IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>>? cubase, IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> overrides)
    {
        var keys = new Dictionary<string, IReadOnlyList<KeyGesture>>();
        var used = new HashSet<KeyGesture>();
        void Take(string id, IEnumerable<KeyGesture> gestures)
        {
            var free = gestures.Where(g => used.Add(g)).ToList();
            if (free.Count > 0) keys[id] = free;
        }

        if (preset == KeyPreset.Osu)
            foreach (var (id, gestures) in OsuKeys) Take(id, gestures);
        else if (cubase is not null)
            // Only Cubase commands this editor has claim keys; the rest of the file's keys stay free for built-in ones.
            foreach (var (id, cubaseCommand) in CubaseKeyFile.CommandMap)
                if (cubase.TryGetValue(cubaseCommand, out var gestures)) Take(id, gestures);

        foreach (var command in EditorCommands.Defaults.Where(c => !keys.ContainsKey(c.Id)))
            Take(command.Id, command.Keys);

        foreach (var (id, gestures) in overrides)
        {
            var taken = gestures.ToHashSet();
            foreach (var other in keys.Keys.ToList())
                keys[other] = [.. keys[other].Where(g => !taken.Contains(g))];
            keys[id] = gestures;
        }

        return keys;
    }

    /// <summary>The command other than <paramref name="id"/> that has this key, if any.</summary>
    public static string? Owner(IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> keys, KeyGesture gesture, string id) =>
        keys.FirstOrDefault(k => k.Key != id && k.Value.Contains(gesture)).Key;

    /// <summary>The overrides after adding a key to a command; the key is taken away from whichever command had it.</summary>
    public static Dictionary<string, List<string>> AddKey(
        IReadOnlyDictionary<string, List<string>> overrides, IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> keys, string id, KeyGesture gesture)
    {
        var result = overrides.ToDictionary(o => o.Key, o => o.Value.ToList());
        if (Owner(keys, gesture, id) is { } owner)
            result[owner] = [.. keys[owner].Where(g => g != gesture).Select(Format)];
        result[id] = [.. keys.GetValueOrDefault(id, []).Append(gesture).Distinct().Select(Format)];
        return result;
    }

    /// <summary>A gesture as stored in preferences: "Control,Shift+D1". Avalonia's own text form isn't parsed back reliably.</summary>
    public static string Format(KeyGesture g) => $"{g.KeyModifiers}+{g.Key}";

    public static KeyGesture? ParseStored(string text)
    {
        int plus = text.LastIndexOf('+');
        return plus > 0 && Enum.TryParse<KeyModifiers>(text[..plus], out var mods) && Enum.TryParse<Key>(text[(plus + 1)..], out var key)
            ? new KeyGesture(key, mods)
            : null;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> ParseOverrides(IReadOnlyDictionary<string, List<string>>? stored) =>
        (stored ?? new Dictionary<string, List<string>>()).ToDictionary(o => o.Key, o => (IReadOnlyList<KeyGesture>)[.. o.Value.Select(ParseStored).OfType<KeyGesture>()]);

    /// <summary>The problem <see cref="For"/> reports when no file was chosen and none was found: normal without Cubase.</summary>
    public const string NoCubaseFile = "No Cubase key file found; using the built-in keys.";

    /// <summary>The keys for these preferences, with a note when the Cubase file couldn't be read (the built-in keys are used then).</summary>
    public static (Dictionary<string, IReadOnlyList<KeyGesture>> Keys, string? Problem) For(Preferences prefs)
    {
        IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>>? cubase = null;
        string? problem = null;
        if (prefs.KeyPreset == KeyPreset.Cubase)
        {
            var path = prefs.CubaseKeyFile ?? CubaseKeyFile.FindNewest();
            if (path is null) problem = NoCubaseFile;
            else
            {
                try
                {
                    cubase = CubaseKeyFile.Read(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or ArgumentException or NotSupportedException)
                {
                    problem = $"Couldn't read the Cubase key file: {ex.Message}";
                }
            }
        }

        return (Build(prefs.KeyPreset, cubase, ParseOverrides(prefs.KeyOverrides.GetValueOrDefault(prefs.KeyPreset.ToString()))), problem);
    }
}

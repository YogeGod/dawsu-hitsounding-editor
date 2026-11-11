using System.Collections.Immutable;

namespace OsuHitsound.Lanes;

/// <summary>One articulation as a drum plug-in lists it: its group ("Snare"), its item ("Flam", "Rack Tom 1: Hit") and
/// its MIDI note (null when the plug-in leaves it unmapped).</summary>
public sealed record DrumKitArticulation(string Group, string Item, int? Note);

/// <summary>An instrument row dawsu! makes from a kit: the lane's name, its MIDI note and the osu! sound it goes to.</summary>
public sealed record DrumKitRow(string Name, int Note, string Sound);

/// <summary>
/// Builds a drum map from a drum plug-in's own note mapping: one row per mapped articulation, named the way a hitsounder
/// reads it ("Kick", not "Kick: Hit"), with chokes left out unless asked for.
/// </summary>
public static class DrumKitMapping
{
    /// <summary>The kit's articulations: dawsu!'s copy of a known plug-in's default mapping, or null for an unknown one.</summary>
    public static IReadOnlyList<DrumKitArticulation>? Known(string pluginName) =>
        pluginName.Contains("Matt Garstka", StringComparison.OrdinalIgnoreCase) ? GgdMattGarstka : null;

    /// <summary>dawsu!'s own copies of kit mappings, by name, to choose from when a plug-in (Kontakt, say) can't be read.</summary>
    // A property, not a field initializer: static fields initialize in file order, and the tables are declared below.
    public static IReadOnlyDictionary<string, IReadOnlyList<DrumKitArticulation>> BuiltIn => new Dictionary<string, IReadOnlyList<DrumKitArticulation>>
    {
        ["GGD Matt Garstka Signature Drums (GGD preset)"] = GgdMattGarstka,
    };

    /// <summary>Articulations from the names a plug-in reports for its notes (VST 3 pitch names), in note order.</summary>
    public static IReadOnlyList<DrumKitArticulation> FromNoteNames(IReadOnlyDictionary<int, string> names) =>
        [.. names.Where(n => n.Key is >= 0 and <= 127 && !string.IsNullOrWhiteSpace(n.Value)).OrderBy(n => n.Key)
            .Select(n => new DrumKitArticulation("", n.Value.Trim(), n.Key))];

    public static bool IsChoke(DrumKitArticulation a) => a.Item.Contains("choke", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The lane name: "Hit" alone is its group ("Kick"), and "Choke" alone is "China Choke"; "Rack Tom 1: Hit" is
    /// "Rack Tom 1"; "Main Crash L: Choke" is "Main Crash L Choke"; any other articulation keeps its own name ("Flam").
    /// </summary>
    public static string Label(DrumKitArticulation a)
    {
        string item = a.Item.Trim();
        if (item.Equals("Hit", StringComparison.OrdinalIgnoreCase)) return Title(a.Group);
        if (item.Equals("Choke", StringComparison.OrdinalIgnoreCase)) return $"{Title(a.Group)} Choke".Trim();
        int colon = item.LastIndexOf(':');
        if (colon > 0)
        {
            string head = item[..colon].Trim(), tail = item[(colon + 1)..].Trim();
            return tail.Equals("Hit", StringComparison.OrdinalIgnoreCase) ? head : $"{head} {tail}";
        }
        return item;
    }

    /// <summary>
    /// The osu! sound an instrument goes to, as hitsounders usually map a kit: kick and toms on hitnormal, the snare and
    /// its articulations on hitclap, hats and ride on hitwhistle, crashes, splashes, china and stack on hitfinish.
    /// </summary>
    public static string Sound(DrumKitArticulation a)
    {
        string text = (a.Group + " " + a.Item).ToLowerInvariant();
        bool Has(params string[] words) => words.Any(text.Contains);
        if (Has("crash", "splash", "china", "stack", "cymbal")) return "hitfinish";
        if (Has("hat", "ride", "bell", "tip", "edge", "open", "pedal", "cc")) return "hitwhistle";
        if (Has("snare", "flam", "ruff", "rim", "sidestick", "side stick", "wires", "stick", "clap", "aux")) return "hitclap";
        return "hitnormal"; // kick, toms and anything unrecognised
    }

    /// <summary>The rows to make, in the kit's order: mapped notes only, chokes only when asked, one row per name.</summary>
    public static IReadOnlyList<DrumKitRow> Rows(IEnumerable<DrumKitArticulation> kit, bool includeChokes)
    {
        var rows = new List<DrumKitRow>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in kit)
        {
            if (a.Note is not (>= 0 and <= 127) || !includeChokes && IsChoke(a)) continue;
            string name = Label(a);
            if (name.Length is 0 or > 128) continue;
            // Two articulations with one name (e.g. plug-in names repeated across groups) get the group to tell them apart.
            if (!names.Add(name) && !(a.Group.Length > 0 && names.Add(name = $"{Title(a.Group)} {name}"))) continue;
            rows.Add(new(name, a.Note.Value, Sound(a)));
        }
        return rows;
    }

    /// <summary>
    /// The kit row an existing drum-map row stands for: the same name; else dawsu!'s own starter names (T1–T4, Right
    /// Crash, Ride Bell, Ghost Notes…); else the same words in any order, ignoring "hi-hat" (Tight Tip ↔ Tip Tight,
    /// Hi-Hat Open 1 ↔ Open 1). Null when the kit has nothing for it.
    /// </summary>
    public static DrumKitRow? Match(string rowName, IReadOnlyList<DrumKitRow> kit)
    {
        DrumKitRow? Named(string name) => kit.FirstOrDefault(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (Named(rowName.Trim()) is { } exact) return exact;
        foreach (var alias in Aliases.GetValueOrDefault(rowName.Trim().ToLowerInvariant(), []))
            if (Named(alias) is { } aliased) return aliased;
        var words = Words(rowName);
        return words.Count == 0 ? null : kit.FirstOrDefault(k => Words(k.Name).SetEquals(words));
    }

    private static HashSet<string> Words(string name) =>
        [.. name.ToLowerInvariant().Replace("hi-hat", " ").Replace("hihat", " ").Split([' ', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("hat" or "hats"))];

    // dawsu!'s starter drum-map names (DrumMapDefaults) and common DAW names → kit names, best first.
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["kick"] = ["Kick", "Bass Drum"],
        ["ghost notes"] = ["Snare"],
        ["t1"] = ["Rack Tom 1", "Tom 1", "High Tom"], ["t2"] = ["Rack Tom 2", "Tom 2", "Mid Tom"],
        ["t3"] = ["Floor Tom 1", "Tom 3", "Low Tom"], ["t4"] = ["Floor Tom 2", "Tom 4", "Floor Tom"],
        ["ride bell"] = ["Bell", "Ride Bell"],
        ["bow ride (low)"] = ["Ride", "Ride Bow"], ["bow ride (high)"] = ["Ride", "Ride Bow"],
        ["right crash"] = ["Main Crash R", "Crash R", "Crash 2"], ["left crash"] = ["Main Crash L", "Crash L", "Crash 1"],
        ["splash"] = ["Splash L", "Splash C", "Splash"],
        ["mini stack left"] = ["Stack"], ["mini stack right"] = ["Stack"],
    };

    private static string Title(string group) =>
        string.Join(' ', group.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length <= 1 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    private static DrumKitArticulation A(string group, string item, int? note) => new(group, item, note);

    /// <summary>GGD Matt Garstka Signature Drums, mapping preset "GGD" (as the plug-in's Mappings page shows it).</summary>
    private static readonly ImmutableArray<DrumKitArticulation> GgdMattGarstka =
    [
        A("KICK", "Hit", 36),
        A("SNARE", "Hit", 37), A("SNARE", "Flam", 38), A("SNARE", "Wires Off", 41), A("SNARE", "Ruff", 39), A("SNARE", "Sidestick", 40),
        A("TOMS", "Rack Tom 1: Hit", 42), A("TOMS", "Rack Tom 2: Hit", 45), A("TOMS", "Floor Tom 1: Hit", 43), A("TOMS", "Floor Tom 2: Hit", 46),
        A("AUX", "Stick Click", null),
        A("HATS", "Tip Tight", 48), A("HATS", "Edge Tight", 54), A("HATS", "Tip Closed", 49), A("HATS", "Edge Closed", null),
        A("HATS", "Open 1", 52), A("HATS", "Open 2", 53), A("HATS", "Open 3", 57), A("HATS", "Pedal", 55),
        A("HATS", "Pedal Ching", null), A("HATS", "CC", null),
        A("RIDE", "Bell", 74), A("RIDE", "Ride", 73),
        A("CHINA", "Hit", 71), A("CHINA", "Choke", 72),
        A("CRASHES", "Main Crash L: Hit", 58), A("CRASHES", "Main Crash L: Choke", 59),
        A("CRASHES", "Wide Crash L: Hit", null), A("CRASHES", "Wide Crash L: Choke", null),
        A("CRASHES", "Main Crash R: Hit", 61), A("CRASHES", "Main Crash R: Choke", 62),
        A("SPLASHES", "Splash L: Hit", 77), A("SPLASHES", "Splash L: Choke", 83),
        A("SPLASHES", "Splash C: Hit", null), A("SPLASHES", "Splash C: Choke", null),
        A("MINI HATS", "Hit", null),
        A("STACK", "Hit", null),
    ];
}

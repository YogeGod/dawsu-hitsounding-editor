using Avalonia.Input;

namespace OsuHitsound.App;

/// <summary>A keyboard command: its id (what the window runs), where it shows in help, and its keys.</summary>
internal sealed record EditorCommand(string Id, string Group, string Description, IReadOnlyList<KeyGesture> Keys)
{
    /// <summary>The Keysound area the command also belongs to when the key editor runs it too (F1 lists it in both), else null.</summary>
    public string? KeysoundGroup { get; init; }

    public string KeysText => Text(Keys);

    public static string Text(IEnumerable<KeyGesture> keys) => string.Join(", ", keys.Select(Name));

    /// <summary>A key as printed on the keyboard: "1" not "D1", "," not "OemComma".</summary>
    public static string Name(KeyGesture gesture)
    {
        string key = gesture.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(gesture.Key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"Numpad {(int)(gesture.Key - Key.NumPad0)}",
            Key.Divide => "Numpad /",
            Key.Multiply => "Numpad *",
            Key.Subtract => "Numpad -",
            Key.Add => "Numpad +",
            Key.Decimal => "Numpad .",
            Key.OemPeriod => ".",
            Key.OemMinus => "-",
            Key.Oem2 => "/",
            Key.OemComma => ",",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.Back => "Backspace",
            Key.Escape => "Esc",
            Key.Delete => "Del",
            var k => k.ToString(),
        };
        return string.Concat(
            gesture.KeyModifiers.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "",
            gesture.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : "",
            gesture.KeyModifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : "",
            key);
    }
}

/// <summary>
/// Every keyboard command and mouse action, defined once: key handling matches against these, menus take their
/// shortcut text from them, and the F1 help lists them.
/// </summary>
internal static class EditorCommands
{
    private static KeyGesture K(Key key, KeyModifiers mods = KeyModifiers.None) => new(key, mods);

    private const KeyModifiers Ctrl = KeyModifiers.Control, Shift = KeyModifiers.Shift, Alt = KeyModifiers.Alt;

    /// <summary>The built-in keys, before any preset or user change. Shared commands get their Keysound area from the key editor's own list.</summary>
    public static readonly IReadOnlyList<EditorCommand> Defaults =
        [.. Commands().Select(c => KeysoundShortcuts.AreaOf(c.Id) is { } ks ? c with { KeysoundGroup = ks } : c)];

    private static IReadOnlyList<EditorCommand> Commands() =>
    [
        new("open", Areas.HsMapFiles, "Open a .osu", [K(Key.O, Ctrl)]),
        new("openFromOsu", Areas.HsOsuConnection, "Open the map osu! has open (it reloads when osu! saves it)", []),
        new("newDiff", Areas.HsMapFiles, "New hitsound diff: an empty difficulty with this map's red lines", [K(Key.N, Ctrl)]),
        new("importOsz", Areas.HsMapFiles, "Import an .osz into the Songs folder", []),
        new("openFolder", Areas.HsMapFiles, "Open the map's folder in Explorer", [K(Key.O, Ctrl | Shift)]),
        new("save", Areas.HsMapOutput, "Save the .osu (a backup is kept outside the mapset)", [K(Key.S, Ctrl)]),
        new("revert", Areas.HsMapFiles, "Revert to the last saved .osu and discard unsaved edits", [K(Key.L, Ctrl)]),
        new("openHitsoundFolder", Areas.HsMapFiles, "Open the hitsound library folder", []),
        new("reviewOutput", Areas.HsMapOutput, "Review output: pairing, finish fusion and export settings", []),
        new("preferences", Areas.Preferences, "Preferences", [K(Key.OemComma, Ctrl)]),
        new("help", Areas.Help, "This shortcut overview", [K(Key.F1)]),
        new("inspector", Areas.Help, "UI inspector", [K(Key.I, Ctrl | Shift)]),

        new("play", Areas.HsPlayback, "Play / stop", [K(Key.Space)]),
        new("start", Areas.HsPlayback, "Go to the start", [K(Key.Home)]),
        new("seekBack", Areas.HsTimeline, "Playhead to the previous grid line (osu! keys: Left, wheel up)", []),
        new("seekForward", Areas.HsTimeline, "Playhead to the next grid line (osu! keys: Right, wheel down)", []),
        new("seekBackFar", Areas.HsTimeline, "Playhead back four grid lines (osu! keys: Shift+Left, Shift+wheel up)", []),
        new("seekForwardFar", Areas.HsTimeline, "Playhead forward four grid lines (osu! keys: Shift+Right, Shift+wheel down)", []),
        new("first", Areas.HsTimeline, "To the first object or placed hit (twice: to the start of the song)", [K(Key.Z)]),
        new("last", Areas.HsTimeline, "To the last object or placed hit (twice: to the end of the song)", [K(Key.V)]),
        new("stop", Areas.HsPlayback, "Stop (again: to the start)", [K(Key.NumPad0)]),
        new("cycle", Areas.HsTimeline, "Cycle between the L and R locators", [K(Key.L), K(Key.Divide), K(Key.Oem2)]),
        new("locatorsToSelection", Areas.HsTimeline, "Locators to the selection (or the visible range)", [K(Key.P)]),
        new("playLocatorRange", Areas.HsTimeline, "Locators to the selection, cycle on, play from L", [K(Key.P, Alt)]),
        new("jumpToSelection", Areas.HsTimeline, "Playhead to the selected note (the first one of a selection)", [K(Key.Space, Alt)]),
        new("jumpLeft", Areas.HsTimeline, "Jump to the left locator", [K(Key.NumPad1)]),
        new("jumpRight", Areas.HsTimeline, "Jump to the right locator", [K(Key.NumPad2)]),
        new("setLeft", Areas.HsTimeline, "Set the left locator at the playhead", [K(Key.NumPad1, Ctrl)]),
        new("setRight", Areas.HsTimeline, "Set the right locator at the playhead", [K(Key.NumPad2, Ctrl)]),

        new("toolSelect", Areas.HsHitEditing, "Select and move", [K(Key.D1)]),
        new("toolGrab", Areas.HsHitEditing, "Grab: pan time and lanes", [K(Key.D2)]),
        new("toolRange", Areas.HsHitEditing, "Range: drag a time range across lanes, then Edit range", []),
        new("toolDraw", Areas.HsHitEditing, "Draw (hold Alt to draw with any tool)", [K(Key.D3)]),
        new("toolErase", Areas.HsHitEditing, "Erase", [K(Key.D4)]),
        new("toolMute", Areas.HsHitEditing, "Mute hits while listening (never saved)", [K(Key.D5)]),
        new("toolRamp", Areas.HsVolume, "Ramp volumes in the volume lane", [K(Key.D6)]),
        new("snap", Areas.HsTimeline, "Snapping on / off", [K(Key.J)]),
        .. Enumerable.Range(1, 9).Select(n => new EditorCommand($"snap{n}", Areas.HsTimeline, $"Snap to 1/{n}", [K(Key.D0 + n, Shift)])),

        new("undo", Areas.HsHitEditing, "Undo", [K(Key.Z, Ctrl)]),
        new("redo", Areas.HsHitEditing, "Redo", [K(Key.Y, Ctrl), K(Key.Z, Ctrl | Shift)]),
        new("copy", Areas.HsHitEditing, "Copy", [K(Key.C, Ctrl)]),
        new("cut", Areas.HsHitEditing, "Cut", [K(Key.X, Ctrl)]),
        new("paste", Areas.HsHitEditing, "Paste at the playhead", [K(Key.V, Ctrl)]),
        new("duplicate", Areas.HsHitEditing, "Duplicate the selection after itself", [K(Key.D, Ctrl)]),
        new("selectAll", Areas.HsHitEditing, "Select all hits", [K(Key.A, Ctrl)]),
        new("deselect", Areas.HsHitEditing, "Select nothing", [K(Key.A, Ctrl | Shift), K(Key.Escape)]),
        new("delete", Areas.HsHitEditing, "Delete the selection", [K(Key.Delete), K(Key.Back)]),
        // The arrow keys as in Cubase's Drum Editor: Left/Right select the previous/next hit in any lane, Up/Down move the
        // selection to the lane above/below (the key editor transposes), Shift adds to the selection, Ctrl+Left/Right
        // nudges, Ctrl+Up/Down zooms rows.
        new("selectPrevious", Areas.HsHitEditing, "Select the previous hit in any lane (key editor: the previous note)", [K(Key.Left)]),
        new("selectNext", Areas.HsHitEditing, "Select the next hit in any lane (key editor: the next note)", [K(Key.Right)]),
        new("addPrevious", Areas.HsHitEditing, "Add the previous hit (note) to the selection", [K(Key.Left, Shift)]),
        new("addNext", Areas.HsHitEditing, "Add the next hit (note) to the selection", [K(Key.Right, Shift)]),
        new("navUp", Areas.HsHitEditing, "Move the selection to the lane above (key editor: transpose up a semitone)", [K(Key.Up)]),
        new("navDown", Areas.HsHitEditing, "Move the selection to the lane below (key editor: transpose down a semitone)", [K(Key.Down)]),
        new("navAddUp", Areas.HsHitEditing, "Add the hit in the lane above (key editor: transpose up an octave)", [K(Key.Up, Shift)]),
        new("navAddDown", Areas.HsHitEditing, "Add the hit in the lane below (key editor: transpose down an octave)", [K(Key.Down, Shift)]),
        new("nudgeLeft", Areas.HsHitEditing, "Nudge left by one snap step", [K(Key.Left, Ctrl)]),
        new("nudgeRight", Areas.HsHitEditing, "Nudge right by one snap step", [K(Key.Right, Ctrl)]),
        new("rowsOut", Areas.HsLanes, "Smaller rows (zoom tracks out)", [K(Key.Up, Ctrl)]),
        new("rowsIn", Areas.HsLanes, "Larger rows (zoom tracks in)", [K(Key.Down, Ctrl)]),
        new("volumeUp", Areas.HsVolume, "Selected volumes (key editor: velocities) up by 5", [K(Key.Up, Alt)]),
        new("volumeDown", Areas.HsVolume, "Selected volumes (key editor: velocities) down by 5", [K(Key.Down, Alt)]),
        new("volumeUpFine", Areas.HsVolume, "Selected volumes (key editor: velocities) up by 1", [K(Key.Up, Alt | Shift)]),
        new("volumeDownFine", Areas.HsVolume, "Selected volumes (key editor: velocities) down by 1", [K(Key.Down, Alt | Shift)]),
        new("ks.startLeft", Areas.KsNoteEditing, "Note starts earlier by one snap step (Cubase's Nudge Start Left)", [K(Key.Left, Alt)]),
        new("ks.endRight", Areas.KsNoteEditing, "Note ends later by one snap step (Cubase's Nudge End Right)", [K(Key.Right, Alt)]),
        new("ks.startRight", Areas.KsNoteEditing, "Note starts later by one snap step (Cubase's Nudge Start Right)", [K(Key.Right, Alt | Shift)]),
        new("ks.endLeft", Areas.KsNoteEditing, "Note ends earlier by one snap step (Cubase's Nudge End Left)", [K(Key.Left, Alt | Shift)]),
        new("gridLeft", Areas.HsHitEditing, "Move to the previous grid line", [K(Key.Left, Ctrl | Alt)]),
        new("gridRight", Areas.HsHitEditing, "Move to the next grid line", [K(Key.Right, Ctrl | Alt)]),
        new("laneUp", Areas.HsHitEditing, "Move the selection to the lane above (as Up; key editor: a semitone up)", [K(Key.Up, Ctrl | Alt)]),
        new("laneDown", Areas.HsHitEditing, "Move the selection to the lane below (as Down; key editor: a semitone down)", [K(Key.Down, Ctrl | Alt)]),
        new("quantize", Areas.HsHitEditing, "Quantize: move the selected loose hits (key editor: notes) toward the grid (last strength)", [K(Key.Q)]),
        new("quantizeDialog", Areas.HsHitEditing, "Quantize with a chosen strength", []),
        new("repeat", Areas.HsHitEditing, "Repeat: copies of the selection, a number of snap steps apart", [K(Key.R, Ctrl)]),
        new("editRange", Areas.HsHitEditing, "Edit range: copy, move or delete the hits in the range", []),
        new("scaleByVolume", Areas.HsHitEditing, "Scale by volume: make circles in a volume range louder or quieter", []),

        new("zoomIn", Areas.HsTimeline, "Zoom in", [K(Key.H)]),
        new("zoomOut", Areas.HsTimeline, "Zoom out", [K(Key.G)]),
        new("filter", Areas.HsLanes, "Filter lanes by name", [K(Key.F, Ctrl)]),

        // Keysound mode's key editor: the commands above work there too (same keys, same meaning); these are its own.
        new("ks.toolSplit", Areas.KsNoteEditing, "Split tool: click a note to split it", [K(Key.D7)]),
        new("ks.toolGlue", Areas.KsNoteEditing, "Glue tool: click a note to join it with the next of its pitch", [K(Key.D8)]),
        new("ks.record", Areas.KsNoteInput, "Record from the MIDI keyboard or musical typing into the selected track", [K(Key.Multiply)]),
        new("ks.typing", Areas.KsNoteInput, "Musical typing: play the selected track from the computer keyboard (other shortcuts are off meanwhile)", [K(Key.K, Alt)]),
        new("ks.sync", Areas.KsSync, "Sync notes to gameplay objects", []),
        new("ks.generate", Areas.KsOutput, "Generate keysounds", []),

        new("addBookmark", Areas.HsTimeline, "Add a bookmark at the playhead", [K(Key.B, Ctrl)]),
        new("removeBookmark", Areas.HsTimeline, "Remove the bookmark at the playhead", [K(Key.B, Ctrl | Shift)]),
        new("previousBookmark", Areas.HsTimeline, "Previous bookmark", [K(Key.OemOpenBrackets)]),
        new("nextBookmark", Areas.HsTimeline, "Next bookmark", [K(Key.OemCloseBrackets)]),
    ];

    /// <summary>Mouse actions, for the help only.</summary>
    public static readonly IReadOnlyList<(string Group, string Keys, string Description)> Mouse =
    [
        (Areas.Mouse(Areas.HsHitEditing), "Click / drag (Select)", "Select hits, drag a box, or drag hits to move them"),
        (Areas.Mouse(Areas.HsHitEditing), "Ctrl+click / Shift+click", "Toggle / add a hit to the selection"),
        (Areas.Mouse(Areas.HsHitEditing), "Drag (Range)", "Mark a time range over the lanes dragged across; a click clears it"),
        (Areas.Mouse(Areas.HsHitEditing), "Click / drag (Draw)", "Paint hits; a stroke starting on a hit erases"),
        (Areas.Mouse(Areas.HsHitEditing), "Alt+drag", "Draw with any tool"),
        (Areas.Mouse(Areas.HsTimeline), "Ctrl+wheel or Ctrl+Alt+wheel", "Zoom in time around the pointer"),
        (Areas.Mouse(Areas.HsLanes), "Ctrl+Shift+wheel", "Zoom rows (vertical zoom)"), // with rowsIn / rowsOut
        (Areas.Mouse(Areas.HsTimeline), "Shift+wheel", "Scroll in time"),
        (Areas.Mouse(Areas.HsTimeline), "Alt+wheel", "Snap finer / coarser"),
        (Areas.Mouse(Areas.HsTimeline), "Click the ruler or waveform", "Move the playhead (onto a bookmark when near one)"),
        (Areas.Mouse(Areas.HsTimeline), "Ctrl+click / Alt+click the ruler", "Set the left / right locator"),
        (Areas.Mouse(Areas.HsTimeline), "Drag the upper ruler", "Draw a cycle range; click inside it to toggle cycling"),
        (Areas.Mouse(Areas.HsTimeline), "Drag inside / an end of the cycle range", "Move it / resize it (both editors; in the key editor the upper half of the ruler)"),
        (Areas.Mouse(Areas.HsTimeline), "Drag the line under the waveform", "Resize it; double-click resets"),
        (Areas.Mouse(Areas.HsTimeline), "Click / double-click an object (over the waveform)", "Select its hits / also play them"),
        (Areas.Mouse(Areas.HsHitEditing), "Double-click a hit (Select tool)", "Move the playhead to it and hear it"),
        (Areas.Mouse(Areas.HsHitEditing), "Double-click empty space (Select tool)", "Insert a hit in that lane, as Draw would"),
        (Areas.Mouse(Areas.HsHitEditing), "Right-click in the grid", "Pick a tool"),
        (Areas.Mouse(Areas.HsLanes), "Click a lane name", "Hear its sample"),
        (Areas.Mouse(Areas.HsLanes), "Click / Ctrl+click / Shift+click a lane name", "Select one lane / toggle a lane / select a range; M/S applies to selected lanes"),
        (Areas.Mouse(Areas.HsLanes), "Right-click a lane name", "Rename, move, delete or add lanes"),
        (Areas.Mouse(Areas.HsVolume), "Drag a bar", "Set its volume; with several selected, all move by the same amount and keep their differences"),
        (Areas.Mouse(Areas.HsVolume), "Ctrl+drag a selected bar", "Spread the selected volumes apart (up) or squeeze them together (down), as in Cubase"),
        (Areas.Mouse(Areas.HsVolume), "Alt+drag", "Paint volumes"),
        (Areas.Mouse(Areas.HsVolume), "Shift+drag, or the Ramp tool", "Draw a ramp"),
        // The key editor: the Hitsound editor's mouse actions on notes, and its own (resize, velocity, pitch).
        (Areas.Mouse(Areas.KsNoteEditing), "Click / drag (Select)", "Select notes, drag a box, or drag notes to move them; drag a note's edge to resize it"),
        (Areas.Mouse(Areas.KsNoteEditing), "Ctrl+click / Shift+click", "Toggle / add a note to the selection"),
        (Areas.Mouse(Areas.KsNoteEditing), "Ctrl+Alt+drag", "Copy the dragged notes"),
        (Areas.Mouse(Areas.KsNoteEditing), "Ctrl+Shift+drag a note", "Change the selected notes' velocity"),
        (Areas.Mouse(Areas.KsNoteEditing), "Click / drag (Draw)", "Add a note; drag right to set its length. Clicking a note deletes it"),
        (Areas.Mouse(Areas.KsNoteEditing), "Alt+click / Alt+drag", "Draw with any tool"),
        (Areas.Mouse(Areas.KsNoteEditing), "Click a note (Mute)", "Silence it while listening (never saved; Generate still renders it)"),
        (Areas.Mouse(Areas.KsNoteEditing), "Double-click a note (Select tool)", "Move the playhead to it and hear it"),
        (Areas.Mouse(Areas.KsNoteEditing), "Double-click empty space (Select tool)", "Insert a note there, as Draw would"),
        (Areas.Mouse(Areas.KsNoteEditing), "Shift+double-click a note", "Select it and the later notes of its pitch"),
        (Areas.Mouse(Areas.KsNoteEditing), "Right-click in the roll", "Pick a tool"),
        (Areas.Mouse(Areas.KsNoteEditing), "Click / Ctrl+click a key", "Hear it / select every note of that pitch"),
        (Areas.Mouse(Areas.KsNoteEditing), "Drag a velocity bar", "Set its velocity; with several selected, all move by the same amount"),
        (Areas.Mouse(Areas.KsNoteEditing), "Ctrl+drag a velocity bar", "Spread the selected velocities apart or squeeze them together"),
        (Areas.Mouse(Areas.KsNoteEditing), "Alt+drag in the velocity lane", "Paint velocities"),
        (Areas.Mouse(Areas.KsNoteEditing), "Shift+drag in the velocity lane, or the Ramp tool", "Draw a velocity ramp"),
    ];

    /// <summary>The commands with the keys in force (see <see cref="Keymap"/>); the built-in keys until <see cref="Use"/>.</summary>
    public static IReadOnlyList<EditorCommand> All { get; private set; } = Defaults;

    public static void Use(IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> keys) =>
        All = [.. Defaults.Select(c => c with { Keys = keys.GetValueOrDefault(c.Id, []) })];

    public static EditorCommand Get(string id) => All.First(c => c.Id == id);

    /// <summary>The command a key press runs: an exact match of key and modifiers.</summary>
    public static EditorCommand? Match(KeyEventArgs e) => All.FirstOrDefault(c => c.Keys.Any(k => k.Matches(e)));
}

/// <summary>
/// The feature areas commands are grouped by in F1 and Preferences → Keys: Hitsound and Keysound areas carry their
/// mode, shared ones none (see CONTEXT.md, "Feature area"). <see cref="Order"/> is the order F1 shows them in.
/// </summary>
internal static class Areas
{
    public const string HsMapFiles = "Hitsound – Map files";
    public const string HsOsuConnection = "Hitsound – osu! connection";
    public const string HsLanes = "Hitsound – Lanes";
    public const string HsHitEditing = "Hitsound – Hit editing";
    public const string HsTimeline = "Hitsound – Timeline navigation";
    public const string HsVolume = "Hitsound – Volume control";
    public const string HsMapOutput = "Hitsound – Map output";
    public const string HsPlayback = "Hitsound – Playback";
    public const string KsNoteEditing = "Keysound – Note editing";
    public const string KsNoteInput = "Keysound – Note input";
    public const string KsSync = "Keysound – Gameplay sync";
    public const string KsOutput = "Keysound – Output";
    public const string KsPlayback = "Keysound – Playback";
    public const string Preferences = "Preferences";
    public const string Help = "Help and updates";

    private const string MouseSuffix = " (mouse)";

    /// <summary>The group of an area's mouse actions, shown right after the area's keys.</summary>
    public static string Mouse(string area) => area + MouseSuffix;

    private static readonly string[] Ordered =
    [
        HsMapFiles, HsOsuConnection, HsLanes, HsHitEditing, HsTimeline, HsVolume, HsMapOutput, HsPlayback,
        KsNoteEditing, KsNoteInput, KsSync, KsOutput, KsPlayback, Preferences, Help,
    ];

    /// <summary>Where a group sorts: by its area, an area's mouse group right after it, unknown groups last.</summary>
    public static int Order(string group)
    {
        bool mouse = group.EndsWith(MouseSuffix, StringComparison.Ordinal);
        int index = Array.IndexOf(Ordered, mouse ? group[..^MouseSuffix.Length] : group);
        return (index < 0 ? Ordered.Length : index) * 2 + (mouse ? 1 : 0);
    }
}

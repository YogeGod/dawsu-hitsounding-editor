using Avalonia.Input;

namespace OsuHitsound.App;

/// <summary>A keyboard command: its id (what the window runs), where it shows in help, and its keys.</summary>
internal sealed record EditorCommand(string Id, string Group, string Description, IReadOnlyList<KeyGesture> Keys)
{
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

    /// <summary>The built-in keys, before any preset or user change.</summary>
    public static readonly IReadOnlyList<EditorCommand> Defaults =
    [
        new("open", "Files and help", "Open a .osu", [K(Key.O, Ctrl)]),
        new("newDiff", "Files and help", "New hitsound diff: an empty difficulty with this map's red lines", [K(Key.N, Ctrl)]),
        new("importOsz", "Files and help", "Import an .osz into the Songs folder", []),
        new("openFolder", "Files and help", "Open the map's folder in Explorer", [K(Key.O, Ctrl | Shift)]),
        new("save", "Files and help", "Save the .osu (a backup is kept outside the mapset)", [K(Key.S, Ctrl)]),
        new("revert", "Files and help", "Revert to the last saved .osu and discard unsaved edits", [K(Key.L, Ctrl)]),
        new("openHitsoundFolder", "Files and help", "Open the hitsound library folder", []),
        new("reviewOutput", "Files and help", "Review output: pairing, finish fusion and export settings", []),
        new("preferences", "Files and help", "Preferences", [K(Key.OemComma, Ctrl)]),
        new("help", "Files and help", "This shortcut overview", [K(Key.F1)]),
        new("inspector", "Files and help", "UI inspector", [K(Key.I, Ctrl | Shift)]),

        new("play", "Playback and cycle", "Play / stop", [K(Key.Space)]),
        new("start", "Playback and cycle", "Go to the start", [K(Key.Home)]),
        new("seekBack", "Playback and cycle", "Playhead to the previous grid line (osu! keys: Left, wheel up)", []),
        new("seekForward", "Playback and cycle", "Playhead to the next grid line (osu! keys: Right, wheel down)", []),
        new("seekBackFar", "Playback and cycle", "Playhead back four grid lines (osu! keys: Shift+Left, Shift+wheel up)", []),
        new("seekForwardFar", "Playback and cycle", "Playhead forward four grid lines (osu! keys: Shift+Right, Shift+wheel down)", []),
        new("first", "Playback and cycle", "To the first object or placed hit (twice: to the start of the song)", [K(Key.Z)]),
        new("last", "Playback and cycle", "To the last object or placed hit (twice: to the end of the song)", [K(Key.V)]),
        new("stop", "Playback and cycle", "Stop (again: to the start)", [K(Key.NumPad0)]),
        new("cycle", "Playback and cycle", "Cycle between the L and R locators", [K(Key.L), K(Key.Divide), K(Key.Oem2)]),
        new("locatorsToSelection", "Playback and cycle", "Locators to the selection (or the visible range)", [K(Key.P)]),
        new("playLocatorRange", "Playback and cycle", "Locators to the selection, cycle on, play from L", [K(Key.P, Alt)]),
        new("jumpLeft", "Playback and cycle", "Jump to the left locator", [K(Key.NumPad1)]),
        new("jumpRight", "Playback and cycle", "Jump to the right locator", [K(Key.NumPad2)]),
        new("setLeft", "Playback and cycle", "Set the left locator at the playhead", [K(Key.NumPad1, Ctrl)]),
        new("setRight", "Playback and cycle", "Set the right locator at the playhead", [K(Key.NumPad2, Ctrl)]),

        new("toolSelect", "Tools and snap", "Select and move", [K(Key.D1)]),
        new("toolGrab", "Tools and snap", "Grab: pan time and lanes", [K(Key.D2)]),
        new("toolRange", "Tools and snap", "Range: drag a time range across lanes, then Edit range", []),
        new("toolDraw", "Tools and snap", "Draw (hold Alt to draw with any tool)", [K(Key.D3)]),
        new("toolErase", "Tools and snap", "Erase", [K(Key.D4)]),
        new("toolMute", "Tools and snap", "Mute hits while listening (never saved)", [K(Key.D5)]),
        new("toolRamp", "Tools and snap", "Ramp volumes in the volume lane", [K(Key.D6)]),
        new("snap", "Tools and snap", "Snapping on / off", [K(Key.J)]),
        .. Enumerable.Range(1, 9).Select(n => new EditorCommand($"snap{n}", "Tools and snap", $"Snap to 1/{n}", [K(Key.D0 + n, Shift)])),

        new("undo", "Editing", "Undo", [K(Key.Z, Ctrl)]),
        new("redo", "Editing", "Redo", [K(Key.Y, Ctrl), K(Key.Z, Ctrl | Shift)]),
        new("copy", "Editing", "Copy", [K(Key.C, Ctrl)]),
        new("cut", "Editing", "Cut", [K(Key.X, Ctrl)]),
        new("paste", "Editing", "Paste at the playhead", [K(Key.V, Ctrl)]),
        new("duplicate", "Editing", "Duplicate the selection after itself", [K(Key.D, Ctrl)]),
        new("selectAll", "Editing", "Select all hits", [K(Key.A, Ctrl)]),
        new("deselect", "Editing", "Select nothing", [K(Key.A, Ctrl | Shift), K(Key.Escape)]),
        new("delete", "Editing", "Delete the selection", [K(Key.Delete), K(Key.Back)]),
        // The arrow keys as in Cubase: Left/Right select the previous/next event, Up/Down the one above/below (the key
        // editor transposes instead), Shift adds to the selection, Ctrl+Left/Right nudges, Ctrl+Up/Down zooms rows.
        new("selectPrevious", "Editing", "Select the previous hit in its lane (key editor: the previous note)", [K(Key.Left)]),
        new("selectNext", "Editing", "Select the next hit in its lane (key editor: the next note)", [K(Key.Right)]),
        new("addPrevious", "Editing", "Add the previous hit (note) to the selection", [K(Key.Left, Shift)]),
        new("addNext", "Editing", "Add the next hit (note) to the selection", [K(Key.Right, Shift)]),
        new("navUp", "Editing", "Select the hit in the lane above (key editor: transpose up a semitone)", [K(Key.Up)]),
        new("navDown", "Editing", "Select the hit in the lane below (key editor: transpose down a semitone)", [K(Key.Down)]),
        new("navAddUp", "Editing", "Add the hit in the lane above (key editor: transpose up an octave)", [K(Key.Up, Shift)]),
        new("navAddDown", "Editing", "Add the hit in the lane below (key editor: transpose down an octave)", [K(Key.Down, Shift)]),
        new("nudgeLeft", "Editing", "Nudge left by one snap step", [K(Key.Left, Ctrl)]),
        new("nudgeRight", "Editing", "Nudge right by one snap step", [K(Key.Right, Ctrl)]),
        new("rowsOut", "Editing", "Smaller rows (zoom tracks out)", [K(Key.Up, Ctrl)]),
        new("rowsIn", "Editing", "Larger rows (zoom tracks in)", [K(Key.Down, Ctrl)]),
        new("volumeUp", "Editing", "Selected volumes (key editor: velocities) up by 5", [K(Key.Up, Alt)]),
        new("volumeDown", "Editing", "Selected volumes (key editor: velocities) down by 5", [K(Key.Down, Alt)]),
        new("volumeUpFine", "Editing", "Selected volumes (key editor: velocities) up by 1", [K(Key.Up, Alt | Shift)]),
        new("volumeDownFine", "Editing", "Selected volumes (key editor: velocities) down by 1", [K(Key.Down, Alt | Shift)]),
        new("ks.startLeft", "Keysound key editor", "Note starts earlier by one snap step (Cubase's Nudge Start Left)", [K(Key.Left, Alt)]),
        new("ks.endRight", "Keysound key editor", "Note ends later by one snap step (Cubase's Nudge End Right)", [K(Key.Right, Alt)]),
        new("ks.startRight", "Keysound key editor", "Note starts later by one snap step (Cubase's Nudge Start Right)", [K(Key.Right, Alt | Shift)]),
        new("ks.endLeft", "Keysound key editor", "Note ends earlier by one snap step (Cubase's Nudge End Left)", [K(Key.Left, Alt | Shift)]),
        new("gridLeft", "Editing", "Move to the previous grid line", [K(Key.Left, Ctrl | Alt)]),
        new("gridRight", "Editing", "Move to the next grid line", [K(Key.Right, Ctrl | Alt)]),
        new("laneUp", "Editing", "Move the selection to the lane above", [K(Key.Up, Ctrl | Alt)]),
        new("laneDown", "Editing", "Move the selection to the lane below", [K(Key.Down, Ctrl | Alt)]),
        new("quantize", "Editing", "Quantize: move the selected loose hits toward the grid (last strength)", [K(Key.Q)]),
        new("quantizeDialog", "Editing", "Quantize with a chosen strength", []),
        new("repeat", "Editing", "Repeat: copies of the selection, a number of snap steps apart", [K(Key.R, Ctrl)]),
        new("editRange", "Editing", "Edit range: copy, move or delete the hits in the range", []),
        new("scaleByVolume", "Editing", "Scale by volume: make circles in a volume range louder or quieter", []),

        new("zoomIn", "View and lanes", "Zoom in", [K(Key.H)]),
        new("zoomOut", "View and lanes", "Zoom out", [K(Key.G)]),
        new("filter", "View and lanes", "Filter lanes by name", [K(Key.F, Ctrl)]),

        // Keysound mode's key editor: the commands above work there too (same keys, same meaning); these are its own.
        new("ks.toolSplit", "Keysound key editor", "Split tool: click a note to split it", [K(Key.D7)]),
        new("ks.toolGlue", "Keysound key editor", "Glue tool: click a note to join it with the next of its pitch", [K(Key.D8)]),
        new("ks.record", "Keysound key editor", "Record from the MIDI keyboard or musical typing into the selected track", [K(Key.Multiply)]),
        new("ks.typing", "Keysound key editor", "Musical typing: play the selected track from the computer keyboard (other shortcuts are off meanwhile)", [K(Key.K, Alt)]),
        new("ks.sync", "Keysound key editor", "Sync notes to gameplay objects", []),
        new("ks.generate", "Keysound key editor", "Generate keysounds", []),

        new("addBookmark", "Bookmarks", "Add a bookmark at the playhead", [K(Key.B, Ctrl)]),
        new("removeBookmark", "Bookmarks", "Remove the bookmark at the playhead", [K(Key.B, Ctrl | Shift)]),
        new("previousBookmark", "Bookmarks", "Previous bookmark", [K(Key.OemOpenBrackets)]),
        new("nextBookmark", "Bookmarks", "Next bookmark", [K(Key.OemCloseBrackets)]),
    ];

    /// <summary>Mouse actions, for the help only.</summary>
    public static readonly IReadOnlyList<(string Group, string Keys, string Description)> Mouse =
    [
        ("Mouse: grid", "Click / drag (Select)", "Select hits, drag a box, or drag hits to move them"),
        ("Mouse: grid", "Ctrl+click / Shift+click", "Toggle / add a hit to the selection"),
        ("Mouse: grid", "Drag (Range)", "Mark a time range over the lanes dragged across; a click clears it"),
        ("Mouse: grid", "Click / drag (Draw)", "Paint hits; a stroke starting on a hit erases"),
        ("Mouse: grid", "Right-click a hit", "Delete it"),
        ("Mouse: grid", "Alt+drag", "Draw with any tool"),
        ("Mouse: zoom and scroll", "Ctrl+wheel or Ctrl+Alt+wheel", "Zoom in time around the pointer"),
        ("Mouse: zoom and scroll", "Ctrl+Shift+wheel", "Zoom rows (vertical zoom)"),
        ("Mouse: zoom and scroll", "Shift+wheel", "Scroll in time"),
        ("Mouse: zoom and scroll", "Alt+wheel", "Snap finer / coarser"),
        ("Mouse: ruler and strips", "Click the ruler or waveform", "Move the playhead (onto a bookmark when near one)"),
        ("Mouse: ruler and strips", "Ctrl+click / Alt+click the ruler", "Set the left / right locator"),
        ("Mouse: ruler and strips", "Drag the upper ruler", "Draw a cycle range; click inside it to toggle cycling"),
        ("Mouse: ruler and strips", "Drag inside / an end of the cycle range", "Move it / resize it (both editors; in the key editor the upper half of the ruler)"),
        ("Mouse: ruler and strips", "Drag the line under the waveform", "Resize it; double-click resets"),
        ("Mouse: ruler and strips", "Click / double-click an object (over the waveform)", "Select its hits / also play them"),
        ("Mouse: grid", "Double-click a hit (Select tool)", "Move the playhead to it and hear it"),
        ("Mouse: lanes", "Click a lane name", "Hear its sample"),
        ("Mouse: lanes", "Click / Ctrl+click / Shift+click a lane name", "Select one lane / toggle a lane / select a range; M/S applies to selected lanes"),
        ("Mouse: lanes", "Right-click a lane name", "Rename, move, delete or add lanes"),
        ("Mouse: volume lane", "Drag a bar", "Set its volume; with several selected, all move by the same amount and keep their differences"),
        ("Mouse: volume lane", "Ctrl+drag a selected bar", "Spread the selected volumes apart (up) or squeeze them together (down), as in Cubase"),
        ("Mouse: volume lane", "Alt+drag", "Paint volumes"),
        ("Mouse: volume lane", "Shift+drag, or the Ramp tool", "Draw a ramp"),
    ];

    /// <summary>The commands with the keys in force (see <see cref="Keymap"/>); the built-in keys until <see cref="Use"/>.</summary>
    public static IReadOnlyList<EditorCommand> All { get; private set; } = Defaults;

    public static void Use(IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> keys) =>
        All = [.. Defaults.Select(c => c with { Keys = keys.GetValueOrDefault(c.Id, []) })];

    public static EditorCommand Get(string id) => All.First(c => c.Id == id);

    /// <summary>The command a key press runs: an exact match of key and modifiers.</summary>
    public static EditorCommand? Match(KeyEventArgs e) => All.FirstOrDefault(c => c.Keys.Any(k => k.Matches(e)));
}

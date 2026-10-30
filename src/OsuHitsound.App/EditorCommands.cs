using Avalonia.Input;

namespace OsuHitsound.App;

/// <summary>A keyboard command: its id (what the window runs), where it shows in help, and its keys.</summary>
internal sealed record EditorCommand(string Id, string Group, string Description, IReadOnlyList<KeyGesture> Keys)
{
    public string KeysText => string.Join(", ", Keys.Select(Name));

    /// <summary>A key as printed on the keyboard: "1" not "D1", "," not "OemComma".</summary>
    public static string Name(KeyGesture gesture)
    {
        string key = gesture.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(gesture.Key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"Numpad {(int)(gesture.Key - Key.NumPad0)}",
            Key.Divide => "Numpad /",
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

    public static readonly IReadOnlyList<EditorCommand> All =
    [
        new("open", "Files and help", "Open a .osu", [K(Key.O, Ctrl)]),
        new("save", "Files and help", "Save the .osu (a backup is kept outside the mapset)", [K(Key.S, Ctrl)]),
        new("preferences", "Files and help", "Preferences", [K(Key.OemComma, Ctrl)]),
        new("help", "Files and help", "This shortcut overview", [K(Key.F1)]),
        new("inspector", "Files and help", "UI inspector", [K(Key.I, Ctrl | Shift)]),

        new("play", "Playback and cycle", "Play / stop", [K(Key.Space)]),
        new("start", "Playback and cycle", "Go to the start", [K(Key.Home)]),
        new("stop", "Playback and cycle", "Stop (again: to the start)", [K(Key.NumPad0)]),
        new("cycle", "Playback and cycle", "Cycle between the L and R locators", [K(Key.L), K(Key.Divide), K(Key.Oem2)]),
        new("locatorsToSelection", "Playback and cycle", "Locators to the selection (or the visible range)", [K(Key.P)]),
        new("playLocatorRange", "Playback and cycle", "Locators to the selection, cycle on, play from L", [K(Key.P, Alt)]),
        new("jumpLeft", "Playback and cycle", "Jump to the left locator", [K(Key.NumPad1)]),
        new("jumpRight", "Playback and cycle", "Jump to the right locator", [K(Key.NumPad2)]),
        new("setLeft", "Playback and cycle", "Set the left locator at the playhead", [K(Key.NumPad1, Ctrl)]),
        new("setRight", "Playback and cycle", "Set the right locator at the playhead", [K(Key.NumPad2, Ctrl)]),

        new("toolSelect", "Tools and snap", "Select and move", [K(Key.D1)]),
        new("toolDraw", "Tools and snap", "Draw (hold Alt to draw with any tool)", [K(Key.D3)]),
        new("toolErase", "Tools and snap", "Erase", [K(Key.D4)]),
        new("toolMute", "Tools and snap", "Mute hits while listening (never saved)", [K(Key.D5)]),
        new("toolRamp", "Tools and snap", "Ramp volumes in the volume lane", [K(Key.D6)]),
        new("snap", "Tools and snap", "Snapping on / off", [K(Key.J)]),

        new("undo", "Editing", "Undo", [K(Key.Z, Ctrl)]),
        new("redo", "Editing", "Redo", [K(Key.Y, Ctrl), K(Key.Z, Ctrl | Shift)]),
        new("copy", "Editing", "Copy", [K(Key.C, Ctrl)]),
        new("cut", "Editing", "Cut", [K(Key.X, Ctrl)]),
        new("paste", "Editing", "Paste at the playhead", [K(Key.V, Ctrl)]),
        new("duplicate", "Editing", "Duplicate the selection after itself", [K(Key.D, Ctrl)]),
        new("selectAll", "Editing", "Select all hits", [K(Key.A, Ctrl)]),
        new("deselect", "Editing", "Select nothing", [K(Key.A, Ctrl | Shift), K(Key.Escape)]),
        new("delete", "Editing", "Delete the selection", [K(Key.Delete), K(Key.Back)]),
        new("nudgeLeft", "Editing", "Nudge left by one snap step", [K(Key.Left)]),
        new("nudgeRight", "Editing", "Nudge right by one snap step", [K(Key.Right)]),
        new("nudgeLeftFine", "Editing", "Nudge left by 1 ms", [K(Key.Left, Alt)]),
        new("nudgeRightFine", "Editing", "Nudge right by 1 ms", [K(Key.Right, Alt)]),
        new("gridLeft", "Editing", "Move to the previous grid line", [K(Key.Left, Ctrl)]),
        new("gridRight", "Editing", "Move to the next grid line", [K(Key.Right, Ctrl)]),
        new("laneUp", "Editing", "Move the selection to the lane above", [K(Key.Up, Ctrl)]),
        new("laneDown", "Editing", "Move the selection to the lane below", [K(Key.Down, Ctrl)]),

        new("zoomIn", "View and lanes", "Zoom in", [K(Key.H)]),
        new("zoomOut", "View and lanes", "Zoom out", [K(Key.G)]),
        new("filter", "View and lanes", "Filter lanes by name", [K(Key.F, Ctrl)]),

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
        ("Mouse: grid", "Click / drag (Draw)", "Paint hits; a stroke starting on a hit erases"),
        ("Mouse: grid", "Right-click a hit", "Delete it"),
        ("Mouse: grid", "Alt+drag", "Draw with any tool"),
        ("Mouse: zoom and scroll", "Ctrl+wheel", "Zoom in time around the pointer"),
        ("Mouse: zoom and scroll", "Ctrl+Alt+wheel", "Zoom rows (vertical zoom)"),
        ("Mouse: zoom and scroll", "Shift+wheel", "Scroll in time"),
        ("Mouse: zoom and scroll", "Alt+wheel", "Snap finer / coarser"),
        ("Mouse: ruler and strips", "Click the ruler or waveform", "Move the playhead (onto a bookmark when near one)"),
        ("Mouse: ruler and strips", "Ctrl+click / Alt+click the ruler", "Set the left / right locator"),
        ("Mouse: ruler and strips", "Drag the upper ruler", "Draw a cycle range; click inside it to toggle cycling"),
        ("Mouse: ruler and strips", "Drag the line under the waveform", "Resize it; double-click resets"),
        ("Mouse: ruler and strips", "Click / double-click an object", "Select its hits / also play them"),
        ("Mouse: lanes", "Click a lane name", "Hear its sample"),
        ("Mouse: lanes", "Ctrl+click / Shift+click a lane name", "Select / add its hits"),
        ("Mouse: lanes", "Right-click a lane name", "Rename, move, delete or add lanes"),
        ("Mouse: volume lane", "Drag a bar", "Set its volume (all selected moments together)"),
        ("Mouse: volume lane", "Alt+drag", "Paint volumes"),
        ("Mouse: volume lane", "Shift+drag, or the Ramp tool", "Draw a ramp"),
    ];

    public static EditorCommand Get(string id) => All.First(c => c.Id == id);

    /// <summary>The command a key press runs: an exact match of key and modifiers.</summary>
    public static EditorCommand? Match(KeyEventArgs e) => All.FirstOrDefault(c => c.Keys.Any(k => k.Matches(e)));
}

using Avalonia.Controls;
using Avalonia.Input;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

/// <summary>
/// Keys in the key editor go through the shared key bindings (<see cref="EditorCommands"/>): the commands both modes
/// have keep their keys and meaning here, and the key editor's own ("ks.") appear in Preferences → Keys and F1 like the
/// rest. A Cubase key file applies to both modes.
/// </summary>
/// <summary>
/// The shared commands <see cref="KeysoundEditor.RunShortcut"/> runs, with the Keysound area F1 also lists them under.
/// Kept next to the switch: a case added there belongs here too. "snapN" (1/N snap) is matched by <see cref="AreaOf"/>.
/// </summary>
internal static class KeysoundShortcuts
{
    private static readonly IReadOnlyDictionary<string, string> Shared = new Dictionary<string, string>
    {
        ["play"] = Areas.KsPlayback, ["start"] = Areas.KsPlayback, ["stop"] = Areas.KsPlayback, ["cycle"] = Areas.KsPlayback,
        ["jumpLeft"] = Areas.KsPlayback, ["jumpRight"] = Areas.KsPlayback, ["setLeft"] = Areas.KsPlayback, ["setRight"] = Areas.KsPlayback,
        ["seekBack"] = Areas.KsPlayback, ["seekForward"] = Areas.KsPlayback, ["seekBackFar"] = Areas.KsPlayback, ["seekForwardFar"] = Areas.KsPlayback,
        ["first"] = Areas.KsPlayback, ["last"] = Areas.KsPlayback, ["locatorsToSelection"] = Areas.KsPlayback, ["playLocatorRange"] = Areas.KsPlayback,

        ["toolSelect"] = Areas.KsNoteEditing, ["toolGrab"] = Areas.KsNoteEditing, ["toolDraw"] = Areas.KsNoteEditing, ["toolErase"] = Areas.KsNoteEditing,
        ["snap"] = Areas.KsNoteEditing, ["undo"] = Areas.KsNoteEditing, ["redo"] = Areas.KsNoteEditing, ["cut"] = Areas.KsNoteEditing,
        ["copy"] = Areas.KsNoteEditing, ["paste"] = Areas.KsNoteEditing, ["duplicate"] = Areas.KsNoteEditing, ["delete"] = Areas.KsNoteEditing,
        ["selectAll"] = Areas.KsNoteEditing, ["deselect"] = Areas.KsNoteEditing, ["quantize"] = Areas.KsNoteEditing, ["zoomIn"] = Areas.KsNoteEditing,
        ["zoomOut"] = Areas.KsNoteEditing, ["nudgeLeft"] = Areas.KsNoteEditing, ["nudgeRight"] = Areas.KsNoteEditing,
        ["volumeUp"] = Areas.KsNoteEditing, ["volumeDown"] = Areas.KsNoteEditing, ["volumeUpFine"] = Areas.KsNoteEditing, ["volumeDownFine"] = Areas.KsNoteEditing,
        ["navUp"] = Areas.KsNoteEditing, ["navDown"] = Areas.KsNoteEditing, ["navAddUp"] = Areas.KsNoteEditing, ["navAddDown"] = Areas.KsNoteEditing,
        ["selectPrevious"] = Areas.KsNoteEditing, ["selectNext"] = Areas.KsNoteEditing, ["addPrevious"] = Areas.KsNoteEditing, ["addNext"] = Areas.KsNoteEditing,
        ["rowsOut"] = Areas.KsNoteEditing, ["rowsIn"] = Areas.KsNoteEditing,
        // The Hitsound editor's other editing and timeline commands, with the same keys.
        ["jumpToSelection"] = Areas.KsPlayback, ["addBookmark"] = Areas.KsPlayback, ["removeBookmark"] = Areas.KsPlayback,
        ["previousBookmark"] = Areas.KsPlayback, ["nextBookmark"] = Areas.KsPlayback,
        ["toolMute"] = Areas.KsNoteEditing, ["toolRamp"] = Areas.KsNoteEditing, ["quantizeDialog"] = Areas.KsNoteEditing,
        ["repeat"] = Areas.KsNoteEditing, ["gridLeft"] = Areas.KsNoteEditing, ["gridRight"] = Areas.KsNoteEditing,
        ["laneUp"] = Areas.KsNoteEditing, ["laneDown"] = Areas.KsNoteEditing,
        // Live input: the Hitsound editor's Live and Record are its musical typing and Record.
        ["ks.record"] = Areas.KsNoteInput, ["ks.typing"] = Areas.KsNoteInput,
        ["toolRange"] = Areas.KsNoteEditing, ["editRange"] = Areas.KsNoteEditing, ["scaleByVolume"] = Areas.KsNoteEditing,
    };

    /// <summary>The Keysound area of a shared command the key editor runs too, else null (the key editor's own "ks." ones included).</summary>
    public static string? AreaOf(string id) =>
        Shared.GetValueOrDefault(id) ?? (IsSnapDivision(id) ? Areas.KsNoteEditing : null);

    /// <summary>"snap1" … "snap9": snap to 1/N.</summary>
    public static bool IsSnapDivision(string id) => id.StartsWith("snap", StringComparison.Ordinal) && int.TryParse(id[4..], out _);
}

public partial class KeysoundEditor
{
    /// <summary>A key press in the key editor. False for keys this editor has no command for.</summary>
    public bool HandleKey(KeyEventArgs e) => doc is not null && EditorCommands.Match(e) is { } command && RunShortcut(command.Id);

    /// <summary>Runs a command by its key-binding id; false when Keysound mode has no such command.</summary>
    internal bool RunShortcut(string id)
    {
        if (doc is null) return false;
        switch (id)
        {
            case "play": TogglePlay(); return true;
            case "start": Seek(0); return true;
            case "stop": if (audio?.IsPlaying == true || IsRecording) TogglePlay(); else Seek(0); return true;
            case "cycle": LoopButton.IsChecked = LoopButton.IsChecked != true; ToggleCycle(); return true;
            case "jumpLeft": if (locatorLeft is { } l) Seek(l); return true;
            case "seekBack": SeekGridSteps(-1); return true;
            case "seekForward": SeekGridSteps(1); return true;
            case "seekBackFar": SeekGridSteps(-4); return true;
            case "seekForwardFar": SeekGridSteps(4); return true;
            case "jumpRight": if (locatorRight is { } r) Seek(r); return true;
            case "setLeft": SetLocator(true, Roll.Playhead); return true;
            case "setRight": SetLocator(false, Roll.Playhead); return true;
            case "locatorsToSelection": LocatorsToSelection(); return true;
            case "playLocatorRange": PlayLocatorRange(); return true;
            case "jumpToSelection":
                // Alt+Space, as in the Hitsound editor: the playhead to the first selected note.
                if (Roll.Notes.Selected.Count == 0) SetStatus("Select a note first: Alt+Space moves the playhead to it.");
                else Seek(Math.Max(0, doc.Tempo.TimeAt(Roll.Notes.Selected.Min(n => n.Start))));
                return true;
            case "previousBookmark": JumpToBookmark(-1); return true;
            case "nextBookmark": JumpToBookmark(1); return true;
            case "first": Seek(FirstOrLast(first: true)); return true;
            case "last": Seek(FirstOrLast(first: false)); return true;

            case "toolSelect": SetTool(KeyTool.Select); return true;
            case "toolGrab": SetTool(KeyTool.Grab); return true;
            case "toolDraw": SetTool(KeyTool.Draw); return true;
            case "toolErase": SetTool(KeyTool.Erase); return true;
            case "toolMute": SetTool(KeyTool.Mute); return true;
            case "toolRange": SetTool(KeyTool.Range); return true;
            case "toolRamp": SetTool(KeyTool.Ramp); return true;
            case "ks.toolSplit": SetTool(KeyTool.Split); return true;
            case "ks.toolGlue": SetTool(KeyTool.Glue); return true;
            case "snap": SnapToggle.IsChecked = SnapToggle.IsChecked != true; return true;
            case var s when s.StartsWith("snap", StringComparison.Ordinal) && int.TryParse(s[4..], out int n):
                int index = n > 0 ? Array.IndexOf(MainWindow.SnapOptions, new SnapSpacing(1, n)) : -1;
                if (index >= 0) SnapBox.SelectedIndex = index;
                return true;

            case "undo": case "redo": case "cut": case "copy": case "paste": case "duplicate": case "delete":
            case "selectAll": case "quantize": case "zoomIn": case "zoomOut": case "quantizeDialog": case "repeat":
            case "addBookmark": case "removeBookmark": case "editRange": case "scaleByVolume":
                _ = RunCommand(id);
                return true;
            case "deselect": if (!Roll.CancelDrag()) Roll.Notes.Deselect(); return true;
            case "nudgeLeft": Roll.Notes.Nudge(-1); return true;
            case "volumeUp": Roll.Notes.SetSelectionVelocity(v => v + 5); return true;
            case "volumeDown": Roll.Notes.SetSelectionVelocity(v => v - 5); return true;
            case "volumeUpFine": Roll.Notes.SetSelectionVelocity(v => v + 1); return true;
            case "volumeDownFine": Roll.Notes.SetSelectionVelocity(v => v - 1); return true;
            case "nudgeRight": Roll.Notes.Nudge(1); return true;
            case "gridLeft": Roll.Notes.NudgeToGrid(-1); return true;
            case "gridRight": Roll.Notes.NudgeToGrid(1); return true;

            case "ks.record": ToggleRecord(); return true;
            case "ks.typing": ToggleTyping(); return true;
            // The arrow keys as in Cubase's Key Editor: Up/Down transpose (Shift: an octave), Left/Right select.
            case "navUp": Roll.Notes.Transpose(1); return true;
            case "navDown": Roll.Notes.Transpose(-1); return true;
            case "navAddUp": Roll.Notes.Transpose(12); return true;
            case "navAddDown": Roll.Notes.Transpose(-12); return true;
            // Ctrl+Alt+Up/Down move the selection a row, as Up/Down do: in the key editor, a semitone.
            case "laneUp": Roll.Notes.Transpose(1); return true;
            case "laneDown": Roll.Notes.Transpose(-1); return true;
            case "selectPrevious": Roll.Notes.SelectNeighbour(-1, extend: false); return true;
            case "selectNext": Roll.Notes.SelectNeighbour(1, extend: false); return true;
            case "addPrevious": Roll.Notes.SelectNeighbour(-1, extend: true); return true;
            case "addNext": Roll.Notes.SelectNeighbour(1, extend: true); return true;
            case "rowsOut": Roll.ZoomRows(1 / 1.2); return true;
            case "rowsIn": Roll.ZoomRows(1.2); return true;
            // Cubase's Nudge Start/End: Alt+Left the start earlier, Alt+Shift+Right later; Alt+Right the end later, Alt+Shift+Left earlier.
            case "ks.startLeft": Roll.Notes.NudgeEdges(-1, 0); return true;
            case "ks.startRight": Roll.Notes.NudgeEdges(1, 0); return true;
            case "ks.endRight": Roll.Notes.NudgeEdges(0, 1); return true;
            case "ks.endLeft": Roll.Notes.NudgeEdges(0, -1); return true;
            case "ks.sync": _ = RunCommand("sync"); return true;
            case "ks.generate": _ = RunCommand("generate"); return true;
            default:
                // F1 lists every shared command under a Keysound area too; one without a case here would be listed wrongly.
                System.Diagnostics.Debug.Assert(KeysoundShortcuts.AreaOf(id) is null, $"KeysoundShortcuts lists \"{id}\" but RunShortcut has no case for it.");
                return false;
        }
    }

    // Z / V: the first or last note of the selected track (or the song's start when there are none).
    private double FirstOrLast(bool first)
    {
        var notes = doc?.SelectedTrack is { } id ? doc.Session.State.Track(id)?.Notes : null;
        if (doc is null || notes is not { Count: > 0 }) return 0;
        long tick = first ? notes.Min(n => n.Start) : notes.Max(n => n.Start);
        return Math.Max(0, doc.Tempo.TimeAt(tick));
    }

    // Alt+P, as in Hitsound mode: the locators to the selected notes, Cycle on, and play from L.
    private void PlayLocatorRange()
    {
        LocatorsToSelection();
        if (locatorLeft is not { } l) { SetStatus("Select notes or set the L and R locators first."); return; }
        LoopButton.IsChecked = true;
        ToggleCycle();
        if (audio?.IsPlaying != true && !IsRecording) { Seek(l); TogglePlay(); }
        else Seek(l);
    }

    // P: the locators around the selected notes.
    private void LocatorsToSelection()
    {
        if (Roll.Notes.Range is { } range)
        {
            (locatorLeft, locatorRight) = (range.Start, range.End);
            ShowLocators();
            return;
        }

        var notes = Roll.Notes.Selected;
        if (doc is null || notes.Count == 0) return;
        locatorLeft = Math.Max(0, doc.Tempo.TimeAt(notes.Min(n => n.Start)));
        locatorRight = Math.Max(0, doc.Tempo.TimeAt(notes.Max(n => n.End)));
        ShowLocators();
    }

    /// <summary>The keys changed (Preferences → Keys): the tools' tooltips show the keys in force.</summary>
    public void RefreshShortcutTips()
    {
        static string Keys(string id) => EditorCommands.All.FirstOrDefault(c => c.Id == id)?.KeysText is { Length: > 0 } k ? $" ({k})" : "";
        ToolTip.SetTip(SelectTool, $"Select and move{Keys("toolSelect")}. Click, Ctrl/Shift+click, drag a box; drag notes to move them (Ctrl: one direction, Ctrl+Alt: copy); drag either edge to resize; Ctrl+Shift+drag changes velocity. Double-click empty space inserts a note; Shift+double-click selects the later notes of that pitch.");
        ToolTip.SetTip(GrabTool, $"Grab{Keys("toolGrab")}. Drag the canvas to pan time and keys. Space plays or stops.");
        ToolTip.SetTip(DrawTool, $"Draw{Keys("toolDraw")}, or hold Alt. Click to add a note at the insert velocity, drag right to set its length; clicking a note deletes it.");
        ToolTip.SetTip(EraseTool, $"Erase{Keys("toolErase")}: click or drag across notes to delete them");
        ToolTip.SetTip(MuteTool, $"Mute{Keys("toolMute")}: click a note to silence it while listening. Preview only: never saved, and Generate still renders it.");
        ToolTip.SetTip(RampTool, $"Ramp{Keys("toolRamp")}: drag in the velocity lane to draw a velocity ramp");
        ToolTip.SetTip(SplitTool, $"Split{Keys("ks.toolSplit")}: click a note to split it at the (snapped) pointer; on a selected note, every selected note splits there");
        ToolTip.SetTip(GlueTool, $"Glue{Keys("ks.toolGlue")}: click a note to join it with the next note of the same pitch");
        ToolTip.SetTip(RecordButton, $"Record{Keys("ks.record")}: from the playhead into the selected track, from your MIDI keyboard. Notes starting in the recorded range are replaced; Space or Record stops.");
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

/// <summary>
/// Keys in the key editor go through the shared key bindings (<see cref="EditorCommands"/>): the commands both modes
/// have keep their keys and meaning here, and the key editor's own ("ks.") appear in Preferences → Keys and F1 like the
/// rest. A Cubase key file applies to both modes.
/// </summary>
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
            case "jumpRight": if (locatorRight is { } r) Seek(r); return true;
            case "setLeft": SetLocator(true, Roll.Playhead); return true;
            case "setRight": SetLocator(false, Roll.Playhead); return true;
            case "locatorsToSelection": LocatorsToSelection(); return true;
            case "first": Seek(FirstOrLast(first: true)); return true;
            case "last": Seek(FirstOrLast(first: false)); return true;

            case "toolSelect": SetTool(KeyTool.Select); return true;
            case "toolGrab": SetTool(KeyTool.Grab); return true;
            case "toolDraw": SetTool(KeyTool.Draw); return true;
            case "toolErase": SetTool(KeyTool.Erase); return true;
            case "ks.toolSplit": SetTool(KeyTool.Split); return true;
            case "ks.toolGlue": SetTool(KeyTool.Glue); return true;
            case "snap": SnapToggle.IsChecked = SnapToggle.IsChecked != true; return true;
            case var s when s.StartsWith("snap", StringComparison.Ordinal) && int.TryParse(s[4..], out int n):
                int index = Array.FindIndex(Snaps, x => x.Divisions == n);
                if (index >= 0) SnapBox.SelectedIndex = index;
                return true;

            case "undo": case "redo": case "cut": case "copy": case "paste": case "duplicate": case "delete":
            case "selectAll": case "quantize": case "zoomIn": case "zoomOut":
                _ = RunCommand(id);
                return true;
            case "deselect": if (!Roll.CancelDrag()) Roll.Deselect(); return true;
            case "nudgeLeft": Roll.Nudge(-1); return true;
            case "nudgeRight": Roll.Nudge(1); return true;

            case "ks.record": ToggleRecord(); return true;
            case "ks.transposeUp": Roll.Transpose(1); return true;
            case "ks.transposeDown": Roll.Transpose(-1); return true;
            case "ks.octaveUp": Roll.Transpose(12); return true;
            case "ks.octaveDown": Roll.Transpose(-12); return true;
            case "ks.selectPrevious": Roll.SelectNeighbour(-1, extend: false); return true;
            case "ks.selectNext": Roll.SelectNeighbour(1, extend: false); return true;
            case "ks.sync": _ = RunCommand("sync"); return true;
            case "ks.generate": _ = RunCommand("generate"); return true;
            default: return false;
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

    // P: the locators around the selected notes.
    private void LocatorsToSelection()
    {
        var notes = Roll.Selected;
        if (doc is null || notes.Count == 0) return;
        locatorLeft = Math.Max(0, doc.Tempo.TimeAt(notes.Min(n => n.Start)));
        locatorRight = Math.Max(0, doc.Tempo.TimeAt(notes.Max(n => n.End)));
        ShowLocators();
    }

    /// <summary>The keys changed (Preferences → Keys): the tools' tooltips show the keys in force.</summary>
    public void RefreshShortcutTips()
    {
        static string Keys(string id) => EditorCommands.All.FirstOrDefault(c => c.Id == id)?.KeysText is { Length: > 0 } k ? $" ({k})" : "";
        ToolTip.SetTip(SelectTool, $"Object Selection{Keys("toolSelect")}: click or box-select notes; drag to move (Ctrl: one direction, Alt: copy); drag either edge to resize; Ctrl+Shift+drag changes velocity; Shift+double-click selects the later notes of that pitch");
        ToolTip.SetTip(GrabTool, $"Grab{Keys("toolGrab")}: drag to pan time and keys");
        ToolTip.SetTip(DrawTool, $"Draw{Keys("toolDraw")}: click to add a note at the insert velocity, drag right to set its length; clicking a note deletes it");
        ToolTip.SetTip(EraseTool, $"Erase{Keys("toolErase")}: click or drag across notes to delete them");
        ToolTip.SetTip(SplitTool, $"Split{Keys("ks.toolSplit")}: click a note to split it at the (snapped) pointer; on a selected note, every selected note splits there");
        ToolTip.SetTip(GlueTool, $"Glue{Keys("ks.toolGlue")}: click a note to join it with the next note of the same pitch");
        ToolTip.SetTip(RecordButton, $"Record{Keys("ks.record")}: from the playhead into the selected track, from your MIDI keyboard. Notes starting in the recorded range are replaced; Space or Record stops.");
    }
}

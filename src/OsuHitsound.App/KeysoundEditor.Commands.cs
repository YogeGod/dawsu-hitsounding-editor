using Avalonia.Controls;

namespace OsuHitsound.App;

/// <summary>
/// The key editor's commands by id, for the title bar's Keysound menus (and, later, the shared key bindings). Each one
/// does what its toolbar button or shortcut does.
/// </summary>
public partial class KeysoundEditor
{
    public static readonly IReadOnlyList<string> CommandIds =
    [
        "undo", "redo", "cut", "copy", "paste", "duplicate", "delete", "selectAll", "quantize", "sync",
        "importMidi", "generate", "projectFolder", "close",
        "addTrack", "renameTrack", "deleteTrack", "instrument", "instrumentEditor",
        "ghosts", "objectLines", "zoomIn", "zoomOut", "quantizeDialog", "repeat", "addBookmark", "removeBookmark", "editRange", "scaleByVolume", "rangeTool",
    ];

    /// <summary>Runs a command (see <see cref="CommandIds"/>); unknown ids do nothing.</summary>
    public async Task RunCommand(string id)
    {
        if (doc is null) return;
        switch (id)
        {
            case "undo": doc.Session.Undo(); break;
            case "redo": doc.Session.Redo(); break;
            case "cut": clipboard = Roll.CopySelection(); Roll.DeleteSelection(); break;
            case "copy": clipboard = Roll.CopySelection(); break;
            case "paste": Roll.Paste(clipboard, Roll.Playhead); break;
            case "duplicate": Roll.Duplicate(); break;
            case "delete": Roll.DeleteSelection(); break;
            case "selectAll": Roll.SelectAll(); break;
            case "quantize": Roll.QuantizeSelection(QuantizeStrength); break;
            case "quantizeDialog": await QuantizeDialog(); break;
            case "repeat": await Repeat(); break;
            case "editRange": await EditRange(); break;
            case "scaleByVolume": await ScaleByVelocity(); break;
            case "addBookmark": AddBookmark(); break;
            case "removeBookmark": doc.Session.RemoveBookmarkNear(Roll.Playhead, 8 / Roll.Axis.Zoom); break;
            case "sync": await SyncNotes(); break;
            case "importMidi": await ImportMidi(); break;
            case "generate": await Generate(); break;
            case "projectFolder": OpenProjectFolder(); break;
            case "close": RequestClose(); break;
            case "addTrack": AddTrack(); break;
            case "renameTrack": await RenameTrack(); break;
            case "deleteTrack": DeleteTrack(); break;
            case "instrument": await ChooseInstrument(); break;
            case "instrumentEditor": await OpenEditor(); break;
            case "ghosts": GhostToggle.IsChecked = GhostToggle.IsChecked != true; break;
            case "objectLines": MarkerToggle.IsChecked = MarkerToggle.IsChecked != true; break;
            case "zoomIn": Roll.ZoomTime(1.25); break;
            case "zoomOut": Roll.ZoomTime(0.8); break;
            case "rangeTool": SetTool(KeyTool.Range); break;
            case "inspector": InspectorToggle.IsChecked = InspectorToggle.IsChecked != true; break;
            case "controller": VelocityToggle.IsChecked = VelocityToggle.IsChecked != true; break;
            case "returnOnStop": ReturnOnStopButton.IsChecked = ReturnOnStopButton.IsChecked != true; break;
            case "follow": FollowPlaybackButton.IsChecked = FollowPlaybackButton.IsChecked != true; break;
        }

        Roll.Focus();
    }

    /// <summary>The strength Q quantizes with, in % (the window keeps it with the Hitsound editor's).</summary>
    public double QuantizeStrength { get; set; } = 100;

    /// <summary>Quantize… as in the Hitsound editor: asks for a strength, then quantizes with it (Q uses it next time).</summary>
    private async Task QuantizeDialog()
    {
        if (doc is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var text = await Dialogs.Prompt(owner, "Quantize", "Strength (1–100 %): how far the selected notes move toward the grid",
            QuantizeStrength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (text is null || !OsuHitsound.Format.OsuNumber.TryParseDouble(text, out double strength) || !double.IsFinite(strength)) return;
        QuantizeStrength = Math.Clamp(strength, 1, 100);
        Roll.QuantizeSelection(QuantizeStrength);
    }

    /// <summary>Ctrl+R, as in the Hitsound editor: how many copies and how many snap steps apart.</summary>
    private async Task Repeat()
    {
        if (doc is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (Roll.Selected.Count == 0) { SetStatus("Select some notes to repeat first."); return; }
        if (await Dialogs.Repeat(owner, Roll.DefaultRepeatGap(), Roll.Spacing) is { } r) Roll.Repeat(r.Copies, r.Gap);
    }

    /// <summary>Edit range…, as in the Hitsound editor: copy, move or delete the notes in the range marked with the Range tool.</summary>
    private async Task EditRange()
    {
        if (doc is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (Roll.Range is not { } range)
        {
            SetStatus("Choose the Range tool and drag a time range over the keys first.");
            return;
        }

        if (await Dialogs.EditRange(owner, range.End - range.Start) is not { } choice) return;
        if (Roll.EditRange(choice.Operation, choice.Offset) == 0) SetStatus("The range has no notes of the selected track.");
    }

    /// <summary>Scale by velocity…, the key editor's Scale by volume: louder or quieter notes in a velocity range.</summary>
    private async Task ScaleByVelocity()
    {
        if (doc is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (await Dialogs.ScaleByVelocity(owner, Roll.Selected.Count > 0, (scope, from, to) => Roll.NotesToScale(scope, from, to).Count) is { } c)
            Roll.ScaleVelocity(c.Scope, c.From, c.To, c.Percent);
    }

    /// <summary>Whether a command can run now (for greying out menu items).</summary>
    public bool CanRun(string id) => doc is not null && id switch
    {
        "undo" => doc.Session.CanUndo,
        "redo" => doc.Session.CanRedo,
        "cut" or "copy" or "duplicate" or "delete" or "quantize" or "quantizeDialog" or "repeat" => Roll.Selected.Count > 0,
        "paste" => clipboard.Count > 0 && doc.SelectedTrack is not null,
        "renameTrack" or "deleteTrack" or "instrument" => doc.SelectedTrack is not null,
        "instrumentEditor" => SelectedTrackPart()?.Vst is not null,
        "editRange" => Roll.Range is not null,
        _ => true,
    };

    public bool GhostsShown => GhostToggle.IsChecked == true;
    public bool ReturnOnStop => ReturnOnStopButton.IsChecked == true;
    public bool FollowPlayback => FollowPlaybackButton.IsChecked == true;
    public bool ObjectLinesShown => MarkerToggle.IsChecked == true;
}

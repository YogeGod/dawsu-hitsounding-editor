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
        "ghosts", "objectLines", "zoomIn", "zoomOut",
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
            case "quantize": Roll.QuantizeSelection(); break;
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
        }

        Roll.Focus();
    }

    /// <summary>Whether a command can run now (for greying out menu items).</summary>
    public bool CanRun(string id) => doc is not null && id switch
    {
        "undo" => doc.Session.CanUndo,
        "redo" => doc.Session.CanRedo,
        "cut" or "copy" or "duplicate" or "delete" or "quantize" => Roll.Selected.Count > 0,
        "paste" => clipboard.Count > 0 && doc.SelectedTrack is not null,
        "renameTrack" or "deleteTrack" or "instrument" => doc.SelectedTrack is not null,
        "instrumentEditor" => SelectedTrackPart()?.Vst is not null,
        _ => true,
    };

    public bool GhostsShown => GhostToggle.IsChecked == true;
    public bool ObjectLinesShown => MarkerToggle.IsChecked == true;
}

using Avalonia.Controls;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private bool closePromptOpen;
    private bool discardProjectOnClose;
    private ProjectData? closeProjectBaseline;

    private async void OnEditorClosing(object? sender, WindowClosingEventArgs e)
    {
        if (renderingDrums || savingHs || generating || !Loading.IsCompleted) { e.Cancel = true; updateOnClose = null; return; }
        // While Keysound shows, the hitsound map's edits were already given up when Keysound opened (MayLeaveMap).
        if (!closeConfirmed && Keys.Document is { } keys)
        {
            Keys.StopRecording();
            if (!keys.Session.IsEdited) return;
            e.Cancel = true;
            if (closePromptOpen) return;
            closePromptOpen = true;
            try
            {
                if (await MayLeaveKeysound())
                {
                    closeConfirmed = true;
                    Close();
                }
                else updateOnClose = null; // cancelled: stay open, and don't update
            }
            finally { closePromptOpen = false; }
            return;
        }

        if (closeConfirmed || session is not { IsEdited: true }) return;
        e.Cancel = true;
        if (closePromptOpen) return;
        closePromptOpen = true;
        try
        {
            var document = doc;
            var state = session.State;
            var choice = await Dialogs.UnsavedClose(this, Path.GetFileName(mapPath) ?? "this difficulty");
            if (!ReferenceEquals(doc, document) || !ReferenceEquals(session?.State, state)) { updateOnClose = null; return; }
            bool close = choice == CloseChoice.Save && await Save();
            if (choice == CloseChoice.Discard)
            {
                discardProjectOnClose = true;
                close = SaveProject();
                if (!close) discardProjectOnClose = false;
            }
            if (close)
            {
                closeConfirmed = true;
                Close();
            }
            else updateOnClose = null; // cancelled: stay open, and don't update
        }
        finally { closePromptOpen = false; }
    }
}

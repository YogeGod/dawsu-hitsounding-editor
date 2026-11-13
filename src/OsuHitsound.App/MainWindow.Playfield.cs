using Avalonia;
using Avalonia.Input;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// The map preview (View → Map preview, and the ribbon's VIEW group): a window showing the playfield with the map's
/// objects at the playhead, as osu!'s editor shows them. It follows playback and every move of the playhead.
/// </summary>
public partial class MainWindow
{
    private PlayfieldWindow? playfieldWindow;
    private OsuFile? playfieldFile;
    private (PixelPoint Position, Size Size)? playfieldPlacement;

    private void SetupPlayfield()
    {
        if (prefs is { PlayfieldX: { } x, PlayfieldY: { } y, PlayfieldWidth: > 100 and var w, PlayfieldHeight: > 100 and var h })
            playfieldPlacement = (new PixelPoint((int)x, (int)y), new Size(w, h));
        PlayfieldToggle.IsCheckedChanged += (_, _) => ShowPlayfield(PlayfieldToggle.IsChecked == true);
        // An owned window can only open once its owner is on screen.
        if (prefs.PlayfieldOpen) Opened += (_, _) => PlayfieldToggle.IsChecked = true;
        Closed += (_, _) =>
        {
            if (playfieldWindow is { } open) playfieldPlacement = open.Placement;
            prefs = prefs with
            {
                PlayfieldOpen = PlayfieldToggle.IsChecked == true,
                PlayfieldX = playfieldPlacement?.Position.X, PlayfieldY = playfieldPlacement?.Position.Y,
                PlayfieldWidth = playfieldPlacement?.Size.Width, PlayfieldHeight = playfieldPlacement?.Size.Height,
            };
            SavePrefs();
        };
    }

    private void ShowPlayfield(bool show)
    {
        if (!show)
        {
            playfieldWindow?.Close();
            return;
        }

        if (playfieldWindow is not null || !IsVisible) return;
        var window = playfieldWindow = new PlayfieldWindow(PlayfieldKey);
        if (playfieldPlacement is var (position, size))
        {
            window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual;
            window.Position = position;
            (window.Width, window.Height) = (size.Width, size.Height);
        }
        else window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner;

        window.Closing += (_, _) => playfieldPlacement = window.Placement;
        window.Closed += (_, _) =>
        {
            playfieldWindow = null;
            playfieldFile = null;
            if (!window.ClosedWithEditor) PlayfieldToggle.IsChecked = false;
        };
        UpdatePlayfield();
        window.Show(this);
        Activate(); // the keyboard stays with the editor
    }

    /// <summary>Gives the preview the open map (built again only when the map changes) and the playhead.</summary>
    private void UpdatePlayfield()
    {
        if (playfieldWindow is not { } window) return;
        var file = doc?.File;
        if (!ReferenceEquals(file, playfieldFile))
        {
            playfieldFile = file;
            window.View.SetMap(file is null ? null : Playfield.Build(file), model?.ComboColours);
        }

        window.View.Playhead = Lanes.Playhead;
    }

    /// <summary>Opens the preview at a time, for the headless snapshots (no frame timer runs there).</summary>
    internal PlayfieldView ShowMapPreviewForSnapshot(double time)
    {
        PlayfieldToggle.IsChecked = true;
        Lanes.Playhead = time;
        UpdatePlayfield();
        return playfieldWindow!.View;
    }

    // A key pressed in the preview window runs the editor's command for it (Space plays, Z and V jump, Ctrl+Z undoes…).
    private void PlayfieldKey(KeyEventArgs e)
    {
        if (e.Handled || Start.IsVisible || Keys.Document is not null || EditorCommands.Match(e) is not { } command) return;
        e.Handled = true;
        Run(command.Id);
    }
}

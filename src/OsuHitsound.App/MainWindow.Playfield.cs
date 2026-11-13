using Avalonia;
using Avalonia.Input;
using Avalonia.Platform.Storage;
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
    private LegacySkin? classicSkin; // lazer's classic skin: itself a choice, and what user skins fall back to
    private bool classicSkinRead;

    private void SetupPlayfield()
    {
        if (prefs is { PlayfieldX: { } x, PlayfieldY: { } y, PlayfieldWidth: > 100 and var w, PlayfieldHeight: > 100 and var h })
            playfieldPlacement = (new PixelPoint((int)x, (int)y), new Size(w, h));
        PlayfieldToggle.IsCheckedChanged += (_, _) => ShowPlayfield(PlayfieldToggle.IsChecked == true);
        // lazer's approach circle, reverse-arrow edge and Torus digits, for the preview's Argon look.
        ArgonPieces.LoadAsync(prefs.LazerResources, () => playfieldWindow?.View.InvalidateVisual());
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
        window.SkinChosen += choice =>
        {
            prefs = prefs with { PlayfieldSkin = choice.Id };
            SavePrefs();
            ApplyPreviewSkin(choice);
        };
        window.SkinsFolderRequested += async () =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Choose osu!'s Skins folder", AllowMultiple = false });
            if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } folder || playfieldWindow != window) return;
            prefs = prefs with { SkinsFolder = folder };
            SavePrefs();
            ApplyPreviewSkin(window.ShowSkins(PreviewSkins(), prefs.PlayfieldSkin));
        };
        ApplyPreviewSkin(window.ShowSkins(PreviewSkins(), prefs.PlayfieldSkin));
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
            window.View.SetMap(file is null ? null : Playfield.Build(file), file is null ? null : ComboColoursAsInLazer(file));
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

    private LegacySkin? ClassicSkin()
    {
        if (!classicSkinRead) (classicSkin, classicSkinRead) = (LegacySkin.LazerClassic(prefs.LazerResources), true);
        return classicSkin;
    }

    /// <summary>lazer's skins and the folders of osu!'s Skins directory (the chosen one, else the one beside the Songs folder).</summary>
    private List<SkinChoice> PreviewSkins() =>
        SkinChoice.All(prefs.SkinsFolder ?? SkinChoice.FindSkinsFolder(prefs.SongsFolder), OsuHitsound.Audio.DefaultSkin.FindLazerResources(prefs.LazerResources) is not null);

    private void ApplyPreviewSkin(SkinChoice choice)
    {
        if (playfieldWindow is not { } window) return;
        window.View.Skin = choice switch
        {
            { Id: SkinChoice.ArgonId } => null,
            { Folder: { } folder } => LegacySkin.FromFolder(folder, ClassicSkin()),
            _ => ClassicSkin(),
        };
        playfieldFile = null; // the combo colours depend on the skin: give the map again
        UpdatePlayfield();
    }

    /// <summary>Shows the preview with a skin, for the headless snapshots: "argon", "classic" or a skin folder's path.</summary>
    internal void SetPreviewSkinForSnapshot(string skin) =>
        ApplyPreviewSkin(Directory.Exists(skin) ? new SkinChoice("folder:" + Path.GetFileName(skin), Path.GetFileName(skin), skin) : new SkinChoice(skin, skin));

    // The colours lazer would use: the map's own; else the skin's (Argon's six, or a legacy skin's, which default
    // to stable's four).
    private IReadOnlyList<Avalonia.Media.Color> ComboColoursAsInLazer(OsuFile file)
    {
        if (OsuHitsound.Lanes.Keysound.KeysoundStore.ComboColours(file) is { Count: > 0 } own)
            return [.. own.Select(c => Avalonia.Media.Color.FromRgb(c.R, c.G, c.B))];
        if (playfieldWindow?.View.Skin is not { } skin) return PlayfieldView.ArgonComboColours;
        for (var s = skin; s is not null; s = s.Fallback)
            if (s.ComboColours.Count > 0) return s.ComboColours;
        return [Avalonia.Media.Color.FromRgb(255, 192, 0), Avalonia.Media.Color.FromRgb(0, 202, 0), Avalonia.Media.Color.FromRgb(18, 124, 255), Avalonia.Media.Color.FromRgb(242, 24, 57)];
    }

    // A key pressed in the preview window runs the editor's command for it (Space plays, Z and V jump, Ctrl+Z undoes…).
    private void PlayfieldKey(KeyEventArgs e)
    {
        if (e.Handled || Start.IsVisible || Keys.Document is not null || EditorCommands.Match(e) is not { } command) return;
        e.Handled = true;
        Run(command.Id);
    }
}

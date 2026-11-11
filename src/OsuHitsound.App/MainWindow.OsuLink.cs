using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// File → Open map from osu!: the map a client serving MTIPC has open (un!stable), the map osu!stable has loaded (read
/// from its memory), or the set osu!lazer is editing externally (File → Edit externally). Live reload
/// (MainWindow.LiveReload) then follows the client's saves, and after Save here the client's editor is asked to reload.
/// </summary>
public partial class MainWindow
{
    /// <summary>Where osu!'s current map is on disk, or why it can't be opened.</summary>
    internal (string? Path, string? Problem) FindOsuMap()
    {
        if (Mtipc.TryReadBeatmap() is { } served) return InSongsFolder(served.Folder, served.OsuFile, served.ServerExecutable, "osu!");
        if (OsuStable.TryRead() is { } stable)
            return stable is { Folder: { Length: > 0 } folder, OsuFile: { Length: > 0 } file }
                ? InSongsFolder(folder, file, stable.Executable, "osu!stable")
                : (null, "osu!stable has no beatmap loaded. Open a map in its editor (or select one), then try again.");
        if (OsuLazerExternalEdit.FindMountedBeatmap() is { } lazer) return (lazer, null);
        return (null, "No osu! map found. In osu!stable or un!stable, open the map in the editor; in osu!lazer, use File → Edit externally in its editor.");
    }

    /// <summary>
    /// The .osu a client names by folder and file: in the Songs folder from Preferences, else beside the client. The
    /// names come from another program, so they must stay inside that Songs folder and name a .osu.
    /// </summary>
    private (string? Path, string? Problem) InSongsFolder(string folder, string file, string? executable, string client)
    {
        var candidates = new[] { prefs.SongsFolder, executable is null ? null : Path.Combine(Path.GetDirectoryName(executable)!, "Songs") }
            .OfType<string>().Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count == 0) return (null, $"Set the osu! Songs folder in Preferences: {client}'s map is in it.");
        foreach (var songs in candidates)
        {
            string path = Path.GetFullPath(Path.Combine(songs, folder, file));
            if (!SafeFile.IsInside(path, songs) || path.Equals(songs, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetExtension(path), ".osu", StringComparison.OrdinalIgnoreCase))
                return (null, $"{client} reported a map outside the Songs folder, so it wasn't opened.");
            if (File.Exists(path)) return (path, null);
        }
        return (null, $"{client}'s map ({file}) isn't in {string.Join(" or ", candidates)}. Check the Songs folder in Preferences.");
    }

    /// <summary>
    /// After Save: if the osu! client's editor has this map open, it reloads it so it shows the new hitsounds. A client
    /// serving MTIPC is asked directly; osu!stable gets its reload shortcut (Ctrl+L, then Enter), as Mapping Tools does.
    /// </summary>
    private async Task ReloadOsuEditor(string path)
    {
        bool Same(string? other) => other is not null && string.Equals(Path.GetFullPath(other), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        bool reloaded = await Task.Run(() =>
        {
            if (Mtipc.TryReadBeatmap() is { } served)
                return Same(InSongsFolder(served.Folder, served.OsuFile, served.ServerExecutable, "osu!").Path) && Mtipc.TryReloadEditor();
            return false;
        });
        if (!reloaded && await Task.Run(OsuStable.TryRead) is { InEditor: true, Folder: { } folder, OsuFile: { } file } stable
            && Same(InSongsFolder(folder, file, stable.Executable, "osu!stable").Path))
            reloaded = await OsuKeys.SendEditorReload(OsuStable.Window());
        if (!reloaded || !IsVisible) return; // after Save from the close prompt, the window has gone
        flash = (flash is { Length: > 0 } f ? f + " " : "") + "osu!'s editor reloaded it.";
        UpdateStatus();
    }

    private async Task OpenFromOsu()
    {
        var (path, problem) = await Task.Run(FindOsuMap);
        if (path is null) { flash = problem; UpdateStatus(); return; }
        if (string.Equals(path, mapPath, StringComparison.OrdinalIgnoreCase) && doc is not null)
        {
            flash = $"{Path.GetFileName(path)} is already open; it reloads whenever osu! saves it.";
            UpdateStatus();
            return;
        }
        if (await MayLeaveMap()) await Open(path);
    }
}

using Avalonia.Threading;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// Live reload: the open .osu is watched, and when another program saves it (osu!'s editor, typically) it reloads at
/// once, keeping the view and putting back the unsaved edits made here (LiveReloadMerge). dawsu!'s own saves are
/// recognised by their content and ignored.
/// </summary>
public partial class MainWindow
{
    private FileSystemWatcher? mapWatcher;
    private DispatcherTimer? mapChangeTimer;
    // The map as opened or last reloaded, after its project was restored: what unsaved edits are measured against.
    // (The session's own first state comes before the project's hits and HS mode are restored, so it can't serve.)
    private EditState? liveBaseline;
    // Bytes the user chose to keep editing over. A later write of the same bytes must not ask again.
    private byte[]? declinedMapBytes;

    /// <summary>Called when a map has opened: watches its file and remembers the state edits are measured against.</summary>
    private void WatchMapFile(string path)
    {
        liveBaseline = session?.State;
        declinedMapBytes = null;
        StopWatchingMap();
        try
        {
            mapWatcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            // osu! may write in place or replace the file: either way, check a moment after the last event.
            void Changed(object? sender, FileSystemEventArgs e) => Dispatcher.UIThread.Post(ScheduleMapCheck);
            mapWatcher.Changed += Changed;
            mapWatcher.Created += Changed;
            mapWatcher.Renamed += (s, e) => Changed(s, e);
            mapWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            StopWatchingMap(); // no live reload for this map (a network drive, say); everything else works
        }
    }

    private void ScheduleMapCheck()
    {
        mapChangeTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, async (_, _) =>
        {
            mapChangeTimer!.Stop();
            await CheckMapChanged();
        });
        mapChangeTimer.Stop();
        mapChangeTimer.Start();
    }

    /// <summary>Reloads when the file's content differs from the map as dawsu! has it (not after dawsu!'s own saves).</summary>
    private async Task CheckMapChanged()
    {
        if (doc is not { } document || session is not { } editing || mapPath is not { } path) return;
        // Busy (a save, render or load, or a drag), or not in the hitsound editor: look again shortly. Only flags are
        // checked meanwhile; the file is read once dawsu! is free.
        if (savingHs || generating || renderingDrums || !Loading.IsCompleted || editing.IsGestureActive || Keys.Document is not null || Start.IsVisible
            || OwnedWindows.Any(w => w.Classes.Contains("editorDialog"))) // a question open: its answer is about this map
        {
            ScheduleMapCheck();
            return;
        }
        byte[] now;
        try { now = await File.ReadAllBytesAsync(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ScheduleMapCheck(); return; } // still being written
        if (SkipMapReload(now, document.Opened, declinedMapBytes) || !ReferenceEquals(doc, document)) return;

        var diff = editing.IsEdited && liveBaseline is { } baseline ? LiveReloadMerge.Between(editing, baseline) : null;
        if (diff is { OtherEdits: true } && !await Dialogs.Confirm(this, "Map changed in osu!",
                $"{Path.GetFileName(path)} was saved by another program. Your unsaved hits and volumes are kept when it reloads, " +
                "but your unsaved slide, sound-set, bookmark or HS-mode edits would be lost. Reload now?", "Reload now", "Keep editing"))
        {
            declinedMapBytes = now;
            flash = $"{Path.GetFileName(path)} changed in osu!. Save or undo your edits here, or reopen it to see osu!'s version.";
            UpdateStatus();
            return;
        }

        if (!await ReopenKeepingView(path) || session is not { } reloaded) return;
        var result = diff is null ? null : LiveReloadMerge.Apply(reloaded, diff);
        flash = $"Reloaded {Path.GetFileName(path)}: osu! saved it." + (result is not { Kept: > 0 } ? ""
            : $" Your {result.Kept} unsaved edits were kept" + (result.WithoutObject > 0 ? $"; {result.WithoutObject} hits lost their object in osu! and now have none." : "."));
        UpdateStatus();
    }

    /// <summary>
    /// Reopens the map from disk (after Save here, or a save elsewhere) keeping the view: lane layout, zoom, scroll and
    /// playhead. While it loads, <see cref="Loading"/> tells other commands to wait.
    /// </summary>
    private async Task<bool> ReopenKeepingView(string path, HsProject? hsOverride = null)
    {
        var view = model?.CaptureViewState();
        double left = Lanes.Axis.Left, zoom = Lanes.Axis.Zoom, scrollY = Lanes.ScrollY, position = audio?.Position ?? Lanes.Playhead;
        var reload = OpenAsync(path, hsOverride: hsOverride);
        Loading = reload;
        if (!await reload) return false;
        if (view is not null) model?.RestoreViewState(view);
        (Lanes.Axis.Zoom, Lanes.Axis.Left, Lanes.ScrollY) = (zoom, left, scrollY);
        Seek(position);
        SyncScrollBars();
        return true;
    }

    /// <summary>An empty write, the bytes already open, or a save the user chose to keep editing over.</summary>
    internal static bool SkipMapReload(ReadOnlySpan<byte> now, ReadOnlySpan<byte> opened, byte[]? declined) =>
        now.IsEmpty || now.SequenceEqual(opened) || (declined is { Length: > 0 } kept && now.SequenceEqual(kept));

    private void StopWatchingMap()
    {
        mapWatcher?.Dispose();
        mapWatcher = null;
        mapChangeTimer?.Stop();
    }
}

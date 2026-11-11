using Velopack;
using Velopack.Sources;

namespace OsuHitsound.App;

/// <summary>
/// Updates for an installed copy (the Setup.exe download). A quiet check runs at start-up; when it finds a new version,
/// the start screen's link becomes "Update to …" and an update button shows in the editor's top bar. Either one, or
/// File → Check for updates…, downloads the new version, then closes the window the normal way (so unsaved work is
/// asked about first) and installs and restarts once it has closed. Copies that weren't installed (a dev build, an
/// unzipped folder) say so instead. Releases come from the project's GitHub page.
/// </summary>
public partial class MainWindow
{
    private const string ReleasesUrl = "https://github.com/YogeGod/dawsu-hitsounding-editor";

    private bool updating;
    // A downloaded update to install when the window has closed; dropped again if the user cancels the close.
    private (UpdateManager Manager, VelopackAsset Release)? updateOnClose;

    private static UpdateManager Updates() => new(new GithubSource(ReleasesUrl, null, prerelease: false));

    private void SetupUpdates()
    {
        CheckUpdatesItem.Click += async (_, _) => await CheckForUpdates(quiet: false);
        UpdateButton.Click += async (_, _) => await CheckForUpdates(quiet: false);
        Start.UpdateRequested += () => _ = CheckForUpdates(quiet: false);
        Closed += (_, _) =>
        {
            // Velopack's updater waits for this process to exit, installs, and starts the new version.
            if (updateOnClose is not var (manager, release)) return;
            try { manager.WaitExitThenApplyUpdates(release, silent: false, restart: true); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { } // dawsu! still closes; the update stays offered next time
        };
        // The quiet check: at most once a day, a little after start-up so it doesn't compete with opening a map.
        if (usePreferences && !(DateTime.UtcNow - prefs.LastUpdateCheck < TimeSpan.FromDays(1)))
            Opened += async (_, _) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (!IsVisible) return; // closed meanwhile
                prefs = prefs with { LastUpdateCheck = DateTime.UtcNow };
                SavePrefs();
                await CheckForUpdates(quiet: true);
            };
    }

    /// <summary>Shows that a new version is ready to install: the start screen's link and the editor's top-bar button.</summary>
    private void ShowUpdateAvailable(string version)
    {
        Start.SetUpdate(version);
        UpdateButton.IsVisible = true;
        Avalonia.Controls.ToolTip.SetTip(UpdateButton, $"Update to dawsu! {version}: asks about unsaved work, then restarts");
    }

    private async Task CheckForUpdates(bool quiet)
    {
        if (updating) return;
        updating = true;
        try
        {
            var manager = Updates();
            if (!manager.IsInstalled)
            {
                if (!quiet) await Dialogs.Confirm(this, "Check for updates",
                    "This copy of dawsu! wasn't installed with the installer, so it can't update itself. Download the newest installer from the releases page.", "OK", "Close");
                return;
            }

            if (!quiet) flash = "Checking for updates…";
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                if (!quiet)
                {
                    flash = null;
                    await Dialogs.Confirm(this, "Check for updates", "dawsu! is up to date.", "OK", "Close");
                }
                return;
            }

            string version = update.TargetFullRelease.Version.ToString();
            ShowUpdateAvailable(version);
            if (quiet)
            {
                flash = $"dawsu! {version} is available: click Update to install it.";
                UpdateStatus();
                return;
            }

            if (!await Dialogs.Confirm(this, "Update dawsu!",
                $"dawsu! {version} is available. Download it and restart? dawsu! asks about unsaved work before it closes.", "Update and restart"))
            {
                flash = null;
                return;
            }

            await manager.DownloadUpdatesAsync(update, percent => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                flash = $"Downloading dawsu! {version}… {percent}%";
                UpdateStatus();
            }));
            flash = $"dawsu! {version} is downloaded. It installs when dawsu! closes.";
            UpdateStatus();
            updateOnClose = (manager, update.TargetFullRelease);
            Close(); // the normal close: unsaved work is asked about, and cancelling keeps dawsu! open without updating
        }
        // Offline, GitHub unreachable, a broken download: never stop the editor over an update check.
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!quiet)
            {
                flash = null;
                await Dialogs.Confirm(this, "Check for updates", $"Couldn't check for updates: {ex.Message}", "OK", "Close");
            }
        }
        finally { updating = false; }
    }
}

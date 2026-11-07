using Velopack;
using Velopack.Sources;

namespace OsuHitsound.App;

/// <summary>
/// Updates for an installed copy (the Setup.exe download): a quiet check at start-up, and File → Check for updates…,
/// which downloads the new version and restarts into it. Copies that weren't installed (a dev build, an unzipped
/// folder) say so instead. Releases come from the project's GitHub page.
/// </summary>
public partial class MainWindow
{
    private const string ReleasesUrl = "https://github.com/YogeGod/dawsu-hitsounding-editor";

    private static UpdateManager Updates() => new(new GithubSource(ReleasesUrl, null, prerelease: false));

    private void SetupUpdates()
    {
        CheckUpdatesItem.Click += async (_, _) => await CheckForUpdates(quiet: false);
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

    private async Task CheckForUpdates(bool quiet)
    {
        try
        {
            var manager = Updates();
            if (!manager.IsInstalled)
            {
                if (!quiet) await Dialogs.Confirm(this, "Check for updates",
                    "This copy of dawsu! wasn't installed with the installer, so it can't update itself. Download the newest installer from the releases page.", "OK", "Close");
                return;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                if (!quiet) await Dialogs.Confirm(this, "Check for updates", "dawsu! is up to date.", "OK", "Close");
                return;
            }

            string version = update.TargetFullRelease.Version.ToString();
            if (quiet)
            {
                flash = $"dawsu! {version} is available: File → Check for updates… installs it.";
                UpdateStatus();
                return;
            }

            if (!await Dialogs.Confirm(this, "Update dawsu!",
                $"dawsu! {version} is available. Download it and restart? Save your work first: the app closes to update.", "Update and restart")) return;
            await manager.DownloadUpdatesAsync(update);
            manager.ApplyUpdatesAndRestart(update);
        }
        // Offline, GitHub unreachable, a broken download: never stop the editor over an update check.
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!quiet) await Dialogs.Confirm(this, "Check for updates", $"Couldn't check for updates: {ex.Message}", "OK", "Close");
        }
    }
}

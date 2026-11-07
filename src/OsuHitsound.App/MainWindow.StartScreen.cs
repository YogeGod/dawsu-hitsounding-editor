using Avalonia.Input;

namespace OsuHitsound.App;

/// <summary>The start screen (<see cref="StartScreen"/>): shown when the app opens without a map, hidden once one opens.</summary>
public partial class MainWindow
{
    private void SetupStartScreen(bool show)
    {
        // Snapshots and tests (no preferences) keep the plain empty editor they expect.
        Start.IsVisible = show && usePreferences;
        if (Start.IsVisible) RefreshStartScreen();
        Start.OpenRequested += () => Run("open");
        Start.RecentRequested += async path => { if (IsKeysoundProject(path) || await MayLeaveMap()) await Open(path); };
        Start.SamplesRequested += ShowSampleTools;
        Start.PreferencesRequested += () => Run("preferences");
        Start.QuickStartRequested += StartQuickStart;
        Start.HelpRequested += () => Run("help");
        Activated += (_, _) => { if (Start.IsVisible) RefreshStartScreen(); }; // e.g. after Preferences changed the Songs folder
        HomeButton.Click += async (_, _) => await GoToStartScreen();
        StartScreenItem.Click += async (_, _) => await GoToStartScreen();
    }

    /// <summary>
    /// Back to the start screen from either editor: asks to save first, as closing does, then shows it. The map stays
    /// open behind it until another one is opened; a Keysound project is closed.
    /// </summary>
    private async Task GoToStartScreen()
    {
        if (!usePreferences || Start.IsVisible) return;
        if (Keys.Document is not null)
        {
            if (!await MayLeaveKeysound()) return;
            hsMapBeforeKeysound = null;
            CloseKeysoundView();
        }
        else if (session is { IsEdited: true } && doc is { } document)
        {
            // The same choice as closing the window: save, or reload the map as saved so nothing unsaved stays behind.
            var choice = await Dialogs.UnsavedClose(this, Path.GetFileName(mapPath) ?? "this difficulty");
            if (choice == CloseChoice.Cancel || !ReferenceEquals(doc, document)) return;
            if (choice == CloseChoice.Save && !await Save()) return;
            if (choice == CloseChoice.Discard)
            {
                var reload = OpenAsync(document.Path, restoreUnanchored: false);
                Loading = reload;
                if (!await reload) return;
                SaveProject(); // the discarded loose hits leave the autosaved editor project too
            }
        }
        audio?.Pause();
        Start.IsVisible = true;
        RefreshStartScreen();
    }

    private void RefreshStartScreen()
    {
        prefs = prefs.WithoutMissing(File.Exists);
        Start.SetRecent(prefs.RecentMaps);
        Start.SetSongsFolder(prefs.SongsFolder ?? Preferences.FindSongsFolder());
    }

    private void HideStartScreen() => Start.IsVisible = false;

    /// <summary>Leaves the start screen with the file browser and the Active lane card open.</summary>
    private void ShowSampleTools()
    {
        HideStartScreen();
        InspectorToggle.IsChecked = true;
        Inspector.Reveal(InspectorPanel.ChannelCard);
        BrowserPanelItem.IsChecked = true;
        ShowBrowser(true);
        if (prefs.ClassicBrowser) Inspector.Reveal("browser");
        Inspector.Browser.Focus();
    }

    /// <summary>Esc leaves the start screen for the empty editor.</summary>
    private bool StartScreenKey(KeyEventArgs e)
    {
        if (!Start.IsVisible || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return false;
        HideStartScreen();
        return true;
    }
}

using Avalonia.Input;

namespace OsuHitsound.App;

/// <summary>The start screen (<see cref="StartScreen"/>): shown when the app opens without a map, hidden once one opens.</summary>
public partial class MainWindow
{
    private void SetupStartScreen(bool show)
    {
        // Snapshots and tests (no preferences) keep the plain empty editor they expect.
        Start.IsVisible = show && usePreferences;
        if (!Start.IsVisible) return;
        RefreshStartScreen();
        Start.OpenRequested += () => Run("open");
        Start.RecentRequested += async path => { if (await MayLeaveMap()) await Open(path); };
        Start.SamplesRequested += ShowSampleTools;
        Start.PreferencesRequested += () => Run("preferences");
        Start.QuickStartRequested += StartQuickStart;
        Start.HelpRequested += () => Run("help");
        Activated += (_, _) => { if (Start.IsVisible) RefreshStartScreen(); }; // e.g. after Preferences changed the Songs folder
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

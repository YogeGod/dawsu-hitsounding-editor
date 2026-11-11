using Avalonia.Controls;

namespace OsuHitsound.App;

/// <summary>The screen over the empty editor until a map opens. It raises requests; <see cref="MainWindow"/> acts on them.</summary>
public partial class StartScreen : UserControl
{
    private IReadOnlyList<string> recent = [];

    public StartScreen()
    {
        InitializeComponent();
        var v = typeof(StartScreen).Assembly.GetName().Version;
        Version.Text = v is null ? "hitsound studio" : $"hitsound studio {v.Major}.{v.Minor}.{v.Build}";
        OpenPanel.Click += (_, _) => OpenRequested?.Invoke();
        SamplesPanel.Click += (_, _) => SamplesRequested?.Invoke();
        PreferencesLink.Click += (_, _) => PreferencesRequested?.Invoke();
        QuickStartLink.Click += (_, _) => QuickStartRequested?.Invoke();
        KeysLink.Click += (_, _) => HelpRequested?.Invoke();
        UpdateLink.Click += (_, _) => UpdateRequested?.Invoke();
        KeysoundNewItem.Click += (_, _) => KeysoundNewRequested?.Invoke();
        KeysoundOpenItem.Click += (_, _) => KeysoundOpenRequested?.Invoke();
    }

    public event Action? OpenRequested;
    public event Action<string>? RecentRequested;
    public event Action? SamplesRequested;
    public event Action? PreferencesRequested;
    public event Action? QuickStartRequested;
    public event Action? HelpRequested;
    public event Action? KeysoundNewRequested;
    public event Action? KeysoundOpenRequested;
    public event Action? UpdateRequested;

    /// <summary>The update link: "Check for updates", or "Update to …" (highlighted) once a new version is found.</summary>
    public void SetUpdate(string? version)
    {
        UpdateLink.Content = version is null ? "Check for updates" : $"Update to {version}";
        UpdateLink.Classes.Set("available", version is not null);
        ToolTip.SetTip(UpdateLink, version is null ? null : "Downloads it, asks about unsaved work, then restarts dawsu!");
    }

    /// <summary>A recent entry's name: the difficulty, or for a Keysound project its folder.</summary>
    public static string RecentLabel(string path) =>
        Path.GetExtension(path).Equals(OsuHitsound.Lanes.Keysound.KeysoundStore.Extension, StringComparison.OrdinalIgnoreCase)
            ? $"{Path.GetFileName(Path.GetDirectoryName(path))} (Keysound)"
            : Path.GetFileNameWithoutExtension(path);

    public void SetMessage(string? message)
    {
        Message.Text = message;
        MessagePanel.IsVisible = !string.IsNullOrEmpty(message);
    }

    /// <summary>Recent maps, newest first.</summary>
    public void SetRecent(IReadOnlyList<string> paths)
    {
        bool changed = !recent.SequenceEqual(paths);
        recent = paths.ToArray();
        bool any = paths.Count > 0;
        RecentPanel.IsEnabled = any;
        RecentCaption.Text = any ? "Last opened" : "Nothing opened yet";
        RecentName.IsVisible = any;
        RecentName.Text = any ? RecentLabel(paths[0]) : "";
        ToolTip.SetTip(RecentPanel, any ? "Choose a recent map (newest first)" : null);
        if (RecentPanel.Flyout is MenuFlyout menu && (changed || menu.Items.Count != paths.Count))
            menu.ItemsSource = paths.Select(path =>
            {
                var item = new MenuItem { Header = RecentLabel(path) };
                ToolTip.SetTip(item, path);
                item.Click += (_, _) => RecentRequested?.Invoke(path);
                return item;
            }).ToList();
    }

    /// <summary>Where imported .osz files go, or that it isn't set yet.</summary>
    public void SetSongsFolder(string? folder)
    {
        SongsFolder.Text = folder ?? "not set";
        ToolTip.SetTip(SongsFolder, folder ?? "Set your osu! Songs folder in Preferences before importing an .osz");
    }

}

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OsuHitsound.App;

/// <summary>The start screen shown over the empty editor until a map opens: open a map, reopen a recent one,
/// go to the sample tools, or (later) keysounding. It only raises requests; <see cref="MainWindow"/> acts on them.</summary>
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
        RecentPanel.Click += OnRecentClick;
    }

    public event Action? OpenRequested;
    public event Action<string>? RecentRequested;
    public event Action? SamplesRequested;
    public event Action? PreferencesRequested;
    public event Action? QuickStartRequested;
    public event Action? HelpRequested;

    /// <summary>The recent maps, newest first (only ones that still exist).</summary>
    public void SetRecent(IReadOnlyList<string> paths)
    {
        recent = paths;
        RecentPanel.IsEnabled = paths.Count > 0;
        RecentCaption.Text = paths.Count == 0 ? "Nothing opened yet" : "Last opened";
        RecentName.IsVisible = paths.Count > 0;
        RecentName.Text = paths.Count > 0 ? Path.GetFileNameWithoutExtension(paths[0]) : "";
        ToolTip.SetTip(RecentPanel, paths.Count > 0 ? "Choose a recent map (newest first)" : null);
    }

    /// <summary>Where imported .osz files go, or that it isn't set yet.</summary>
    public void SetSongsFolder(string? folder)
    {
        SongsFolder.Text = folder ?? "not set";
        ToolTip.SetTip(SongsFolder, folder ?? "Set your osu! Songs folder in Preferences before importing an .osz");
    }

    private void OnRecentClick(object? sender, RoutedEventArgs e)
    {
        if (RecentPanel.Flyout is not MenuFlyout menu) return;
        menu.ItemsSource = recent.Select(path =>
        {
            var item = new MenuItem { Header = Path.GetFileNameWithoutExtension(path) };
            ToolTip.SetTip(item, path);
            item.Click += (_, _) => RecentRequested?.Invoke(path);
            return item;
        }).ToList();
    }
}

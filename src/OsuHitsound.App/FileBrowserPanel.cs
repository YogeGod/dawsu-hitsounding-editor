using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using OsuHitsound.Format;

namespace OsuHitsound.App;

internal sealed class FileBrowserPanel : StackPanel
{
    private sealed record Entry(string Path, bool Folder)
    {
        public override string ToString() => (Folder ? "▸ " : "") + System.IO.Path.GetFileName(Path);
    }
    private readonly ComboBox mode = new() { ItemsSource = new[] { "Samples", "Maps and difficulties" }, SelectedIndex = 0, Foreground = Avalonia.Media.Brushes.Gainsboro };
    private readonly TextBox location = new() { Watermark = "Folder path", Foreground = Avalonia.Media.Brushes.Gainsboro };
    private readonly ListBox entries = new() { Height = 180 };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11 };
    private readonly Button use = new() { Content = "Use sample" }, preview = new() { Content = "Preview" };
    private string? samplesRoot, mapsRoot;
    private string? activeMapRoot, libraryRoot;
    private int scanNumber;
    public event Action<string>? OpenMapRequested;
    public event Action<string>? SampleRequested;
    public event Action<string>? PreviewRequested;

    public FileBrowserPanel()
    {
        Spacing = 6;
        var browse = new Button { Content = "Folder…" };
        var up = new Button { Content = "Up" };
        var go = new Button { Content = "Go" };
        var navigation = new WrapPanel { Orientation = Orientation.Horizontal };
        navigation.Children.AddRange([browse, up, go]);
        var mapFolder = new Button { Content = "Map folder" };
        var library = new Button { Content = "Hitsound folder" };
        navigation.Children.AddRange([mapFolder, library]);
        mapFolder.Click += (_, _) => SetFolder(activeMapRoot);
        library.Click += (_, _) =>
        {
            if (!Directory.Exists(libraryRoot)) { status.Text = "Choose an existing hitsound folder in Preferences."; return; }
            SetFolder(libraryRoot);
        };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.AddRange([use, preview]);
        Children.AddRange([mode, location, navigation, entries, actions, status]);
        mode.SelectionChanged += (_, _) =>
        {
            use.Content = mode.SelectedIndex == 0 ? "Use sample" : "Open map";
            preview.IsVisible = mode.SelectedIndex == 0;
            SetFolder(mode.SelectedIndex == 0 ? samplesRoot : mapsRoot);
        };
        browse.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new() { Title = "Browse files", AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } folder) SetFolder(folder);
        };
        up.Click += (_, _) => { try { SetFolder(Directory.GetParent(location.Text ?? "")?.FullName); } catch (Exception ex) when (ex is ArgumentException or IOException) { status.Text = ex.Message; } };
        go.Click += (_, _) => SetFolder(location.Text);
        location.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SetFolder(location.Text); e.Handled = true; } };
        use.Click += (_, _) => Activate();
        entries.DoubleTapped += (_, _) => Activate();
        entries.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Activate(); e.Handled = true; } };
        preview.Click += (_, _) => { if (entries.SelectedItem is Entry { Folder: false } file) PreviewRequested?.Invoke(file.Path); };
    }

    public void SetRoots(string? samples, string? maps)
    {
        (samplesRoot, mapsRoot) = (samples, maps);
        SetFolder(mode.SelectedIndex == 0 ? samples : maps);
    }

    public void SetShortcuts(string? map, string? library) => (activeMapRoot, libraryRoot) = (map, library);

    private void Activate()
    {
        if (entries.SelectedItem is not Entry entry) return;
        if (entry.Folder) SetFolder(entry.Path);
        else if (mode.SelectedIndex == 0) SampleRequested?.Invoke(entry.Path);
        else OpenMapRequested?.Invoke(entry.Path);
    }

    private async void SetFolder(string? path)
    {
        int number = ++scanNumber;
        if (string.IsNullOrWhiteSpace(path)) { entries.ItemsSource = null; location.Text = ""; return; }
        bool samples = mode.SelectedIndex == 0;
        try
        {
            string full = Path.GetFullPath(path);
            status.Text = "Loading…";
            var found = await Task.Run(() => Directory.EnumerateFileSystemEntries(full).Select(p => new Entry(p, Directory.Exists(p)))
                .Where(e => e.Folder || (samples ? HitsoundRules.SampleExtensions.Contains(Path.GetExtension(e.Path).ToLowerInvariant()) : Path.GetExtension(e.Path).ToLowerInvariant() is ".osu" or ".osz"))
                .OrderByDescending(e => e.Folder).ThenBy(e => Path.GetFileName(e.Path), StringComparer.OrdinalIgnoreCase).ToList());
            if (number != scanNumber) return;
            location.Text = full;
            entries.ItemsSource = found;
            status.Text = $"{found.Count(e => !e.Folder)} files. Double-click a folder to browse.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (number != scanNumber) return;
            entries.ItemsSource = null; status.Text = $"Could not browse: {ex.Message}";
        }
    }
}

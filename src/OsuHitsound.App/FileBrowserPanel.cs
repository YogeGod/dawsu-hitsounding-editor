using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OsuHitsound.Format;

namespace OsuHitsound.App;

internal sealed class FileBrowserPanel : DockPanel
{
    public const string SampleDragFormat = "dawsu/sample-path";
    private sealed class Entry(string path, bool folder, int depth)
    {
        public string Path { get; } = path;
        public bool Folder { get; } = folder;
        public int Depth { get; } = depth;
        public bool Expanded { get; set; }
        public List<Entry>? Children { get; set; }
        public string Name => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : Path;
        public override string ToString() => Name;
    }
    private readonly ComboBox mode = new() { ItemsSource = new[] { "Samples", "Maps and difficulties" }, SelectedIndex = 0 };
    private readonly TextBox location = new() { Watermark = "Folder path" };
    private readonly ListBox entries = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly TextBlock assignment = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly Button use = new() { Content = "Add to selected lane", HorizontalAlignment = HorizontalAlignment.Stretch };
    private string? samplesRoot, mapsRoot, activeMapRoot, libraryRoot, assignmentTarget;
    private bool canAssign, refreshing;
    private int scanNumber;
    private Entry? root;
    private Point? dragStart;
    public event Action<string>? OpenMapRequested;
    public event Action<string>? SampleRequested;
    public event Action<string>? PreviewRequested;
    public event Action? CloseRequested;

    public FileBrowserPanel()
    {
        var close = new Button { Content = "›" };
        close.Click += (_, _) => CloseRequested?.Invoke();
        ToolTip.SetTip(close, "Hide file browser");
        var title = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        SetDock(close, Dock.Right);
        title.Children.AddRange([close, new TextBlock { Text = "File browser", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }]);
        var browse = new Button { Content = "Folder…" };
        var go = new Button { Content = "Go" };
        var mapFolder = new Button { Content = "Map folder" };
        var library = new Button { Content = "Library" };
        var navigation = new WrapPanel { Orientation = Orientation.Horizontal };
        navigation.Children.AddRange([browse, go, mapFolder, library]);
        var top = new StackPanel { Spacing = 6, Children = { title, mode, location, navigation } };
        var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0), Children = { assignment, use, status } };
        SetDock(top, Dock.Top); SetDock(bottom, Dock.Bottom);
        Children.AddRange([top, bottom, entries]);
        entries.ItemTemplate = new FuncDataTemplate<Entry>((entry, _) =>
        {
            if (entry is null) return null;
            var row = new DockPanel { Margin = new Thickness(entry.Depth * 14, 0, 0, 0), MinHeight = 26 };
            var toggle = new Button { Content = entry.Expanded ? "−" : "+", Width = 22, MinWidth = 22, Padding = new Thickness(0), IsVisible = entry.Folder };
            Avalonia.Automation.AutomationProperties.SetName(toggle, $"{(entry.Expanded ? "Collapse" : "Expand")} {entry.Path}");
            toggle.Click += async (_, e) => { e.Handled = true; await Toggle(entry); };
            SetDock(toggle, Dock.Left);
            row.Children.Add(toggle);
            row.Children.Add(new TextBlock { Text = entry.Name,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(entry.Folder ? 4 : 26, 0, 0, 0) });
            ToolTip.SetTip(row, entry.Path);
            return row;
        });
        mode.SelectionChanged += (_, _) => SetFolder(mode.SelectedIndex == 0 ? samplesRoot : mapsRoot);
        browse.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new() { Title = "Browse files", AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } folder) SetFolder(folder);
        };
        go.Click += (_, _) => SetFolder(location.Text);
        location.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SetFolder(location.Text); e.Handled = true; } };
        mapFolder.Click += (_, _) => SetFolder(activeMapRoot);
        library.Click += (_, _) => SetFolder(libraryRoot);
        entries.SelectionChanged += (_, _) =>
        {
            RefreshActions();
            if (!refreshing && mode.SelectedIndex == 0 && entries.SelectedItem is Entry { Folder: false } file) PreviewRequested?.Invoke(file.Path);
        };
        entries.DoubleTapped += async (_, _) => await Activate();
        entries.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Activate(); } };
        use.Click += (_, _) =>
        {
            if (entries.SelectedItem is not Entry { Folder: false } file) return;
            if (mode.SelectedIndex == 0 && canAssign) SampleRequested?.Invoke(file.Path);
            else if (mode.SelectedIndex != 0) OpenMapRequested?.Invoke(file.Path);
        };
        entries.AddHandler(PointerPressedEvent, (_, e) => dragStart = e.GetCurrentPoint(entries).Properties.IsLeftButtonPressed ? e.GetPosition(entries) : null, RoutingStrategies.Tunnel);
        entries.PointerReleased += (_, _) => dragStart = null;
        entries.PointerMoved += async (_, e) =>
        {
            if (dragStart is not { } start || mode.SelectedIndex != 0 || entries.SelectedItem is not Entry { Folder: false } file) return;
            if (!e.GetCurrentPoint(entries).Properties.IsLeftButtonPressed) { dragStart = null; return; }
            var p = e.GetPosition(entries);
            if (Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) < 8) return;
            dragStart = null;
#pragma warning disable CS0618
            var data = new DataObject(); data.Set(SampleDragFormat, file.Path);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
#pragma warning restore CS0618
        };
        RefreshActions();
    }

    public void SetAssignmentTarget(string? name, bool editable) { assignmentTarget = name; canAssign = editable; RefreshActions(); }
    public void SetRoots(string? samples, string? maps) { (samplesRoot, mapsRoot) = (samples, maps); SetFolder(mode.SelectedIndex == 0 ? samples : maps); }
    public void SetShortcuts(string? map, string? library) => (activeMapRoot, libraryRoot) = (map, library);

    private void RefreshActions()
    {
        bool samples = mode.SelectedIndex == 0;
        bool file = entries.SelectedItem is Entry { Folder: false };
        assignment.Text = samples ? assignmentTarget is null ? "Select a lane to replace its sample."
            : !canAssign ? $"{assignmentTarget} is read-only."
            : $"Replaces the sample for every hit in {assignmentTarget}." : "Open a difficulty to edit it.";
        use.Content = samples ? "Add to selected lane" : "Open map";
        use.IsVisible = file;
        use.IsEnabled = file && (!samples || canAssign);
        ToolTip.SetTip(use, assignment.Text);
    }

    private async Task Activate()
    {
        if (entries.SelectedItem is not Entry entry) return;
        if (entry.Folder) await Toggle(entry);
        else if (mode.SelectedIndex == 0) PreviewRequested?.Invoke(entry.Path);
        else OpenMapRequested?.Invoke(entry.Path);
    }

    private static List<Entry> ReadChildren(Entry node, bool samples) => Directory.EnumerateFileSystemEntries(node.Path)
        .Select(p => new Entry(p, Directory.Exists(p), node.Depth + 1))
        .Where(e => e.Folder || (samples ? HitsoundRules.SampleExtensions.Contains(Path.GetExtension(e.Path).ToLowerInvariant()) : Path.GetExtension(e.Path).ToLowerInvariant() is ".osu" or ".osz"))
        .OrderByDescending(e => e.Folder).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();

    private void RefreshTree()
    {
        var selected = entries.SelectedItem;
        var visible = new List<Entry>();
        void Add(Entry node) { visible.Add(node); if (node.Expanded && node.Children is { } children) foreach (var child in children) Add(child); }
        if (root is not null) Add(root);
        refreshing = true;
        try { entries.ItemsSource = visible; entries.SelectedItem = selected is Entry entry && visible.Contains(entry) ? selected : null; }
        finally { refreshing = false; }
        RefreshActions();
    }

    private async Task Toggle(Entry node)
    {
        int number = scanNumber;
        try
        {
            if (node.Children is null)
            {
                bool samples = mode.SelectedIndex == 0;
                var children = await Task.Run(() => ReadChildren(node, samples));
                if (number != scanNumber) return;
                node.Children = children;
            }
            node.Expanded = !node.Expanded;
            RefreshTree();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { if (number == scanNumber) status.Text = $"Could not browse: {ex.Message}"; }
    }

    private async void SetFolder(string? path)
    {
        int number = ++scanNumber;
        if (string.IsNullOrWhiteSpace(path)) { root = null; RefreshTree(); location.Text = ""; return; }
        bool samples = mode.SelectedIndex == 0;
        try
        {
            string full = Path.GetFullPath(path);
            status.Text = "Loading…";
            var found = await Task.Run(() =>
            {
                var chain = new Stack<string>();
                for (var dir = new DirectoryInfo(full); dir is not null; dir = dir.Parent) chain.Push(dir.FullName);
                var first = new Entry(chain.Pop(), true, 0);
                var current = first;
                while (true)
                {
                    current.Children = ReadChildren(current, samples); current.Expanded = true;
                    if (chain.Count == 0) break;
                    string next = chain.Pop();
                    current = current.Children.First(e => e.Folder && string.Equals(e.Path, next, StringComparison.OrdinalIgnoreCase));
                }
                return (Root: first, Target: current);
            });
            if (number != scanNumber) return;
            location.Text = full; root = found.Root; RefreshTree();
            Dispatcher.UIThread.Post(() =>
            {
                if (number != scanNumber) return;
                entries.ScrollIntoView(found.Target);
                Dispatcher.UIThread.Post(() =>
                {
                    if (number != scanNumber || entries.Scroll is not { } scroll) return;
                    int index = entries.Items.IndexOf(found.Target);
                    if (entries.ContainerFromIndex(index) is { } container && container.TranslatePoint(default, entries) is { } point)
                        scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, scroll.Offset.Y + point.Y - 4));
                }, DispatcherPriority.Loaded);
            }, DispatcherPriority.Loaded);
            status.Text = samples ? "Select to preview · drag onto a lane to replace" : "Double-click a map to open";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            if (number != scanNumber) return;
            root = null; RefreshTree(); status.Text = $"Could not browse: {ex.Message}";
        }
    }
}

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
    private string? assignmentUnavailable;
    private int scanNumber;
    private Entry? root;
    private Point? dragStart;
    private bool classic;
    private readonly Button up = new() { Content = "Up", IsVisible = false };
    private readonly DockPanel title = new() { Margin = new Thickness(0, 0, 0, 8) };
    public event Action<string>? OpenMapRequested;
    public event Action<string>? SampleRequested;
    public event Action<string>? PreviewRequested;
    public event Action? CloseRequested;
    /// <summary>The Sound sets shortcut: the window shows its Sound sets panel.</summary>
    public event Action? SoundSetsRequested;

    public FileBrowserPanel()
    {
        var close = new Button { Content = "‹", Width = 24, Height = 24, MinHeight = 24, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center }; // the same small button as the inspector
        close.Click += (_, _) => CloseRequested?.Invoke();
        ToolTip.SetTip(close, "Hide file browser");
        SetDock(close, Dock.Right);
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.AddRange([new AppIcon { Glyph = "OsuFiles", Size = 15 }, new TextBlock { Text = "File browser", FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }]);
        title.Children.AddRange([close, heading]);
        var browse = new Button { Content = "Folder…" };
        var go = new Button { Content = "Go" };
        var mapFolder = new Button { Content = "Map folder" };
        var library = new Button { Content = "Library" };
        var soundSets = new Button { Content = "Sound sets" };
        ToolTip.SetTip(soundSets, "Show the Sound sets panel in the inspector");
        soundSets.Click += (_, _) => SoundSetsRequested?.Invoke();
        // UI refresh (2026-11-13): no wrapping row of buttons. The two shortcuts share a row; the path has Go and Folder… beside it.
        var shortcuts = new Avalonia.Controls.Primitives.UniformGrid { Rows = 1 };
        foreach (var shortcut in new[] { mapFolder, library, soundSets, up })
        {
            shortcut.HorizontalAlignment = HorizontalAlignment.Stretch;
            shortcut.HorizontalContentAlignment = HorizontalAlignment.Center;
            shortcut.Margin = new Thickness(shortcut == mapFolder ? 0 : 4, 0, 0, 0); // gaps between, none trailing: the row ends where the path row does
            shortcut.Padding = new Thickness(0, 4);
            shortcut.FontSize = 11;
        }
        shortcuts.Children.AddRange([mapFolder, library, soundSets, up]);
        var pathRow = new DockPanel();
        foreach (var beside in new[] { browse, go }) { beside.Margin = new Thickness(4, 0, 0, 0); beside.Padding = new Thickness(8, 4); SetDock(beside, Dock.Right); }
        pathRow.Children.AddRange([browse, go, location]);
        var top = new StackPanel { Spacing = 6, Children = { title, mode, shortcuts, pathRow } };
        SegmentedChoice.Attach(mode); // "Samples | Maps and difficulties" as two buttons
        var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0), Children = { assignment, use, status } };
        SetDock(top, Dock.Top); SetDock(bottom, Dock.Bottom);
        Children.AddRange([top, bottom, entries]);
        entries.ItemTemplate = RowTemplate();
        mode.SelectionChanged += (_, _) => SetFolder(mode.SelectedIndex == 0 ? samplesRoot : mapsRoot);
        browse.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new() { Title = "Browse files", AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } folder) SetFolder(folder);
        };
        go.Click += (_, _) => SetFolder(location.Text);
        up.Click += (_, _) =>
        {
            if (root is not null && Directory.GetParent(root.Path) is { } parent) SetFolder(parent.FullName);
        };
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

    public void SetClassic(bool value)
    {
        if (classic == value) return;
        classic = value;
        title.IsVisible = !classic;
        up.IsVisible = classic;
        entries.Height = classic ? 180 : double.NaN;
        entries.ItemTemplate = RowTemplate();
        SetFolder(location.Text);
    }

    private FuncDataTemplate<Entry> RowTemplate() => new((entry, _) =>
    {
        if (entry is null) return null;
        if (classic) return new TextBlock { Text = (entry.Folder ? "▸ " : "") + entry.Name, TextTrimming = TextTrimming.CharacterEllipsis };
        // Flat rows (the design's list): a fold arrow before a folder, a play mark before a file. Deep folders stop
        // indenting after six levels so the names stay readable in a narrow pane.
        var row = new DockPanel { Margin = new Thickness(Math.Min(entry.Depth, 6) * 10, 0, 0, 0), MinHeight = 28 };
        var toggle = new Button { Content = entry.Expanded ? "▾" : "▸", Width = 22, MinWidth = 22, Padding = new Thickness(0), IsVisible = entry.Folder,
            Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
        toggle.Classes.Add("flat");
        if (!entry.Folder)
        {
            var mark = new AppIcon { Glyph = "OsuPlay", Size = 13, Opacity = 0.6, Width = 22, VerticalAlignment = VerticalAlignment.Center };
            SetDock(mark, Dock.Left);
            row.Children.Add(mark);
        }
        Avalonia.Automation.AutomationProperties.SetName(toggle, $"{(entry.Expanded ? "Collapse" : "Expand")} {entry.Path}");
        toggle.Click += async (_, e) => { e.Handled = true; await Toggle(entry); };
        SetDock(toggle, Dock.Left);
        row.Children.Add(toggle);
        row.Children.Add(new TextBlock { Text = entry.Name, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0, 0, 0) });
        ToolTip.SetTip(row, entry.Path);
        return row;
    });

    public void SetAssignmentTarget(string? name, bool editable, string? unavailableReason = null)
    { assignmentTarget = name; canAssign = editable; assignmentUnavailable = unavailableReason; RefreshActions(); }
    public void SetRoots(string? samples, string? maps) { (samplesRoot, mapsRoot) = (samples, maps); SetFolder(mode.SelectedIndex == 0 ? samples : maps); }
    public void SetShortcuts(string? map, string? library) => (activeMapRoot, libraryRoot) = (map, library);

    private void RefreshActions()
    {
        bool samples = mode.SelectedIndex == 0;
        bool file = entries.SelectedItem is Entry { Folder: false };
        assignment.Text = samples ? assignmentUnavailable ?? (assignmentTarget is null ? "Select a lane to replace its sample."
            : !canAssign ? $"{assignmentTarget} is read-only."
            : $"Replaces the sample for every hit in {assignmentTarget}.") : "Open a difficulty to edit it.";
        use.Content = samples ? "Add to selected lane" : "Open map";
        use.IsVisible = file;
        use.IsEnabled = file && (!samples || canAssign);
        ToolTip.SetTip(use, assignment.Text);
    }

    private async Task Activate()
    {
        if (entries.SelectedItem is not Entry entry) return;
        if (entry.Folder) { if (classic) SetFolder(entry.Path); else await Toggle(entry); }
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
        if (root is not null) { if (classic) visible.AddRange(root.Children ?? []); else Add(root); }
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
        bool flat = classic;
        try
        {
            string full = Path.GetFullPath(path);
            status.Text = "Loading…";
            var found = await Task.Run(() =>
            {
                if (flat)
                {
                    var folder = new Entry(full, true, -1);
                    folder.Children = ReadChildren(folder, samples);
                    return (Root: folder, Target: folder);
                }
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
            if (!flat) Dispatcher.UIThread.Post(() =>
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

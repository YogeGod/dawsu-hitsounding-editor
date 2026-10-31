using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>Small modal dialogs: confirm, a text prompt, and picking one item from a list.</summary>
internal static class Dialogs
{
    public static async Task<bool> Confirm(Window owner, string title, string text, string ok = "OK") =>
        await Show<bool>(owner, title, (body, close) =>
        {
            body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
            return (() => close(true), ok);
        });

    public static async Task<string?> Prompt(Window owner, string title, string label, string initial) =>
        await Show<string?>(owner, title, (body, close) =>
        {
            var box = new TextBox { Text = initial, Width = 360 };
            body.Children.Add(new TextBlock { Text = label });
            body.Children.Add(box);
            box.AttachedToVisualTree += (_, _) => { box.Focus(); box.SelectAll(); };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) close(box.Text ?? ""); };
            return (() => close(box.Text ?? ""), "OK");
        });

    public static async Task<string?> Choose(Window owner, string title, string label, IReadOnlyList<string> items, string? initial = null) =>
        await Show<string?>(owner, title, (body, close) =>
        {
            var list = new ListBox { ItemsSource = items, Height = 260, Width = 400, SelectedItem = initial };
            list.AttachedToVisualTree += (_, _) => { if (list.SelectedItem is { } s) list.ScrollIntoView(s); };
            list.KeyDown += (_, e) => { if (e.Key == Key.Enter && list.SelectedItem is string s) close(s); };
            list.DoubleTapped += (_, _) => { if (list.SelectedItem is string s) close(s); };
            body.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
            body.Children.Add(list);
            return (() => { if (list.SelectedItem is string s) close(s); }, "Add");
        });

    /// <summary>
    /// The time signature dialog: one change per line as "bar n/d" (e.g. "33 7/8"). Returns the changes, or null on Cancel.
    /// </summary>
    public static async Task<List<TimeSignature>?> TimeSignatures(Window owner, IReadOnlyList<TimeSignature> current) =>
        await Show<List<TimeSignature>>(owner, "Time signatures", (body, close) =>
        {
            var box = new TextBox
            {
                Text = string.Join("\n", current.Select(t => $"{t.Bar} {t.Numerator}/{t.Denominator}")),
                AcceptsReturn = true, Width = 300, Height = 180, Watermark = "33 7/8",
            };
            var problem = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
            body.Children.AddRange([
                new TextBlock { Text = "One change per line: bar, then beats/beat unit (e.g. 33 7/8).", TextWrapping = TextWrapping.Wrap, MaxWidth = 300 },
                box,
                new TextBlock { Text = "Only changes how bars are drawn in the editor. The map's timing is never touched.", TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Opacity = 0.7 },
                problem,
            ]);
            return (() =>
            {
                var changes = new List<TimeSignature>();
                foreach (var line in (box.Text ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split([' ', '/'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3 && int.TryParse(parts[0], out int bar) && int.TryParse(parts[1], out int n) && int.TryParse(parts[2], out int den)
                        && ArrangeModel.IsValid(new TimeSignature(bar, n, den)))
                        changes.Add(new TimeSignature(bar, n, den));
                    else
                    {
                        problem.Text = $"\"{line}\" isn't a change: bar 1 or later, 1–32 beats, and a beat unit of 1, 2, 4, 8 or 16.";
                        return;
                    }
                }

                close(changes);
            }, "OK");
        });

    /// <summary>The Repeat dialog: how many copies, and the gap between them in snap steps (1/<paramref name="divisor"/>).</summary>
    public static async Task<(int Copies, int Gap)?> Repeat(Window owner, int gap, int divisor) =>
        await Show<(int, int)?>(owner, "Repeat", (body, close) =>
        {
            var copies = new TextBox { Text = "1", Width = 80 };
            var steps = new TextBox { Text = gap.ToString(CultureInfo.InvariantCulture), Width = 80 };
            var problem = new TextBlock { Foreground = Brushes.OrangeRed };
            body.Children.AddRange([
                new TextBlock { Text = "Copies (1–64)" }, copies,
                new TextBlock { Text = $"Gap in snap steps of 1/{divisor} (from one copy's start to the next)" }, steps,
                new TextBlock { Text = "The gap is counted on the grid, so it follows tempo changes.", TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Opacity = 0.7 },
                problem,
            ]);
            copies.AttachedToVisualTree += (_, _) => { copies.Focus(); copies.SelectAll(); };
            return (() =>
            {
                if (OsuNumber.TryParseInt(copies.Text ?? "", out int n) && n is >= 1 and <= HitEditor.MaxRepeatCopies
                    && OsuNumber.TryParseInt(steps.Text ?? "", out int g) && g is >= 1 and <= HitEditor.MaxRepeatGap)
                    close((n, g));
                else problem.Text = $"Copies must be 1–{HitEditor.MaxRepeatCopies} and the gap 1–{HitEditor.MaxRepeatGap} steps.";
            }, "Repeat");
        });

    /// <summary>The Range tool's Edit range dialog: what to do and the offset (ms, defaulting to the range's length).</summary>
    public static async Task<(RangeOperation Operation, double Offset)?> EditRange(Window owner, double length) =>
        await Show<(RangeOperation, double)?>(owner, "Edit range", (body, close) =>
        {
            var operation = new ComboBox { ItemsSource = Enum.GetValues<RangeOperation>(), SelectedIndex = 0, Width = 200 };
            var offset = new TextBox { Text = length.ToString("0.###", CultureInfo.InvariantCulture), Width = 200 };
            var problem = new TextBlock { Foreground = Brushes.OrangeRed };
            operation.SelectionChanged += (_, _) => offset.IsEnabled = operation.SelectedItem is not RangeOperation.Delete;
            body.Children.AddRange([
                new TextBlock { Text = "Operation" }, operation,
                new TextBlock { Text = "Offset (ms)" }, offset,
                new TextBlock { Text = "The range end is excluded. Gameplay objects are never moved.", TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Opacity = 0.7 },
                problem,
            ]);
            return (() =>
            {
                var op = operation.SelectedItem as RangeOperation? ?? RangeOperation.Copy;
                if (op == RangeOperation.Delete) close((op, 0));
                else if (OsuNumber.TryParseDouble(offset.Text ?? "", out double ms) && double.IsFinite(ms)) close((op, ms));
                else problem.Text = "The offset must be a number of milliseconds.";
            }, "OK");
        });

    /// <summary>
    /// The Scale by volume dialog. <paramref name="count"/> says how many circles and slider ends a choice changes, for
    /// the live preview. Returns the choice, or null on Cancel.
    /// </summary>
    public static async Task<(VolumeScope Scope, int From, int To, double Percent)?> ScaleByVolume(Window owner, bool hasSelection, Func<VolumeScope, int, int, int> count) =>
        await Show<(VolumeScope, int, int, double)?>(owner, "Scale by volume", (body, close) =>
        {
            (VolumeScope Scope, string Label)[] scopes = [(VolumeScope.Selected, "Selected hits"), (VolumeScope.SelectedLanes, "Lanes of the selected hits"), (VolumeScope.All, "All hits")];
            var scope = new ComboBox
            {
                ItemsSource = scopes.Select(x => x.Label).ToList(),
                SelectedIndex = Array.FindIndex(scopes, x => x.Scope == (hasSelection ? VolumeScope.Selected : VolumeScope.All)),
                Width = 240,
            };
            var from = new TextBox { Text = "1", Width = 70 };
            var to = new TextBox { Text = "100", Width = 70 };
            var percent = new TextBox { Text = "100", Width = 70 };
            var preview = new TextBlock { Opacity = 0.8 };

            // The choice as typed, with the volume bounds in order; false while a box isn't a valid number.
            bool Read(out VolumeScope s, out int low, out int high, out double p)
            {
                s = scopes[Math.Max(0, scope.SelectedIndex)].Scope;
                p = 0;
                bool ok = OsuNumber.TryParseInt(from.Text ?? "", out int a) & OsuNumber.TryParseInt(to.Text ?? "", out int b)
                          && OsuNumber.TryParseDouble(percent.Text ?? "", out p) && double.IsFinite(p) && p >= 0;
                (low, high) = (Math.Min(a, b), Math.Max(a, b));
                return ok && low >= 0 && high <= 100;
            }

            void Update() => preview.Text = Read(out var s, out int a, out int b, out _)
                ? count(s, a, b) switch { 1 => "Changes 1 circle or slider end.", var n => $"Changes {n} circles and slider ends." }
                : "Volumes are 0–100 %, and the scale is a percentage of at least 0. Scaled volumes stay 1–100 %.";

            foreach (var box in new[] { from, to, percent }) box.TextChanged += (_, _) => Update();
            scope.SelectionChanged += (_, _) => Update();
            var range = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            range.Children.AddRange([from, new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center }, to, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center }]);
            body.Children.AddRange([
                new TextBlock { Text = "Look at" }, scope,
                new TextBlock { Text = "Circles and slider ends with a volume from" }, range,
                new TextBlock { Text = "Scale by (%; 50 halves, 150 is half as loud again)" }, percent,
                new TextBlock { Text = "A circle has one volume, so every lane on it changes, also lanes outside this choice.", TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Opacity = 0.7 },
                preview,
            ]);
            Update();
            return (() =>
            {
                if (Read(out var s, out int a, out int b, out double p)) close((s, a, b, p));
            }, "Scale");
        });

    /// <summary>The preferences form; the edited copy on OK, null on Cancel.</summary>
    public static async Task<Preferences?> EditPreferences(Window owner, Preferences prefs, IReadOnlyList<(int Device, string Name)> devices) =>
        await Show<Preferences>(owner, "Preferences", (body, close) =>
        {
            var general = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            TextBox PathRow(StackPanel page, string label, string? value, string note, string? filePattern)
            {
                var box = new TextBox { Text = value ?? "", Width = 380, Watermark = note };
                var browse = new Button { Content = "Browse…" };
                browse.Click += async (_, _) =>
                {
                    var storage = TopLevel.GetTopLevel(body)!.StorageProvider;
                    var picked = filePattern is null
                        ? (await storage.OpenFolderPickerAsync(new() { Title = label })).FirstOrDefault()?.TryGetLocalPath()
                        : (await storage.OpenFilePickerAsync(new() { Title = label, FileTypeFilter = [new(filePattern) { Patterns = [filePattern] }] }))
                            .FirstOrDefault()?.TryGetLocalPath();
                    if (picked is not null) box.Text = picked;
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                row.Children.AddRange([box, browse]);
                page.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold });
                page.Children.Add(row);
                return box;
            }

            var songs = PathRow(general, "osu! Songs folder (Open starts here)", prefs.SongsFolder, "Not found yet", filePattern: null);
            var lazer = PathRow(general, "Default sounds: lazer's osu.Game.Resources.dll", prefs.LazerResources, "Blank: the usual lazer install", "osu.Game.Resources.dll");

            general.Children.Add(new TextBlock { Text = "Audio output", FontWeight = FontWeight.SemiBold });
            var device = new ComboBox { ItemsSource = devices.Select(d => d.Name).ToList(), Width = 380 };
            device.SelectedIndex = Math.Max(0, devices.ToList().FindIndex(d => d.Device == prefs.AudioDevice));
            general.Children.Add(device);

            var bufferText = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
            var buffer = new Slider { Minimum = 20, Maximum = 500, Value = prefs.BufferMs, Width = 380, TickFrequency = 10, IsSnapToTickEnabled = true };
            void ShowBuffer() => bufferText.Text = $"Buffer: {buffer.Value:0} ms. Hitsounds lag the music by about this much, so keep it small; raise it only if playback crackles.";
            buffer.ValueChanged += (_, _) => ShowBuffer();
            ShowBuffer();
            general.Children.Add(bufferText);
            general.Children.Add(buffer);

            var keys = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            var editedKeys = KeysPage(keys, prefs, body, PathRow);

            body.Children.Add(new TabControl
            {
                ItemsSource = new[] { new TabItem { Header = "General", Content = general }, new TabItem { Header = "Keys", Content = keys } },
            });

            return (() => close(editedKeys() with
            {
                SongsFolder = Blank(songs.Text),
                LazerResources = Blank(lazer.Text),
                AudioDevice = devices[Math.Max(0, device.SelectedIndex)].Device,
                BufferMs = (int)buffer.Value,
            }), "OK");
        });

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// The Keys tab, after Cubase's Key Commands window: the preset and Cubase file on top, the commands in folders with
    /// their keys on the left, and on the right the picked command's keys, where a pressed key is shown with its current
    /// owner before Assign adds it. Returns a function giving the preferences with the key settings as edited.
    /// </summary>
    private static Func<Preferences> KeysPage(StackPanel page, Preferences prefs, Control body,
        Func<StackPanel, string, string?, string, string?, TextBox> pathRow)
    {
        var edited = prefs;
        var presets = new[] { (Preset: KeyPreset.Cubase, Name: "Cubase (your Key Commands file)"), (Preset: KeyPreset.Osu, Name: "osu! editor (built-in keys with osu!'s tool and play keys)") };
        page.Children.Add(new TextBlock { Text = "Key preset", FontWeight = FontWeight.SemiBold });
        var preset = new ComboBox { ItemsSource = presets.Select(p => p.Name).ToList(), Width = 380 };
        preset.SelectedIndex = Array.FindIndex(presets, p => p.Preset == prefs.KeyPreset);
        page.Children.Add(preset);

        var cubaseRows = new StackPanel { Spacing = 8 };
        var file = pathRow(cubaseRows, "Cubase Key Commands file", prefs.CubaseKeyFile ?? CubaseKeyFile.FindNewest(),
            "Not found: it's in %AppData%\\Steinberg\\Cubase …", "Key Commands.xml");
        var reload = new Button { Content = "Reload" };
        ((StackPanel)cubaseRows.Children[^1]).Children.Add(reload);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 700, Opacity = 0.8 };
        page.Children.AddRange([cubaseRows, status]);

        // Left: search and the command tree. Right: the picked command.
        var search = new TextBox { Watermark = "Search commands or keys…", Width = 420 };
        var tree = new TreeView { Height = 360, Width = 420 };
        var left = new StackPanel { Spacing = 6 };
        left.Children.AddRange([search, tree]);

        var name = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 260, Text = "Pick a command" };
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 260, Opacity = 0.8 };
        var keyList = new ListBox { Height = 90, Width = 260 };
        var remove = new Button { Content = "Remove" };
        var clear = new Button { Content = "Clear command" };
        var resetCommand = new Button { Content = "Reset command" };
        var keyButtons = new WrapPanel { MaxWidth = 260 };
        foreach (var b in new[] { remove, clear, resetCommand }) { b.Margin = new Thickness(0, 0, 6, 6); keyButtons.Children.Add(b); }
        var capture = new TextBox { IsReadOnly = true, Width = 260, Watermark = "Click here, then press a key" };
        var assignedTo = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
        var assign = new Button { Content = "Assign", IsEnabled = false };
        var right = new StackPanel { Spacing = 6, Width = 260 };
        right.Children.AddRange([name, description, new TextBlock { Text = "Keys", FontWeight = FontWeight.SemiBold }, keyList, keyButtons,
            new TextBlock { Text = "Type in key", FontWeight = FontWeight.SemiBold }, capture, assignedTo, assign]);

        var columns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        columns.Children.AddRange([left, right]);
        var resetPreset = new Button { Content = "Reset preset…" };
        page.Children.AddRange([columns, resetPreset]);

        Dictionary<string, List<string>> Overrides() => edited.KeyOverrides.GetValueOrDefault(edited.KeyPreset.ToString()) ?? [];
        Dictionary<string, IReadOnlyList<KeyGesture>> current = [];
        string? selectedId = null;
        KeyGesture? pending = null;
        bool HasKeys(string id) => current.GetValueOrDefault(id, []).Count > 0;
        string KeysOf(string id) => HasKeys(id) ? EditorCommand.Text(current[id]) : "Unassigned";

        void ShowCommand()
        {
            pending = null;
            capture.Text = "";
            assignedTo.Text = "";
            assign.IsEnabled = false;
            bool any = selectedId is not null;
            foreach (var c in new Control[] { keyList, remove, clear, capture }) c.IsEnabled = any;
            if (selectedId is not { } id)
            {
                resetCommand.IsEnabled = false;
                (name.Text, description.Text, keyList.ItemsSource) = ("Pick a command", "", null);
                return;
            }

            var command = EditorCommands.Get(id);
            bool changed = Overrides().ContainsKey(id);
            name.Text = command.Description;
            description.Text = $"{command.Group}{(changed ? " · changed by you" : "")}";
            keyList.ItemsSource = current.GetValueOrDefault(id, []).Select(EditorCommand.Name).ToList();
            resetCommand.IsEnabled = changed;
        }

        void FillTree()
        {
            string term = search.Text?.Trim() ?? "";
            TreeViewItem? reselect = null;
            tree.ItemsSource = EditorCommands.Defaults
                .Where(c => term.Length == 0 || $"{c.Group} {c.Description} {KeysOf(c.Id)}".Contains(term, StringComparison.OrdinalIgnoreCase))
                .GroupBy(c => c.Group)
                .Select(g => new TreeViewItem
                {
                    Header = g.Key,
                    IsExpanded = true,
                    ItemsSource = g.Select(c =>
                    {
                        var row = new Grid { ColumnDefinitions = new("230,*") };
                        var keys = new TextBlock { Text = KeysOf(c.Id), Opacity = HasKeys(c.Id) ? 1 : 0.5 };
                        Grid.SetColumn(keys, 1);
                        row.Children.AddRange([new TextBlock { Text = c.Description, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) }, keys]);
                        ToolTip.SetTip(row, c.Description);
                        var item = new TreeViewItem { Header = row, Tag = c.Id };
                        if (c.Id == selectedId) reselect = item;
                        return item;
                    }).ToList(),
                }).ToList();
            if (reselect is not null) tree.SelectedItem = reselect;
            else if (selectedId is not null)
            {
                // The picked command is filtered out: let go of it, so the panel never edits a hidden command.
                selectedId = null;
                ShowCommand();
            }
        }

        void Refresh(string? message = null)
        {
            cubaseRows.IsVisible = edited.KeyPreset == KeyPreset.Cubase;
            edited = edited with { CubaseKeyFile = Blank(file.Text) };
            (current, var problem) = Keymap.For(edited);
            status.Text = problem ?? message ?? (edited.KeyPreset == KeyPreset.Cubase
                ? "Commands with a Cubase match take its keys; the rest keep the editor's own keys where they're free."
                : "");
            FillTree();
            ShowCommand();
        }

        void SetOverrides(Dictionary<string, List<string>> overrides) =>
            edited = edited with { KeyOverrides = new(edited.KeyOverrides) { [edited.KeyPreset.ToString()] = overrides } };

        tree.SelectionChanged += (_, _) =>
        {
            if (tree.SelectedItem is TreeViewItem { Tag: string id })
            {
                selectedId = id;
                ShowCommand();
            }
        };
        search.TextChanged += (_, _) => FillTree();
        preset.SelectionChanged += (_, _) =>
        {
            edited = edited with { KeyPreset = presets[Math.Max(0, preset.SelectedIndex)].Preset };
            Refresh();
        };
        file.TextChanged += (_, _) => Refresh(); // CubaseKeyFile.Read re-parses only a changed file; the tree is still rebuilt
        reload.Click += (_, _) =>
        {
            CubaseKeyFile.Forget();
            Refresh("Reloaded.");
        };

        remove.Click += (_, _) =>
        {
            if (selectedId is not { } id || keyList.SelectedIndex < 0) return;
            var gone = current[id][keyList.SelectedIndex];
            SetOverrides(new(Overrides()) { [id] = [.. current[id].Where(g => g != gone).Select(Keymap.Format)] });
            Refresh();
        };
        clear.Click += (_, _) =>
        {
            if (selectedId is not { } id) return;
            SetOverrides(new(Overrides()) { [id] = [] });
            Refresh();
        };
        resetCommand.Click += (_, _) =>
        {
            if (selectedId is not { } id) return;
            var overrides = new Dictionary<string, List<string>>(Overrides());
            overrides.Remove(id);
            SetOverrides(overrides);
            Refresh();
        };
        resetPreset.Click += async (_, _) =>
        {
            if (Overrides().Count == 0) return;
            if (!await Confirm((Window)TopLevel.GetTopLevel(body)!, "Reset preset",
                $"Undo all {Overrides().Count} of your key changes to the {presets.First(p => p.Preset == edited.KeyPreset).Name.Split(' ')[0]} preset?", "Reset"))
                return;
            SetOverrides([]);
            Refresh("Your changes to this preset are undone.");
        };

        // Keys are caught only while this box has focus, before the dialog sees them, so Enter, Esc or Ctrl+Tab can be assigned too.
        capture.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            // Tab and Shift+Tab still move focus, so the box can be left without the mouse.
            if (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None) return;
            e.Handled = true;
            if (selectedId is not { } id || e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
                return;
            pending = new KeyGesture(e.Key, e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt));
            capture.Text = EditorCommand.Name(pending);
            if (current.GetValueOrDefault(id, []).Contains(pending))
                (assignedTo.Text, assign.IsEnabled) = ("Already a key of this command.", false);
            else
            {
                assignedTo.Text = Keymap.Owner(current, pending, id) is { } owner
                    ? $"Assigned to: {EditorCommands.Get(owner).Group} → {EditorCommands.Get(owner).Description}"
                    : "Unassigned";
                assign.IsEnabled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        assign.Click += async (_, _) =>
        {
            if (selectedId is not { } id || pending is not { } gesture) return;
            if (Keymap.Owner(current, gesture, id) is { } owner
                && !await Confirm((Window)TopLevel.GetTopLevel(body)!, "Key in use",
                    $"Move {EditorCommand.Name(gesture)} from \"{EditorCommands.Get(owner).Description}\" to \"{EditorCommands.Get(id).Description}\"?", "Move"))
                return;
            SetOverrides(Keymap.AddKey(Overrides(), current, id, gesture));
            Refresh();
        };

        Refresh();
        return () => edited with { CubaseKeyFile = Blank(file.Text) };
    }

    private static Task<T?> Show<T>(Window owner, string title, Func<StackPanel, Action<T>, (Action Ok, string OkText)> build)
    {
        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
            Background = new SolidColorBrush(Color.Parse("#2b2b2d")),
        };
        var result = new TaskCompletionSource<T?>();
        void Close(T value)
        {
            result.TrySetResult(value);
            window.Close();
        }

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        var (ok, okText) = build(body, Close);
        var okButton = new Button { Content = okText, IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        okButton.Click += (_, _) => ok();
        cancel.Click += (_, _) => window.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.AddRange([okButton, cancel]);
        body.Children.Add(buttons);
        window.Content = body;
        window.Closed += (_, _) => result.TrySetResult(default);
        _ = window.ShowDialog(owner);
        return result.Task;
    }
}

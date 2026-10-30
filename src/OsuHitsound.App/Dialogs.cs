using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

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

    /// <summary>The preferences form; the edited copy on OK, null on Cancel.</summary>
    public static async Task<Preferences?> EditPreferences(Window owner, Preferences prefs, IReadOnlyList<(int Device, string Name)> devices) =>
        await Show<Preferences>(owner, "Preferences", (body, close) =>
        {
            TextBox PathRow(string label, string? value, string note, bool folder)
            {
                var box = new TextBox { Text = value ?? "", Width = 380, Watermark = note };
                var browse = new Button { Content = "Browse…" };
                browse.Click += async (_, _) =>
                {
                    var storage = TopLevel.GetTopLevel(body)!.StorageProvider;
                    var picked = folder
                        ? (await storage.OpenFolderPickerAsync(new() { Title = label })).FirstOrDefault()?.TryGetLocalPath()
                        : (await storage.OpenFilePickerAsync(new() { Title = label, FileTypeFilter = [new("osu.Game.Resources.dll") { Patterns = ["osu.Game.Resources.dll"] }] }))
                            .FirstOrDefault()?.TryGetLocalPath();
                    if (picked is not null) box.Text = picked;
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                row.Children.AddRange([box, browse]);
                body.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold });
                body.Children.Add(row);
                return box;
            }

            var songs = PathRow("osu! Songs folder (Open starts here)", prefs.SongsFolder, "Not found yet", folder: true);
            var lazer = PathRow("Default sounds: lazer's osu.Game.Resources.dll", prefs.LazerResources, "Blank: the usual lazer install", folder: false);

            body.Children.Add(new TextBlock { Text = "Audio output", FontWeight = FontWeight.SemiBold });
            var device = new ComboBox { ItemsSource = devices.Select(d => d.Name).ToList(), Width = 380 };
            device.SelectedIndex = Math.Max(0, devices.ToList().FindIndex(d => d.Device == prefs.AudioDevice));
            body.Children.Add(device);

            var bufferText = new TextBlock();
            var buffer = new Slider { Minimum = 20, Maximum = 500, Value = prefs.BufferMs, Width = 380, TickFrequency = 10, IsSnapToTickEnabled = true };
            void ShowBuffer() => bufferText.Text = $"Buffer: {buffer.Value:0} ms. Smaller reacts faster; raise it if playback crackles.";
            buffer.ValueChanged += (_, _) => ShowBuffer();
            ShowBuffer();
            body.Children.Add(bufferText);
            body.Children.Add(buffer);

            static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            return (() => close(prefs with
            {
                SongsFolder = Blank(songs.Text),
                LazerResources = Blank(lazer.Text),
                AudioDevice = devices[Math.Max(0, device.SelectedIndex)].Device,
                BufferMs = (int)buffer.Value,
            }), "OK");
        });

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

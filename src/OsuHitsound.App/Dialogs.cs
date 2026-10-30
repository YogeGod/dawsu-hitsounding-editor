using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

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

    public static async Task<string?> Choose(Window owner, string title, string label, IReadOnlyList<string> items) =>
        await Show<string?>(owner, title, (body, close) =>
        {
            var list = new ListBox { ItemsSource = items, Height = 260, Width = 400 };
            list.DoubleTapped += (_, _) => { if (list.SelectedItem is string s) close(s); };
            body.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
            body.Children.Add(list);
            return (() => { if (list.SelectedItem is string s) close(s); }, "Add");
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

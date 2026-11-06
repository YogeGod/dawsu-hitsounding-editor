using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace OsuHitsound.App;

internal enum CloseChoice { Cancel, Save, Discard }

internal static partial class Dialogs
{
    public static Task<CloseChoice> UnsavedClose(Window owner, string name)
    {
        var dialog = new Window
        {
            Title = "Unsaved changes", Width = 480, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
            Background = Paint.Brush(UiTheme.Current.Color("title.bg")),
        };
        dialog.Classes.Add("editorDialog");
        var save = new Button { Content = "Save", IsDefault = true };
        var discard = new Button { Content = "Discard" };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Classes.Add("primary");
        save.Click += (_, _) => dialog.Close(CloseChoice.Save);
        discard.Click += (_, _) => dialog.Close(CloseChoice.Discard);
        cancel.Click += (_, _) => dialog.Close(CloseChoice.Cancel);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.AddRange([save, discard, cancel]);
        var body = new StackPanel { Margin = new Thickness(16), Spacing = 16 };
        body.Children.Add(new TextBlock { Text = $"Save changes to {name} before closing?", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(buttons);
        dialog.Content = body;
        return dialog.ShowDialog<CloseChoice>(owner);
    }
}

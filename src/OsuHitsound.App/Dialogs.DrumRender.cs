using Avalonia.Controls;
using Avalonia.Media;

namespace OsuHitsound.App;

internal static partial class Dialogs
{
    public static Task<bool> ReviewDrumRender(Window owner, string summary, string[] rows) =>
        Show<bool>(owner, "Render VST drums", (body, close) =>
        {
            body.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
            body.Children.Add(new ListBox { ItemsSource = rows, Height = 160, Width = 520 });
            return (() => close(true), "Render");
        });
}

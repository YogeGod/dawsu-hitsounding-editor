using Avalonia;
using Avalonia.Controls;

namespace OsuHitsound.App;

internal static class SelectionBarLayout
{
    public static void Apply(Grid content, Button disclosure, bool collapsed, double expandedMinHeight)
    {
        content.MinHeight = collapsed ? 0 : expandedMinHeight;
        content.Margin = new Thickness(8, collapsed ? 0 : 2);
        disclosure.MinHeight = collapsed ? 14 : 30;
        disclosure.Height = collapsed ? 14 : 30;
        disclosure.Margin = new Thickness(0, collapsed ? 0 : 4, 8, 0);
    }
}

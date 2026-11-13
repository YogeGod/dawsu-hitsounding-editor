using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace OsuHitsound.App;

/// <summary>
/// The transport's Cycle, Return and Follow buttons name themselves when the window is wide enough (UI refresh,
/// 2026-11-13), in both editors. Narrower windows keep the icon alone: with six speed buttons beside them, the
/// labelled buttons would reach the centred time display.
/// </summary>
internal static class TransportLabels
{
    /// <summary>The narrowest window whose transport has room for the labels.</summary>
    public const double MinWidth = 1710;

    public static void Fit(double width, params (Button Button, string Text, string Glyph)[] buttons)
    {
        bool named = width >= MinWidth;
        foreach (var (button, text, glyph) in buttons)
        {
            if (button.Content is not StackPanel content)
                button.Content = content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { new AppIcon { Glyph = glyph, Size = 20 }, new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center } },
                };
            content.Children[1].IsVisible = named;
            button.Width = named ? double.NaN : 30;
            button.Padding = named ? new Thickness(9, 0, 11, 0) : default;
        }
    }
}

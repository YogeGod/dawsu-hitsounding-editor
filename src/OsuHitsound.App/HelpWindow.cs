using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace OsuHitsound.App;

/// <summary>F1: every keyboard shortcut and mouse action from <see cref="EditorCommands"/>, grouped and searchable.</summary>
internal sealed class HelpWindow : Window
{
    /// <summary>Help rows (group, keys, what it does) matching the search in any column; all of them for a blank search.</summary>
    public static IEnumerable<(string Group, string Keys, string Description)> Rows(string search) =>
        EditorCommands.All.Select(c => (c.Group, c.KeysText, c.Description))
            .Concat(EditorCommands.Mouse)
            .Where(r => string.IsNullOrWhiteSpace(search) || $"{r.Item1} {r.Item2} {r.Item3}".Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));

    public HelpWindow()
    {
        Title = "Shortcuts and help";
        Width = 640;
        Height = 620;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Background = new SolidColorBrush(Color.Parse("#2b2b2d"));

        var search = new TextBox { Watermark = "Search shortcuts…", Margin = new Thickness(12, 12, 12, 6) };
        var list = new StackPanel { Spacing = 2, Margin = new Thickness(12, 0, 12, 12) };
        void Fill()
        {
            list.Children.Clear();
            foreach (var group in Rows(search.Text ?? "").GroupBy(r => r.Group))
            {
                list.Children.Add(new TextBlock { Text = group.Key, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 2), Foreground = Brushes.White });
                foreach (var (_, keys, description) in group)
                {
                    var row = new Grid { ColumnDefinitions = new("220,*") };
                    row.Children.Add(new TextBlock { Text = keys, Foreground = new SolidColorBrush(Color.Parse("#9ecbff")), TextWrapping = TextWrapping.Wrap });
                    var text = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#d8d8da")) };
                    Grid.SetColumn(text, 1);
                    row.Children.Add(text);
                    list.Children.Add(row);
                }
            }

            if (list.Children.Count == 0) list.Children.Add(new TextBlock { Text = "Nothing matches.", Foreground = Brushes.Gray });
        }

        search.TextChanged += (_, _) => Fill();
        Fill();
        var layout = new DockPanel();
        DockPanel.SetDock(search, Dock.Top);
        layout.Children.Add(search);
        layout.Children.Add(new ScrollViewer { Content = list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = layout;
        Opened += (_, _) => search.Focus();
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) Close(); };
    }
}

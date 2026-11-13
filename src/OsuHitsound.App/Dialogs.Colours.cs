using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace OsuHitsound.App;

internal static partial class Dialogs
{
    /// <summary>
    /// The Colours tab, laid out as Cubase's User Interface – General Colors page: five Default Color Schemes, then rows
    /// in groups, each a tick box and a swatch whose ▾ opens hue, saturation and brightness sliders (two-slider rows take
    /// the scheme's hue). Every change shows at once through <paramref name="preview"/>; Apply saves without closing.
    /// Returns the colours as edited.
    /// </summary>
    internal static Func<UserColors> ColoursPage(StackPanel page, UserColors start, bool darkModeFallback,
        Action<UserColors> preview, Action<UserColors> apply)
    {
        var current = start.Scheme is null ? start with { Scheme = darkModeFallback ? UiTheme.SlateScheme : 0 } : start;
        var refreshers = new List<Action>();
        void Changed()
        {
            preview(current);
            foreach (var refresh in refreshers) refresh();
        }

        double SchemeHue() => current.Rows.TryGetValue(ThemeColors.UserScheme, out var s) ? s.H
            : Hsb.FromColor(Color.Parse(UiTheme.Current.BaseValue("panel.bg"))).H;
        Hsb Shown(ColorRow row, Hsb c) => row.HasHue ? c : c with { H = SchemeHue() };

        // "Default Color Schemes ——": a bold group title with a rule after it, as Cubase draws them.
        void Header(string text)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 6, 0, 0), Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
            grid.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold });
            var rule = new Border { Height = 1, Background = Paint.Token("line.divider"), Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(rule, 1);
            grid.Children.Add(rule);
            page.Children.Add(grid);
        }

        Header("Default Color Schemes");
        var schemes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        string[] schemeNames = ["Grey", "Dark", "Blue-grey dark", "Teal dark", "Slate"];
        for (int i = 0; i < UiTheme.SchemeCount; i++)
        {
            int scheme = i;
            var swatch = new Button
            {
                Width = 52, Height = 30, Padding = new Thickness(3),
                Content = new Border { Background = new SolidColorBrush(UiTheme.SchemeSwatch(scheme)), CornerRadius = new CornerRadius(2), Width = 44, Height = 22 },
            };
            AutomationProperties.SetName(swatch, $"{schemeNames[scheme]} colour scheme");
            ToolTip.SetTip(swatch, schemeNames[scheme]);
            swatch.Click += (_, _) => { current = current with { Scheme = scheme }; Changed(); };
            refreshers.Add(() =>
            {
                bool on = current.Scheme == scheme;
                swatch.BorderBrush = on ? Paint.Token("button.active") : null;
                swatch.BorderThickness = new Thickness(on ? 2 : 0);
            });
            schemes.Children.Add(swatch);
        }
        page.Children.Add(schemes);

        foreach (var group in ThemeColors.Rows.GroupBy(r => r.Group))
        {
            Header(group.Key);
            foreach (var row in group) page.Children.Add(ColourRow(row));
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        var defaults = new Button { Content = "Defaults" };
        ToolTip.SetTip(defaults, "Back to the Slate scheme with no custom colours");
        defaults.Click += (_, _) => { current = new UserColors { Scheme = UiTheme.SlateScheme }; Changed(); };
        var applyButton = new Button { Content = "Apply" };
        ToolTip.SetTip(applyButton, "Keep these colours now; the window stays open");
        applyButton.Click += (_, _) => apply(current);
        buttons.Children.AddRange([defaults, applyButton]);
        page.Children.Add(buttons);
        page.Children.Add(new TextBlock
        {
            Text = "Changes show as you make them. OK keeps them; Cancel puts the previous colours back.",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 460, Opacity = 0.7,
        });

        Changed();
        return () => current;

        Control ColourRow(ColorRow row)
        {
            var check = new CheckBox { Content = row.Label, Width = 220 };
            var fill = new Border { CornerRadius = new CornerRadius(2), Width = 22, Height = 16 };
            var arrow = new TextBlock { Text = "▾", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var swatchContent = new Panel();
            swatchContent.Children.AddRange([fill, arrow]);
            var swatch = new Button { Width = 28, Height = 22, Padding = new Thickness(2), Content = swatchContent };
            AutomationProperties.SetName(swatch, $"{row.Group} {row.Label} colour");
            AutomationProperties.SetName(check, $"{row.Group} {row.Label}");

            var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
            swatch.Flyout = flyout;
            flyout.Opening += (_, _) => flyout.Content = Sliders(row);

            bool syncing = false;
            check.IsCheckedChanged += (_, _) =>
            {
                if (syncing) return;
                current = current.With(row.Id, check.IsChecked == true ? ThemeColors.Start(row, UiTheme.Current.BaseValue) : null);
                if (check.IsChecked != true) // rows that need this one switch off with it, as Cubase greys them
                    foreach (var child in ThemeColors.Rows.Where(r => r.Requires == row.Id)) current = current.With(child.Id, null);
                Changed();
            };
            refreshers.Add(() =>
            {
                syncing = true;
                bool on = current.Rows.TryGetValue(row.Id, out var colour);
                check.IsChecked = on;
                check.IsEnabled = row.Requires is null || current.Rows.ContainsKey(row.Requires);
                swatch.IsEnabled = on;
                fill.Background = on ? new SolidColorBrush(Shown(row, colour).ToColor()) : null;
                arrow.Foreground = on && Shown(row, colour).B > 0.6 ? Brushes.Black : Brushes.White;
                arrow.IsVisible = on;
                syncing = false;
            });

            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            line.Children.AddRange([check, swatch]);
            return line;
        }

        // The pop-up under a swatch: hue (when the row has one), saturation and brightness, each with its number.
        Control Sliders(ColorRow row)
        {
            var panel = new StackPanel { Spacing = 6, Margin = new Thickness(4) };
            Hsb Now() => current.Rows.TryGetValue(row.Id, out var c) ? c : ThemeColors.Start(row, UiTheme.Current.BaseValue);
            void Set(Hsb c) { current = current.With(row.Id, c); Changed(); }
            var sliders = new List<ColorSlider>();
            void Add(string name, double max, Func<Hsb, double> get, Func<Hsb, double, Hsb> with, Func<Hsb, double, Color> colourAt)
            {
                var slider = new ColorSlider { Maximum = max, Width = 200, ColorAt = f => colourAt(Shown(row, Now()), f) };
                slider.Value = get(Now());
                AutomationProperties.SetName(slider, name);
                var number = new TextBlock { Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = slider.Value.ToString("0") };
                slider.ValueChanged += v =>
                {
                    number.Text = v.ToString("0");
                    Set(with(Now(), v));
                    foreach (var other in sliders) other.InvalidateVisual(); // the other tracks follow the new colour
                };
                sliders.Add(slider);
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                line.Children.AddRange([slider, number]);
                panel.Children.Add(line);
            }

            if (row.HasHue)
                Add("Hue", 360, c => c.H, (c, v) => c with { H = v }, (_, f) => new Hsb(f * 360, 0.85, 0.95).ToColor());
            Add("Saturation", 100, c => c.S * 100, (c, v) => c with { S = v / 100 }, (c, f) => (c with { S = f, B = Math.Max(c.B, 0.5) }).ToColor());
            Add("Brightness", 100, c => c.B * 100, (c, v) => c with { B = v / 100 }, (c, f) => (c with { B = f }).ToColor());
            return panel;
        }
    }
}

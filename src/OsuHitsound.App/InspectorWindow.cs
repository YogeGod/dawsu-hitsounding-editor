using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace OsuHitsound.App;

/// <summary>
/// The UI inspector (Ctrl+Shift+I): every part of the main screen, grouped. Turn on Pick and click a part of the editor
/// to select it, or choose it from the list; its colours, sizes, font sizes and labels can then be changed and show at
/// once. Save writes ui-theme.json in the project; Revert goes back to the saved file.
/// </summary>
internal sealed class InspectorWindow : Window
{
    private static readonly string[] Swatches =
    [
        "#262628", "#2f2f31", "#3a3a3c", "#48484c", "#a4a4a9", "#d6d6da", "#e4e4e6", "#ffffff",
        "#c04fd0", "#3fb5b0", "#d98a2b", "#4cb05e", "#3d8fd1", "#2f6fd0", "#ffcc33", "#ff453a",
    ];

    private readonly UiTheme theme;
    private readonly string path;
    private readonly ListBox list = new();
    private readonly StackPanel editor = new() { Spacing = 10, Margin = new Thickness(12) };
    private readonly TextBlock saveState = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };
    private readonly ToggleButton pick = new() { Content = "Pick", Focusable = false };
    private bool dirty;

    public InspectorWindow(UiTheme theme, string path)
    {
        this.theme = theme;
        this.path = path;
        Title = "UI inspector";
        Width = 420;
        Height = 760;
        MinWidth = 320;
        Background = new SolidColorBrush(Color.Parse("#2b2b2d"));
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        ShowActivated = false;

        ToolTip.SetTip(pick, "Turn on, then click any part of the editor to select it here.");
        var save = new Button { Content = "Save", Focusable = false };
        var revert = new Button { Content = "Revert", Focusable = false };
        ToolTip.SetTip(save, $"Write the changes to {path}");
        ToolTip.SetTip(revert, "Go back to the last saved theme");
        save.Click += (_, _) => Save();
        revert.Click += (_, _) => Revert();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8) };
        top.Children.AddRange([pick, save, revert, saveState]);

        list.ItemsSource = UiTheme.Parts;
        list.ItemTemplate = new FuncDataTemplate<UiPart>((part, _) => new TextBlock
        {
            Text = part is null ? "" : $"{part.Group}  ·  {part.Name}",
            FontSize = 12,
        });
        list.SelectionChanged += (_, _) => ShowPart(list.SelectedItem as UiPart);

        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,220,*") };
        Grid.SetRow(list, 1);
        var scroller = new ScrollViewer { Content = editor };
        Grid.SetRow(scroller, 2);
        body.Children.AddRange([top, list, scroller]);
        Content = body;

        // The theme outlives this window: unsubscribe on close so a closed inspector is not kept alive.
        theme.Changed += OnThemeChanged;
        Closed += (_, _) => theme.Changed -= OnThemeChanged;
        UpdateSaveState();
    }

    /// <summary>True while Pick is on: the main window then routes its next click here instead of acting on it.</summary>
    public bool IsPicking => pick.IsChecked == true;

    /// <summary>Selects a part (after a pick) and turns Pick off.</summary>
    public void Select(string partId)
    {
        list.SelectedItem = UiTheme.Parts.FirstOrDefault(p => p.Id == partId);
        if (list.SelectedItem is not null) list.ScrollIntoView(list.SelectedItem);
        pick.IsChecked = false;
    }

    private void ShowPart(UiPart? part)
    {
        editor.Children.Clear();
        if (part is null) return;
        editor.Children.Add(new TextBlock { Text = part.Name, FontSize = 16, FontWeight = FontWeight.SemiBold });
        foreach (var setting in part.Settings)
            editor.Children.Add(EditorFor(setting));
    }

    private Control EditorFor(Setting setting)
    {
        var panel = new StackPanel { Spacing = 4 };
        var header = new DockPanel();
        var reset = new Button { Content = "Reset", FontSize = 11, Padding = new Thickness(6, 1), Focusable = false };
        DockPanel.SetDock(reset, Dock.Right);
        header.Children.AddRange([reset, new TextBlock { Text = setting.Label, VerticalAlignment = VerticalAlignment.Center }]);
        panel.Children.Add(header);

        var box = new TextBox { Text = theme.Get(setting.Key) };
        void Apply(string value)
        {
            try
            {
                theme.Set(setting.Key, value);
                box.BorderBrush = null;
            }
            catch (FormatException ex)
            {
                box.BorderBrush = Brushes.IndianRed;
                ToolTip.SetTip(box, ex.Message);
            }
        }

        switch (setting.Kind)
        {
            case SettingKind.Color:
            {
                var preview = new Border { Width = 28, Height = 24, CornerRadius = new CornerRadius(4), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1) };
                void Show() => preview.Background = new SolidColorBrush(theme.Color(setting.Key));
                Show();
                var row = new DockPanel();
                DockPanel.SetDock(preview, Dock.Left);
                preview.Margin = new Thickness(0, 0, 6, 0);
                row.Children.AddRange([preview, box]);
                panel.Children.Add(row);

                var swatches = new WrapPanel();
                foreach (var hex in Swatches)
                {
                    var swatch = new Button
                    {
                        Width = 20, Height = 20, Margin = new Thickness(0, 0, 3, 3), Padding = default, Focusable = false,
                        Background = new SolidColorBrush(Color.Parse(hex)), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1),
                    };
                    ToolTip.SetTip(swatch, hex);
                    swatch.Click += (_, _) => { box.Text = hex; };
                    swatches.Children.Add(swatch);
                }

                panel.Children.Add(swatches);
                box.TextChanged += (_, _) => { if (box.Text is { } t) { Apply(t); Show(); } };
                break;
            }

            case SettingKind.Number:
            {
                var slider = new Slider
                {
                    Minimum = setting.Min, Maximum = setting.Max, Value = theme.Number(setting.Key), Focusable = false,
                    SmallChange = setting.Max <= 1 ? 0.01 : 1,
                };
                bool syncing = false;
                slider.ValueChanged += (_, e) =>
                {
                    if (syncing) return;
                    double v = setting.Max <= 1 ? Math.Round(e.NewValue, 2) : Math.Round(e.NewValue);
                    syncing = true;
                    box.Text = v.ToString(CultureInfo.InvariantCulture);
                    syncing = false;
                };
                box.TextChanged += (_, _) =>
                {
                    if (box.Text is not { } t) return;
                    Apply(t);
                    if (!syncing && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d >= setting.Min && d <= setting.Max)
                    {
                        syncing = true;
                        slider.Value = d;
                        syncing = false;
                    }
                };
                var row = new DockPanel();
                box.Width = 70;
                DockPanel.SetDock(box, Dock.Right);
                row.Children.AddRange([box, slider]);
                panel.Children.Add(row);
                break;
            }

            case SettingKind.ColorList:
            {
                var previews = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
                void Show()
                {
                    previews.Children.Clear();
                    foreach (var c in theme.Colors(setting.Key))
                        previews.Children.Add(new Border { Width = 22, Height = 16, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(c) });
                }

                Show();
                panel.Children.Add(box);
                panel.Children.Add(previews);
                panel.Children.Add(new TextBlock { Text = "Group colour first, then the lane shades, separated by commas.", FontSize = 11, Foreground = Brushes.Gray });
                box.TextChanged += (_, _) => { if (box.Text is { } t) { Apply(t); if (UiTheme.IsValid(setting, t)) Show(); } };
                break;
            }

            default:
                panel.Children.Add(box);
                box.TextChanged += (_, _) => { if (box.Text is { } t) Apply(t); };
                break;
        }

        reset.Click += (_, _) => box.Text = setting.Default;
        return panel;
    }

    private void Save()
    {
        try
        {
            theme.Save(path);
            dirty = false;
            UpdateSaveState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            saveState.Text = $"Couldn't save: {ex.Message}";
        }
    }

    private void Revert()
    {
        theme.ReplaceWith(UiTheme.Load(path));
        dirty = false;
        UpdateSaveState();
        ShowPart(list.SelectedItem as UiPart);
    }

    private void OnThemeChanged()
    {
        dirty = true;
        saveState.Text = "Not saved";
    }

    private void UpdateSaveState() => saveState.Text = dirty ? "Not saved" : "Saved";
}

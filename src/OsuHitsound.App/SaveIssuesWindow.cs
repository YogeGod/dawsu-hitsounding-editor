using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>A persistent, keyboard-accessible list beside the editor; it never changes or discards edits.</summary>
internal sealed class SaveIssuesWindow : Window
{
    private readonly TextBlock summary = new() { FontSize = 22, FontWeight = FontWeight.SemiBold };
    private readonly StackPanel items = new() { Spacing = 12 };
    private readonly Action<SaveIssue> navigate;
    private SaveIssue[]? shown;

    public SaveIssuesWindow(Action<SaveIssue> navigate)
    {
        this.navigate = navigate;
        Title = "Save issues";
        Width = 620;
        Height = 530;
        MinWidth = 440;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Background = Paint.Brush("#252529");
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(24) };
        body.Children.Add(summary);
        var hint = new TextBlock
        {
            Text = "Save leaves the map unchanged until these issues are resolved. Your edits and undo history stay available.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 20), Foreground = Paint.Brush("#c4c4cf"),
        };
        Grid.SetRow(hint, 1);
        body.Children.Add(hint);
        var scroll = new ScrollViewer { Content = items, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2);
        body.Children.Add(scroll);
        var close = new Button { Content = "Back to editing", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 3);
        body.Children.Add(close);
        Content = body;
    }

    public void UpdateIssues(IReadOnlyList<SaveIssue> issues)
    {
        if (shown is not null && shown.SequenceEqual(issues)) return;
        shown = issues.ToArray();
        summary.Text = issues.Count == 0 ? "Ready to save" : $"{issues.Count} save issue{(issues.Count == 1 ? "" : "s")}";
        items.Children.Clear();
        if (issues.Count == 0)
            items.Children.Add(new TextBlock { Text = "All listed issues are resolved. Return to the editor and save when you are ready.", TextWrapping = TextWrapping.Wrap });
        foreach (var issue in issues)
        {
            var time = TimeSpan.FromMilliseconds(Math.Max(0, issue.Time));
            string at = $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds:000}";
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(new TextBlock { Text = at, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Paint.Brush("#ffc78a") });
            body.Children.Add(new TextBlock { Text = issue.Reason, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
            body.Children.Add(new TextBlock { Text = issue.Resolution, TextWrapping = TextWrapping.Wrap, Foreground = Paint.Brush("#c4c4cf") });
            var go = new Button { Content = "Go to issue", HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(go, $"Go to issue at {at}: {issue.Reason}");
            go.Click += (_, _) => navigate(issue);
            body.Children.Add(go);
            items.Children.Add(new Border { Child = body, Padding = new Thickness(16), Background = Paint.Brush("#303039"), CornerRadius = new CornerRadius(6) });
        }
    }
}

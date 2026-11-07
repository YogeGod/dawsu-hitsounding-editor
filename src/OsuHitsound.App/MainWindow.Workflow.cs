using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private string? workflowWarning;
    private string WorkflowFeedback => string.Join("\n\n", new[] { workflowWarning, audioError, workflowResult }.Where(s => !string.IsNullOrEmpty(s)));

    private void SetupWorkflowFeedback() => WorkflowStatusButton.Click += async (_, _) => await ShowWorkflowDetails();

    private void RefreshWorkflowFeedback()
    {
        var feedback = WorkflowFeedback;
        WorkflowStatusButton.IsVisible = feedback.Length > 0;
        bool warning = workflowWarning is not null || audioError is not null;
        WorkflowStatusIcon.Glyph = warning ? "TriangleAlert" : "Info";
        WorkflowStatusButton.Foreground = Paint.Brush(warning ? "#ffd166" : UiTheme.Current.Get("text.secondary"));
        ToolTip.SetTip(WorkflowStatusButton, feedback + "\nClick for operation details.");
        AutomationProperties.SetName(WorkflowStatusButton, "Operation details: " + feedback);
    }

    private async Task ShowWorkflowDetails()
    {
        var details = new Window
        {
            Title = "Operation details", Width = Math.Min(640, Bounds.Width - 40), Height = Math.Min(420, Bounds.Height - 40),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
        };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var layout = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);
        layout.Children.Add(new TextBox
        {
            Text = string.Join("\n\n", new[] { doc?.Path, WorkflowFeedback }.Where(s => !string.IsNullOrEmpty(s))),
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        if (doc is not null) Action("Open map folder", OpenMapFolder);
        Action("Open backups", OpenBackups);
        if (session?.Unanchored.Any() == true || lastCompile?.Result.Conflicts.Count > 0)
            Action("Next problem", () => { details.Close(); ShowNextOutputProblem(); });
        if (audioError is not null) Action("Preferences…", () => { details.Close(); _ = ShowPreferences(); });
        if (workflowResult is not null) Action("Dismiss result", () =>
        {
            workflowResult = flashMessage = null;
            RefreshWorkflowFeedback();
            details.Close();
        });
        var close = Action("Close", () => details.Close());
        close.IsCancel = close.IsDefault = true;
        details.Content = layout;
        await details.ShowDialog(this);

        Button Action(string label, Action run)
        {
            var button = new Button { Content = label, Margin = new Thickness(4, 2) };
            button.Click += (_, _) => run();
            actions.Children.Add(button);
            return button;
        }
    }

    private void OpenBackups()
    {
        try
        {
            string folder = MapSaver.DefaultBackupRoot();
            Directory.CreateDirectory(folder);
            var start = new System.Diagnostics.ProcessStartInfo("explorer.exe");
            start.ArgumentList.Add(folder);
            System.Diagnostics.Process.Start(start);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { flash = $"Couldn't open backups: {ex.Message}"; UpdateStatus(); }
    }
}

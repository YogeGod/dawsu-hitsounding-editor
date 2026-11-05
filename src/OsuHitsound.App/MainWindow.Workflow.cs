using Avalonia.Controls;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private string? workflowWarning;
    private void SetupWorkflowFeedback()
    {
        NextProblemButton.Click += (_, _) => ShowNextOutputProblem();
        WorkingFolderButton.Click += (_, _) => OpenMapFolder();
        WorkflowHelpToggle.IsCheckedChanged += (_, _) => WorkflowHelpPanel.IsVisible = WorkflowHelpToggle.IsChecked == true;
        WorkflowPreferencesButton.Click += async (_, _) => await ShowPreferences();
        WorkflowDetailsButton.Click += async (_, _) =>
        {
            var details = new Window
            {
                Title = "Output and operation details", Width = Math.Min(640, Bounds.Width - 40), Height = Math.Min(420, Bounds.Height - 40),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
            };
            var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            close.Click += (_, _) => details.Close();
            var layout = new DockPanel { Margin = new Avalonia.Thickness(12) };
            DockPanel.SetDock(close, Dock.Bottom);
            layout.Children.Add(close);
            layout.Children.Add(new TextBox { Text = string.Join("\n\n", new[] { doc?.Path, workflowWarning, WorkflowMessage.Text }.Where(s => !string.IsNullOrEmpty(s))),
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            details.Content = layout;
            await details.ShowDialog(this);
        };
        WorkflowBackupsButton.Click += (_, _) =>
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
        };
        DismissWorkflowButton.Click += (_, _) =>
        {
            workflowResult = flashMessage = null;
            RefreshWorkflowFeedback();
            Lanes.Focus();
        };
        WorkflowFooter.SizeChanged += (_, _) => UpdateVolumePanelHeight();
    }

    private void RefreshWorkflowFeedback()
    {
        WorkingCopyLabel.Text = doc is null ? "Output:" : "Editing:";
        WorkingFolderButton.Content = "Open map folder";
        WorkingFolderButton.IsVisible = doc is not null;
        WorkingCopyPath.Text = doc?.Path ?? "Open a map to begin.";
        ToolTip.SetTip(WorkingCopyPath, doc?.Path);
        WorkflowMessage.Text = workflowResult ?? (audioError is not null ? $"No sound: {audioError}. Check Preferences → Audio output." : "");
        WorkflowWarning.Text = workflowWarning ?? "";
        WorkflowWarning.IsVisible = !string.IsNullOrEmpty(workflowWarning);
        WorkflowMessage.IsVisible = !string.IsNullOrEmpty(WorkflowMessage.Text);
        WorkflowMessageRow.IsVisible = WorkflowMessage.IsVisible || WorkflowWarning.IsVisible;
        DismissWorkflowButton.IsVisible = workflowResult is not null;
        NextProblemButton.IsVisible = session?.Unanchored.Any() == true || lastCompile?.Result.Conflicts.Count > 0;
        WorkflowPreferencesButton.IsVisible = doc is null || audioError is not null || WorkflowMessage.Text?.Contains("Preferences", StringComparison.OrdinalIgnoreCase) == true;
        bool compact = Bounds.Height < 600;
        WorkflowMessageScroll.MaxHeight = compact ? 44 : 88;
        foreach (var text in new[] { WorkflowMessage, WorkflowWarning })
        {
            text.MaxLines = compact ? 1 : 0;
            text.TextWrapping = compact ? Avalonia.Media.TextWrapping.NoWrap : Avalonia.Media.TextWrapping.Wrap;
            text.TextTrimming = compact ? Avalonia.Media.TextTrimming.CharacterEllipsis : Avalonia.Media.TextTrimming.None;
            ToolTip.SetTip(text, text.Text);
        }
    }
}

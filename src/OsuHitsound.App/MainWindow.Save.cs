using Avalonia.Controls;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private bool allowClose, closePromptOpen;
    private SaveIssuesWindow? saveIssuesWindow;

    private async Task CloseSafely()
    {
        if (closePromptOpen) return;
        closePromptOpen = true;
        try
        {
            var decision = await Dialogs.UnsavedChanges(this);
            if (decision == CloseDecision.Cancel) return;
            if (decision == CloseDecision.Save && !await Save()) return;
            allowClose = true;
            Close();
        }
        finally { closePromptOpen = false; }
    }

    private void ShowSaveIssues()
    {
        if (session is null || lastCompile is null) return;
        if (saveIssuesWindow is null)
        {
            saveIssuesWindow = new SaveIssuesWindow(GoToSaveIssue);
            saveIssuesWindow.Closed += (_, _) => saveIssuesWindow = null;
            saveIssuesWindow.UpdateIssues(SaveIssue.Find(session, session.State, lastCompile.Value.Result));
            saveIssuesWindow.Show(this);
        }
        else
        {
            saveIssuesWindow.UpdateIssues(SaveIssue.Find(session, session.State, lastCompile.Value.Result));
            saveIssuesWindow.Activate();
        }
    }

    private void GoToSaveIssue(SaveIssue issue)
    {
        if (session is null || model is null) return;
        var hits = session.State.Hits.Where(h => issue.Moment is { } key ? h.Anchor == key : h.Anchor is null && h.Time == issue.Time).ToList();
        // A missing hitnormal can leave no hit at all; still reveal its original lane.
        var lane = hits.FirstOrDefault()?.Lane ?? (issue.Moment is { } moment
            ? session.MomentByKey[moment].Hits.FirstOrDefault()?.Lane : null);
        LaneFilterBox.Text = "";
        if (lane is not null) Lanes.FocusLane(lane);
        Lanes.Edits.Select(hits);
        Lanes.Axis.Left = issue.Time - Lanes.Axis.VisibleDuration / 3;
        Seek(issue.Time);
        Activate();
        Lanes.Focus();
    }

    private void UpdateSaveReadiness()
    {
        bool hasMap = session is not null;
        SaveButton.IsEnabled = SaveItem.IsEnabled = hasMap;
        SaveIssuesButton.IsVisible = hasMap;
        if (!hasMap) { saveIssuesWindow?.Close(); return; }
        if (lastCompile is null)
        {
            SaveIssuesButton.Content = "Checking save…";
            SaveIssuesButton.IsEnabled = false;
            return;
        }
        var issues = SaveIssue.Find(session!, session!.State, lastCompile.Value.Result);
        SaveIssuesButton.Content = issues.Count > 0 ? $"{issues.Count} save issue{(issues.Count == 1 ? "" : "s")}…" : "Ready to save";
        SaveIssuesButton.IsEnabled = issues.Count > 0;
        SaveIssuesButton.Classes.Set("needsAttention", issues.Count > 0);
        saveIssuesWindow?.UpdateIssues(issues);
    }

    internal Task<bool> SaveForVerification() => Save();
    internal void ShowSaveIssuesForVerification() => ShowSaveIssues();
}

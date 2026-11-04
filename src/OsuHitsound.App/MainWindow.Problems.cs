namespace OsuHitsound.App;

public partial class MainWindow
{
    private void ShowNextOutputProblem()
    {
        if (session is null || model is null) return;
        var conflicts = lastCompile?.Result.Conflicts ?? [];
        var times = session.Unanchored.Select(h => h.Time).Concat(conflicts.Select(c => c.Time)).Distinct().Order().ToList();
        if (times.Count == 0) return;
        double time = times.FirstOrDefault(t => t > Lanes.Playhead + 0.01, times[0]);
        var keys = conflicts.Where(c => Math.Abs(c.Time - time) < 0.01).Select(c => c.Moment).ToHashSet();
        var hits = session.State.Hits.Where(h => h.Anchor is { } key ? keys.Contains(key) : Math.Abs(h.Time - time) < 0.01).ToList();
        LaneFilterBox.Text = "";
        foreach (var lane in hits.Select(h => h.Lane).Distinct().Reverse()) Lanes.RevealLane(lane);
        Lanes.Edits.Select(hits);
        Seek(time);
        Lanes.Axis.Left = time - Lanes.Axis.VisibleDuration * 0.35;
        SyncScrollBars();
        Lanes.Focus();
    }
}

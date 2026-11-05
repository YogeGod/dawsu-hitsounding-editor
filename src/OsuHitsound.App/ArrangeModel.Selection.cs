using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal sealed partial class ArrangeModel
{
    private readonly HashSet<Lane> selectedLanes = [];
    private Lane? laneSelectionAnchor;
    public IReadOnlySet<Lane> SelectedLanes => selectedLanes;

    /// <summary>Lane-list selection is independent of the selected hits and of the active sample editor.</summary>
    public void SelectLane(Lane lane, bool extend = false, bool toggle = false)
    {
        var visible = Rows.OfType<LaneRow>().Select(r => r.Lane).Distinct().ToList();
        int end = visible.IndexOf(lane);
        if (end < 0) return;
        int start = laneSelectionAnchor is { } anchor ? visible.IndexOf(anchor) : -1;
        if (extend && start >= 0)
        {
            if (!toggle) selectedLanes.Clear();
            selectedLanes.UnionWith(visible.Skip(Math.Min(start, end)).Take(Math.Abs(end - start) + 1));
        }
        else
        {
            if (!toggle) selectedLanes.Clear();
            if (!toggle || !selectedLanes.Remove(lane)) selectedLanes.Add(lane);
            laneSelectionAnchor = lane;
        }
        Changed?.Invoke();
    }

    public void DeselectLanes()
    {
        selectedLanes.Clear();
        laneSelectionAnchor = null;
        Changed?.Invoke();
    }

    private void ToggleSelected(HashSet<Lane> set, Lane clicked)
    {
        lock (muted)
        {
            var lanes = selectedLanes.Contains(clicked) ? selectedLanes : [clicked];
            if (set.Contains(clicked)) set.ExceptWith(lanes);
            else set.UnionWith(lanes);
        }
        Changed?.Invoke();
    }
}

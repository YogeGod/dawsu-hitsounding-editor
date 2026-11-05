using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal sealed partial class ArrangeModel
{
    public Dictionary<string, DrumRenderSource> DrumRenders { get; private set; } = [];
    private Lane? RenderParent(Lane lane) => DrumRenders.TryGetValue(ProjectStore.KeyOf(lane), out var source) ? ProjectStore.LaneOf(source.Parent) : null;
    private string? RenderFamily(Lane lane)
    {
        var key = ProjectStore.KeyOf(lane);
        if (DrumRenders.TryGetValue(key, out var source)) return "vst:" + source.Parent;
        return DrumRenders.Values.Any(s => s.Parent == key) ? "vst:" + key : null;
    }
    private void RestoreDrumRenders(IReadOnlyDictionary<string, DrumRenderSource>? sources)
    {
        DrumRenders = [];
        foreach (var (key, source) in sources ?? new Dictionary<string, DrumRenderSource>())
            if (source is not null && !string.IsNullOrWhiteSpace(source.Parent) && source.Note is >= 0 and <= 127 && source.Velocity is >= 0 and <= 127
                && ProjectStore.LaneOf(key) is { Source: LaneSource.BeatmapFile } lane && MapFolder.IsInside(lane.Name)
                && ProjectStore.LaneOf(source.Parent) is { } parent && Enum.IsDefined(parent.Source) && MapFolder.IsInside(parent.Name)
                && key != source.Parent && !sources!.ContainsKey(source.Parent)) DrumRenders[key] = source;
    }
    public void ApplyDrumRender(IReadOnlyDictionary<EditHit, Lane> assignments, Dictionary<string, DrumRenderSource> metadata)
    {
        var before = DrumRenders.ToDictionary(); var after = before.ToDictionary();
        foreach (var (key, source) in metadata) after[key] = source;
        var oldCollapsed = collapsedFamilies.ToHashSet();
        var oldRows = laneRows.ToList(); var oldAdded = added.ToHashSet(); var oldHidden = hidden.ToHashSet();
        void Restore()
        {
            collapsedFamilies.Clear(); collapsedFamilies.UnionWith(oldCollapsed);
            DrumRenders = before; laneRows.Clear(); laneRows.AddRange(oldRows);
            added.Clear(); added.UnionWith(oldAdded); hidden.Clear(); hidden.UnionWith(oldHidden); RefreshRows();
        }
        void Apply()
        {
            DrumRenders = after;
            foreach (var lane in assignments.Values.Distinct())
            {
                hidden.Remove(lane); added.Add(lane);
                if (laneRows.All(r => r.Lane != lane)) laneRows.Add(new(DisplayGroup(RenderParent(lane) ?? lane), lane, 0, laneRows.Count, null));
            }
            foreach (var lane in before.Keys.Select(ProjectStore.LaneOf).OfType<Lane>())
                if (!Session.State.Hits.Any(h => h.Lane == lane)) hidden.Add(lane);
            foreach (var source in metadata.Values)
            {
                var parent = ProjectStore.LaneOf(source.Parent)!;
                added.Add(parent); hidden.Remove(parent);
                collapsedFamilies.Add("vst:" + source.Parent);
            }
            RefreshRows();
        }
        Session.ApplyDrumRender(assignments, Restore, Apply, before.Keys.Select(ProjectStore.LaneOf).OfType<Lane>().ToHashSet());
    }
}

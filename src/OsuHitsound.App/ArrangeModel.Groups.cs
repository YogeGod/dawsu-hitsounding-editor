using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal sealed partial class ArrangeModel
{
    private readonly Dictionary<Lane, Lane> laneSlots = [];
    private readonly Dictionary<Lane, int> depths = [];
    private readonly Dictionary<LaneGroup, IReadOnlyList<LaneRow>> closedFolders = [];

    public Lane SlotOf(Lane lane) => laneSlots.GetValueOrDefault(lane) ?? lane;
    public string? FamilyKey(Lane lane) => RenderFamily(lane) ?? FamilyOf(SlotOf(lane));
    public int DepthOf(Lane lane) => DrumRenders.ContainsKey(ProjectStore.KeyOf(lane)) ? 2 : DrumMapMode ? 1 : depths.GetValueOrDefault(lane, 1);
    public bool IsFamilyBase(Lane lane) => FamilyKey(lane) is { } key && familyParents.GetValueOrDefault(key) == lane;
    public IReadOnlyList<LaneRow> FolderChildren(LaneGroup group) => closedFolders.GetValueOrDefault(group) ?? [];
    public Dictionary<string, string> SlotLayout() => laneSlots.ToDictionary(p => ProjectStore.KeyOf(p.Key), p => ProjectStore.KeyOf(p.Value));

    public Lane NextSlot(Lane family, MapFolder folder)
    {
        var known = laneRows.Select(r => r.Lane).ToHashSet();
        var withHits = Session.State.Hits.Select(h => h.Lane).ToHashSet();
        var visible = laneRows.Where(r => !hidden.Contains(r.Lane) || withHits.Contains(r.Lane)).Select(r => SlotOf(r.Lane));
        var undisplayed = folder.Files.Select(f => new Lane(LaneSource.BeatmapFile, f)).Where(l => !known.Contains(l));
        return SampleSlots.Next(visible.Concat(undisplayed), SlotOf(family));
    }

    public void AddSlotLane(Lane source, Lane slot)
    {
        if (Session.IsGestureActive || laneRows.Any(r => r.Lane == source)) return;
        var active = ActiveLane;
        laneSlots[source] = slot; added.Add(source); hidden.Add(source);
        laneRows.Add(new LaneRow(DisplayGroup(slot), source, 0, laneRows.Count, slot.Sample?.Index));
        var order = NormalizeFamilies(laneRows).Select(r => r.Lane).ToList();
        Session.ChangeLaneLayout("Add sample layer", order,
            () => Relayout(() => { hidden.Add(source); ActiveLane = active; }),
            () => { RevealLane(source); });
    }

    private void RestoreSlots(IReadOnlyDictionary<string, string>? slots)
    {
        laneSlots.Clear();
        if (slots is null) { RefreshSlotRows(); return; }
        foreach (var (key, value) in slots)
            if (value is not null && ProjectStore.LaneOf(key) is { } source && ProjectStore.LaneOf(value) is { } slot
                && Enum.IsDefined(source.Source) && Enum.IsDefined(slot.Source) && MapFolder.IsInside(source.Name) && MapFolder.IsInside(slot.Name))
                laneSlots[source] = slot;
        RefreshSlotRows();
    }

    private void RefreshSlotRows()
    {
        for (int i = 0; i < laneRows.Count; i++)
        {
            var row = laneRows[i]; var slot = SlotOf(row.Lane);
            laneRows[i] = row with { Group = DisplayGroup(slot), Index = slot.Sample?.Index };
        }
    }

    private IEnumerable<LaneRow> NormalizeFamilies(IEnumerable<LaneRow> source)
    {
        foreach (var family in source.GroupBy(r => FamilyKey(r.Lane) ?? "file:" + ProjectStore.KeyOf(r.Lane)))
        {
            var items = family.ToList();
            var parent = items.FirstOrDefault(r => RenderFamily(r.Lane) is not null && !DrumRenders.ContainsKey(ProjectStore.KeyOf(r.Lane)))
                ?? items.FirstOrDefault(r => SlotOf(r.Lane).Sample is { Index: 1 })
                ?? items.FirstOrDefault(r => SlotOf(r.Lane).Sample is { Index: 0 }) ?? items[0];
            yield return parent;
            foreach (var child in items.Where(r => r != parent)) yield return child;
        }
    }

    private List<Row> BuildGroupedRows()
    {
        var withHits = Session.State.Hits.Select(h => h.Lane).ToHashSet();
        var visible = NormalizeFamilies(laneRows.Where(r => !hidden.Contains(r.Lane) || withHits.Contains(r.Lane))).ToList();
        familyParents.Clear(); familiesWithChildren.Clear(); collapsedChildren.Clear(); closedFolders.Clear(); depths.Clear();
        var rows = new List<Row>();
        foreach (var family in visible.GroupBy(r => FamilyKey(r.Lane) ?? "file:" + ProjectStore.KeyOf(r.Lane)))
        {
            var items = family.ToList(); var parent = items[0];
            string? key = FamilyKey(parent.Lane);
            depths[parent.Lane] = 1;
            foreach (var child in items.Skip(1)) depths[child.Lane] = 2;
            if (key is not null)
            {
                familyParents[key] = parent.Lane;
                if (items.Count > 1) familiesWithChildren.Add(key);
            }
            var matches = items.Where(r => MatchesFilter(r.Lane)).ToList();
            if (matches.Count == 0) continue;
            if (Filter.Length > 0)
            { rows.Add(parent); rows.AddRange(matches.Where(r => r != parent)); }
            else if (key is null || !collapsedFamilies.Contains(key)) rows.AddRange(matches);
            else
            {
                rows.Add(parent);
                collapsedChildren[parent.Lane] = items.Skip(1).ToList();
            }
        }
        return rows;
    }

    public void RevealLane(Lane lane)
    {
        if (!laneRows.Any(r => r.Lane == lane)) return;
        Relayout(() =>
        {
            hidden.Remove(lane); collapsed.Remove(DisplayGroup(SlotOf(lane)));
            if (FamilyKey(lane) is { } key) collapsedFamilies.Remove(key);
            ActiveLane = lane;
        });
    }

    private void MoveGroupedLane(Lane lane, Lane target, bool after)
    {
        var source = laneRows.FirstOrDefault(r => r.Lane == lane);
        var destination = laneRows.FirstOrDefault(r => r.Lane == target);
        if (source is null || destination is null || lane == target) return;
        string? family = FamilyKey(lane), other = FamilyKey(target);
        var order = NormalizeFamilies(laneRows).Select(r => r.Lane).ToList();
        List<Lane> moving;
        if (family is not null && family == other)
        {
            if (familyParents.GetValueOrDefault(family) == lane || familyParents.GetValueOrDefault(family) == target) return;
            moving = [lane];
        }
        else if (family is not null)
        {
            moving = order.Where(l => FamilyKey(l) == family).ToList();
        }
        else moving = [lane];
        order.RemoveAll(moving.Contains);
        var targets = order.Where(l => other is null || family == other ? l == target : FamilyKey(l) == other).ToList();
        int index = after ? order.IndexOf(targets[^1]) + 1 : order.IndexOf(targets[0]);
        order.InsertRange(index, moving);
        Session.SetLaneOrder(order, moving.Count > 1 ? "Move sample family" : "Move lane");
    }
}

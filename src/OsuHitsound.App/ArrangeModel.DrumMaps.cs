using System.Collections.Immutable;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal sealed record UnassignedDrumRow(DrumMapEntry Entry) : Row(LaneGroup.OwnFiles);
internal sealed partial class ArrangeModel
{
    public DrumMapCollection DrumMaps { get; private set; } = DrumMapCollection.Empty;
    public bool DrumMapMode => DrumMaps.Enabled;
    public DrumMapPreset? ActiveDrumMap => DrumMaps.Presets.FirstOrDefault(p => p.Id == DrumMaps.Active);
    public event Action? DrumMapsChanged;

    public void RestoreDrumMaps(DrumMapCollection maps)
    { maps.Validate(); Relayout(() => DrumMaps = maps); }

    public void ChangeDrumMaps(DrumMapCollection maps, string label = "Edit drum map", IReadOnlyDictionary<Lane, Lane>? move = null, DrumMapOverlap? overlap = null)
    {
        maps.Validate(); var before = DrumMaps;
        void Apply(DrumMapCollection value) { Relayout(() => DrumMaps = value); DrumMapsChanged?.Invoke(); }
        if (move is { Count: > 0 }) Session.MoveDrumMapHits(move, overlap, () => Apply(before), () => Apply(maps));
        else Session.ChangeLaneLayout(label, Session.State.LaneOrder, () => Apply(before), () => Apply(maps));
    }

    public void SetDrumMapMode(bool enabled)
    {
        var maps = DrumMaps;
        if (enabled && maps.Presets.Count == 0)
        { maps = DrumMapDefaults.Create(); }
        ChangeDrumMaps(maps with { Enabled = enabled }, "Switch lane view");
    }

    public void SetDrumPreset(DrumMapPreset preset, string label = "Edit drum map", IReadOnlyDictionary<Lane, Lane>? move = null, DrumMapOverlap? overlap = null)
    {
        preset.Validate();
        var existing = DrumMaps.Presets.FindIndex(p => p.Id == preset.Id);
        var presets = existing < 0 ? DrumMaps.Presets.Add(preset) : DrumMaps.Presets.SetItem(existing, preset);
        ChangeDrumMaps(DrumMaps with { Presets = presets, Active = preset.Id }, label, move, overlap);
    }

    public string DrumRowName(LaneRow row) => row.InstrumentName ?? NameOf(row.Lane);
    public Guid? ActiveDrumEntry { get; private set; }
    public void SetActiveRow(LaneRow row) { ActiveDrumEntry = row.DrumEntryId; SetActive(row.Lane); }

    private List<Row> BuildDrumRows()
    {
        var grouped = BuildGroupedRows(folders: false);
        var rows = new List<Row>(); var assigned = new HashSet<Lane>();
        foreach (var entry in ActiveDrumMap?.Rows ?? [])
        {
            var lane = entry.LaneKey is { } key ? ProjectStore.LaneOf(key) : null;
            var source = laneRows.FirstOrDefault(r => r.Lane == lane);
            if (source is null)
            {
                if (Filter.Length == 0 || entry.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)) rows.Add(new UnassignedDrumRow(entry));
                continue;
            }
            assigned.Add(source.Lane);
            var children = laneRows.Where(r => RenderParent(r.Lane) == source.Lane && (!hidden.Contains(r.Lane) || Session.State.Hits.Any(h => h.Lane == r.Lane))).ToList();
            foreach (var child in children) assigned.Add(child.Lane);
            if (Filter.Length == 0 || entry.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase) || MatchesFilter(source.Lane))
            {
                rows.Add(source with { DrumEntryId = entry.Id, InstrumentName = entry.Name, Number = rows.Count + 1 });
                if (!IsFamilyCollapsed(source.Lane) || Filter.Length > 0) rows.AddRange(children);
            }
        }
        // Never lose access to samples or existing hits when entering the instrument view.
        var withHits = Session.State.Hits.Select(h => h.Lane).ToHashSet();
        rows.AddRange(laneRows.Where(r => !assigned.Contains(r.Lane) && RenderFamily(r.Lane) is null && (!hidden.Contains(r.Lane) || withHits.Contains(r.Lane)) && MatchesFilter(r.Lane)));
        rows.AddRange(grouped.OfType<LaneRow>().Where(r => !assigned.Contains(r.Lane) && RenderFamily(r.Lane) is not null));
        return rows;
    }

    public void MoveDrumRow(Row source, Row target, bool after)
    {
        if (!DrumMapMode || ActiveDrumMap is not { } preset || source == target || Filter.Length > 0 || Rows.Count > 4096 ||
            DrumMaps.Presets.Where(p => p.Id != preset.Id).Sum(p => p.Rows.Count) + Rows.Count > 16384) return;
        DrumMapEntry Entry(Row row) => row switch
        {
            UnassignedDrumRow empty => empty.Entry,
            LaneRow { DrumEntryId: { } id } => preset.Rows.Single(r => r.Id == id),
            LaneRow lane => new(Guid.NewGuid(), NameOf(lane.Lane)[..Math.Min(128, NameOf(lane.Lane).Length)], ProjectStore.KeyOf(lane.Lane), ProjectStore.KeyOf(SlotOf(lane.Lane))),
            _ => throw new InvalidOperationException("Select an instrument row.")
        };
        if (source is LaneRow sr && RenderParent(sr.Lane) is not null || target is LaneRow tr && RenderParent(tr.Lane) is not null) return;
        var pairs = Rows.Where(row => row is not LaneRow r || RenderParent(r.Lane) is null).Select(row => (Row: row, Entry: Entry(row))).ToList();
        var moving = pairs.Single(p => p.Row == source); pairs.Remove(moving);
        int index = pairs.FindIndex(p => p.Row == target); if (index < 0) return;
        pairs.Insert(index + (after ? 1 : 0), moving);
        SetDrumPreset(preset with { Rows = pairs.Select(p => p.Entry).ToImmutableList() }, "Move instrument row");
    }

    public Lane ShiftDrumLane(Lane lane, int shift, int? anchorRow = null)
    {
        if (shift == 0) return lane;
        anchorRow ??= ActiveDrumEntry is { } active ? Rows.ToList().FindIndex(r => r is LaneRow l && l.DrumEntryId == active && l.Lane == lane) : null;
        int at = anchorRow is { } index && index >= 0 && index < Rows.Count && Rows[index] is LaneRow r && r.Lane == lane
            ? index : Rows.ToList().FindIndex(r => r is LaneRow l && l.Lane == lane);
        if (at < 0) return lane;
        int destination = Math.Clamp(at + shift, 0, Rows.Count - 1);
        return Rows[destination] is LaneRow target ? target.Lane : lane;
    }
}

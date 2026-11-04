using System.Collections.Immutable;

namespace OsuHitsound.Lanes;

public sealed partial class EditSession
{
    private int nextVoice;

    public IReadOnlyList<EditHit> AddWithVolumes(IEnumerable<(EditHit Hit, int Volume)> hits, string label = "Paste")
    {
        IReadOnlyList<EditHit> placed = [];
        Change(label, s => PlaceLayers(s, hits.Select(h => (h.Hit, s.HsMode ? (int?)Math.Clamp(h.Volume, 0, 100) : null)), [], out placed));
        return placed;
    }

    private EditState PlaceLayers(EditState state, IEnumerable<(EditHit Hit, int? Volume)> additions,
        HashSet<EditHit> remove, out IReadOnlyList<EditHit> placed)
    {
        var hits = state.Hits.RemoveAll(remove.Contains);
        var present = hits.ToHashSet();
        var layers = state.LayerVolumes.ToBuilder();
        var volumes = state.Volumes.ToBuilder();
        var result = new List<EditHit>();
        foreach (var (incoming, volume) in additions)
        {
            var hit = incoming;
            int ExistingVolume(EditHit h) => layers.TryGetValue(h, out int v) ? v
                : h.Anchor is { } anchor ? volumes.GetValueOrDefault(anchor, 100) : 100;
            if (volume is { } wanted && present.Contains(hit) && ExistingVolume(hit) != wanted)
            {
                do { hit = hit with { Voice = checked(++nextVoice) }; } while (present.Contains(hit));
            }
            if (present.Add(hit))
            {
                hits = hits.Add(hit);
                layers.Remove(hit);
                if (volume is { } v)
                {
                    layers[hit] = v;
                    if (hit.Anchor is { ObjectIndex: < 0 } key && !volumes.ContainsKey(key)) volumes[key] = v;
                }
            }
            result.Add(hit);
        }
        placed = result.Distinct().ToList();
        if (hits.SequenceEqual(state.Hits) && layers.Count == state.LayerVolumes.Count && layers.All(p => state.LayerVolumes.TryGetValue(p.Key, out int v) && v == p.Value)
            && volumes.Count == state.Volumes.Count && volumes.All(p => state.Volumes.TryGetValue(p.Key, out int v) && v == p.Value)) return state;
        return state with { Hits = hits, LayerVolumes = layers.ToImmutable(), Volumes = volumes.ToImmutable() };
    }
}

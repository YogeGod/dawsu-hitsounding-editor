using System.Collections.Immutable;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

public sealed partial class EditSession
{
    /// <summary>
    /// When off (the default), a sound added to an object from another sample index moves the whole object to that
    /// index, as osu!'s editor does: its hitnormal and other sounds go to that index's lanes (the map's file when it has
    /// one, otherwise the skin's). Likewise an addition from another set moves the object's other additions to that
    /// set. Turn it on to keep such sounds on one object on purpose (a mix); Save then makes a combined copy of them.
    /// </summary>
    public bool KeepMixedIndexes { get; set; }

    /// <summary>The mapset folder as listed now, so sample files added while the map is open count as that index's lanes.</summary>
    internal void UseFolder(MapFolder folder) => sampleFolder = folder;

    // The object's index after an edit, read from the sounds the edit put on it; null when the edit itself brought
    // several indexes (a mix, kept as it is).
    private int? IndexFor(IReadOnlyList<EditHit> added, IReadOnlyList<EditHit> kept)
    {
        static List<int> FileIndexes(IEnumerable<EditHit> hits) => [.. hits
            .Where(h => h.Lane.Source == LaneSource.BeatmapFile && h.Lane.Sample is not null).Select(h => h.Lane.Sample!.Value.Index).Distinct()];
        var files = FileIndexes(added);
        if (files.Count > 1) return null;
        if (files.Count == 1) return files[0];
        // Only skin sounds were added: the object keeps its index while that index plays them from the skin.
        var current = FileIndexes(kept);
        if (current.Count > 1) return null;
        int index = current.Count == 1 ? current[0] : 0;
        return added.All(h => h.Lane.Sample is not { } s || Decompiler.ResolveLane(sampleFolder, s.Set, s.Sound, index) == h.Lane) ? index : 0;
    }

    /// <summary>Moves every object an edit added sounds to onto one sample index (see <see cref="KeepMixedIndexes"/>).</summary>
    private EditState ConformIndexes(EditState before, EditState after)
    {
        if (KeepMixedIndexes || ReferenceEquals(before.Hits, after.Hits)) return after;
        var old = before.Hits.ToHashSet();
        var touched = after.Hits.Where(h => h.Anchor is not null && !old.Contains(h)).ToLookup(h => h.Anchor!.Value);
        if (touched.Count == 0) return after;
        var onMoment = after.Hits.Where(h => h.Anchor is { } k && touched.Contains(k)).ToLookup(h => h.Anchor!.Value);
        var replace = new Dictionary<EditHit, EditHit>();
        foreach (var group in touched)
        {
            var all = onMoment[group.Key].ToList();
            if (all.Any(h => h.SetPlacement != Guid.Empty)) continue; // sound sets bake their own layers
            var added = group.ToList();
            var kept = all.Except(added).ToList(); // the sounds just placed stay where they were put
            if (IndexFor(added, kept) is not { } index) continue;
            // An object has one addition set too: clap, whistle and finish follow the set of the addition just added.
            var additionSets = added.Select(h => h.Lane.Sample).Where(x => x is { Sound: not "hitnormal" }).Select(x => x!.Value.Set).Distinct().ToList();
            SampleSet? additions = additionSets.Count == 1 ? additionSets[0] : null;
            foreach (var hit in kept)
                if (hit.Lane.Sample is { } s
                    && Decompiler.ResolveLane(sampleFolder, s.Sound != "hitnormal" && additions is { } set ? set : s.Set, s.Sound, index) is var lane
                    && lane != hit.Lane)
                    replace[hit] = hit with { Lane = lane };
        }

        if (replace.Count == 0) return after;
        var seen = new HashSet<EditHit>();
        var hits = after.Hits.Select(h => replace.GetValueOrDefault(h, h)).Where(seen.Add).ToImmutableList();
        var layers = after.LayerVolumes;
        foreach (var (from, to) in replace)
            if (layers.TryGetValue(from, out int volume)) layers = layers.Remove(from).SetItem(to, volume);
        return after with { Hits = hits, LayerVolumes = layers }; // the new lanes are all in Lanes already
    }
}

using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

public enum DrumMapOverlap { KeepBoth, KeepDestination, ReplaceDestination }
public sealed partial class EditSession
{
    public IReadOnlyList<EditHit> DrumMapOverlaps(IReadOnlyDictionary<Lane, Lane> assignments)
    {
        var moving = State.Hits.Where(h => assignments.TryGetValue(h.Lane, out var target) && target != h.Lane).ToList();
        var remaining = State.Hits.Except(moving).ToList();
        var incoming = moving.Select(h => h with { Lane = assignments[h.Lane] }).ToList();
        return incoming.Where((hit, i) => remaining.Concat(incoming.Take(i)).Any(h => h.Lane == hit.Lane && Math.Abs(h.Time - hit.Time) < .5)).ToList();
    }

    /// <summary>Explicit current-difficulty migration; preserves times, anchors, volume and mirrored-row identity.</summary>
    public void MoveDrumMapHits(IReadOnlyDictionary<Lane, Lane> assignments, DrumMapOverlap? overlap,
        Action undoLayout, Action redoLayout)
    {
        if (IsGestureActive) throw new InvalidOperationException("Finish the current edit first.");
        if (DrumMapOverlaps(assignments).Count > 0 && overlap is null) throw new InvalidOperationException("Choose how to handle the overlapping hits first.");
        Change("Reassign drum-map hits", state =>
        {
            var moving = state.Hits.Where(h => assignments.TryGetValue(h.Lane, out var target) && target != h.Lane).ToList();
            var remove = moving.ToHashSet();
            var kept = state.Hits.Where(h => !remove.Contains(h)).ToList();
            var additions = new List<(EditHit Hit, int? Volume)>();
            foreach (var original in moving)
            {
                var incoming = original with { Lane = assignments[original.Lane] };
                var collisions = kept.Where(h => h.Lane == incoming.Lane && Math.Abs(h.Time - incoming.Time) < .5).ToList();
                if (collisions.Count > 0 && overlap == DrumMapOverlap.KeepDestination) continue;
                if (overlap == DrumMapOverlap.ReplaceDestination)
                {
                    foreach (var hit in collisions) { remove.Add(hit); kept.Remove(hit); additions.RemoveAll(a => a.Hit == hit); }
                }
                // Two identical voices still represent two sounds when Keep both was explicitly chosen.
                while (kept.Contains(incoming)) incoming = incoming with { Voice = checked(incoming.Voice + 1) };
                kept.Add(incoming); additions.Add((incoming, VolumeOf(state, original)));
            }
            var next = PlaceLayers(state, additions, remove, out _);
            return ReferenceEquals(next, state) ? state with { } : next;
        }, undoLayout, redoLayout);
    }
}

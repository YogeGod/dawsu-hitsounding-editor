namespace OsuHitsound.Lanes;

/// <summary>
/// Live reload: when another program (osu!'s editor) saves the open map, dawsu! reloads it and puts back the unsaved
/// edits made here. Only what was changed here is carried over (a three-way merge against the map as it was opened), so
/// hitsounds changed in osu! meanwhile are kept too. Hits, moment volumes and layer volumes are carried (HS-mode
/// circles follow from the hits); other kinds of unsaved edit (slides, sound sets, bookmarks, switching HS mode) are
/// reported so the caller can ask first.
/// </summary>
public static class LiveReloadMerge
{
    /// <summary>A hit by lane, time (ms) and voice, independent of the map's object indexes.</summary>
    public readonly record struct HitKey(Lane Lane, double Time, int Voice)
    {
        public static HitKey Of(EditHit hit) => new(hit.Lane, Math.Round(hit.Time, 3), hit.Voice);
    }

    public sealed record Diff(
        IReadOnlyList<HitKey> Removed,
        IReadOnlyList<HitKey> Added,
        IReadOnlyList<(double Time, int Ordinal, int Volume)> MomentVolumes,
        IReadOnlyList<(HitKey Hit, int Volume)> LayerVolumes,
        bool OtherEdits)
    {
        public bool IsEmpty => Removed.Count == 0 && Added.Count == 0 && MomentVolumes.Count == 0 && LayerVolumes.Count == 0;
    }

    /// <summary>What was changed in <paramref name="session"/> since <paramref name="baseline"/> (the map as opened).</summary>
    public static Diff Between(EditSession session, EditState baseline)
    {
        var current = session.State;
        var before = baseline.Hits.Select(HitKey.Of).ToHashSet();
        var now = current.Hits.Select(HitKey.Of).ToHashSet();
        var moments = session.MomentByKey;
        var ordinal = new Dictionary<MomentKey, int>();
        foreach (var group in session.Moments.GroupBy(m => m.Moment.Time))
        {
            int index = 0;
            foreach (var moment in group) ordinal[moment.Key] = index++;
        }
        var volumes = current.Volumes
            .Where(kv => (!baseline.Volumes.TryGetValue(kv.Key, out int old) || old != kv.Value) && moments.ContainsKey(kv.Key))
            .Select(kv => (Time: moments[kv.Key].Time, Ordinal: ordinal.GetValueOrDefault(kv.Key), Volume: kv.Value))
            .OrderBy(v => v.Time).ThenBy(v => v.Ordinal).ToList();
        var layers = current.LayerVolumes
            .Where(kv => !baseline.LayerVolumes.TryGetValue(kv.Key, out int old) || old != kv.Value)
            .Select(kv => (HitKey.Of(kv.Key), kv.Value)).ToList();
        // A layer volume cleared here (setting the moment's volume clears its layers): the hit keeps the volume it plays
        // at now, or the reloaded file's old layer volume would come back.
        var stillHere = current.Hits.ToHashSet();
        layers.AddRange(baseline.LayerVolumes.Keys.Where(h => !current.LayerVolumes.ContainsKey(h) && stillHere.Contains(h))
            .Select(h => (HitKey.Of(h), EditSession.VolumeOf(current, h))));
        bool other = !ReferenceEquals(current.Slides, baseline.Slides) || !ReferenceEquals(current.SoundSets, baseline.SoundSets)
            || !ReferenceEquals(current.SoundPlacements, baseline.SoundPlacements)
            || !ReferenceEquals(current.Bookmarks, baseline.Bookmarks) || !ReferenceEquals(current.BookmarkLabels, baseline.BookmarkLabels)
            || current.HsMode != baseline.HsMode;
        return new([.. before.Except(now)], [.. now.Except(before)], volumes, layers, other);
    }

    public sealed record Result(int Kept, int WithoutObject);

    /// <summary>
    /// Applies <paramref name="diff"/> to the reloaded map as one undo step ("Keep unsaved edits"), placing each hit by
    /// its time: on the object there now, or as a hit with no object when osu! deleted or moved it.
    /// </summary>
    public static Result Apply(EditSession session, Diff diff)
    {
        if (diff.IsEmpty) return new(0, 0);
        // Nearest hit in the lane within the same 2 ms window editing uses.
        Func<HitKey, EditHit?> Finder()
        {
            var byLane = session.State.Hits.ToLookup(h => (h.Lane, h.Voice));
            return key =>
            {
                EditHit? best = null;
                double bestDistance = double.MaxValue;
                foreach (var hit in byLane[(key.Lane, key.Voice)])
                {
                    double distance = Math.Abs(hit.Time - key.Time);
                    if (distance <= EditSession.AnchorTolerance && distance < bestDistance)
                        (best, bestDistance) = (hit, distance);
                }
                return best;
            };
        }
        session.BeginGesture("Keep unsaved edits");
        try
        {
            session.Remove([.. diff.Removed.Select(Finder()).OfType<EditHit>()]);
            var added = diff.Added.Select(k => session.HitAt(k.Lane, k.Time) with { Voice = k.Voice }).ToList();
            session.Add(added);
            var volumes = new Dictionary<MomentKey, int>();
            foreach (var (time, ordinalAtTime, volume) in diff.MomentVolumes)
                if (MomentFor(session, time, ordinalAtTime) is { } key) volumes[key] = volume;
            if (volumes.Count > 0) session.SetVolumes(volumes);
            var find = Finder(); // after the additions, which may be the hits whose layers changed
            var layers = diff.LayerVolumes.Select(l => (Hit: find(l.Hit), l.Volume)).Where(l => l.Hit is not null).ToDictionary(l => l.Hit!, l => l.Volume);
            if (layers.Count > 0) session.SetLayerVolumes(layers);
            return new(diff.Removed.Count + diff.Added.Count + diff.MomentVolumes.Count + diff.LayerVolumes.Count,
                added.Count(h => h.Anchor is null));
        }
        finally { session.EndGesture(); }
    }

    /// <summary>The moment a stored volume belongs to: its place among objects at that time, or the nearest if osu! moved it.</summary>
    private static MomentKey? MomentFor(EditSession session, double time, int ordinal)
    {
        var within = session.Moments.Where(m => Math.Abs(m.Moment.Time - time) <= EditSession.AnchorTolerance).ToList();
        if (within.Count == 0) return null;
        var exact = within.Where(m => m.Moment.Time == time).ToList();
        if (exact.Count == 0)
        {
            double nearest = within.Min(m => Math.Abs(m.Moment.Time - time));
            exact = within.Where(m => Math.Abs(m.Moment.Time - time) - nearest <= 1e-6).ToList();
        }
        return exact[Math.Clamp(ordinal, 0, exact.Count - 1)].Key;
    }
}

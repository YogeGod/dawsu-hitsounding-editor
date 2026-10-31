using System.Collections.Immutable;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>
/// One sound placed in a lane. <see cref="Anchor"/> is the moment (object or slider node) it plays on, or null when
/// no object is at its time: such a hit can't be saved in Apply mode (Generate mode will add a circle for it).
/// Times are timeline milliseconds, like <see cref="Moment.Time"/>.
/// </summary>
public sealed record EditHit(Lane Lane, double Time, MomentKey? Anchor);

/// <summary>
/// The editable state: every placed hit, the volume of each moment (one per object or node in osu!), and the editor
/// bookmarks (ms as written in the .osu, sorted).
/// </summary>
public sealed record EditState(ImmutableList<EditHit> Hits, ImmutableDictionary<MomentKey, int> Volumes, ImmutableList<int> Bookmarks);

/// <summary>What a range edit does with the hits in a time range.</summary>
public enum RangeOperation
{
    Copy,
    Move,
    Delete,
}

/// <summary>
/// Hitsound editing on top of a decompiled map: the hits you want, with undo and redo. Moments are fixed (Apply mode):
/// a hit placed within <see cref="AnchorTolerance"/> of a moment plays on it; elsewhere it is a hit with no object.
/// <see cref="Wanted"/> turns the state into the compiler's input.
/// </summary>
public sealed class EditSession
{
    /// <summary>How close (ms) a hit must be to a moment to play on it. Slider nodes land on fractional times.</summary>
    public const double AnchorTolerance = 2;

    // Each entry is the state on one side of a step and the step's name: undo holds the state before it, redo the state after.
    private readonly List<(EditState State, string Label)> undo = [];
    private readonly List<(EditState State, string Label)> redo = [];
    private EditState? gestureStart;
    private string gestureLabel = "Edit";
    private readonly double timeOffset; // v3/v4 maps show 24 ms later than written; bookmarks are stored as written

    public EditSession(OsuFile file, MapFolder folder, DecompiledMap map)
    {
        Map = map;
        var objectIndex = file.HitObjects.Select((o, i) => (o, i)).ToDictionary(x => x.o, x => x.i); // HitObject compares by reference
        Moments = [.. map.Moments.Select(m => (m, new MomentKey(objectIndex[m.Object], m.Node)))];
        MomentByKey = Moments.ToDictionary(x => x.Key, x => x.Moment);
        State = new EditState(
            [.. Moments.SelectMany(x => x.Moment.Hits.Select(h => new EditHit(h.Lane, x.Moment.Time, x.Key)))],
            Moments.ToImmutableDictionary(x => x.Key, x => x.Moment.Volume),
            [.. file.Bookmarks.Order()]);
        announced = State;
        timeOffset = map.Settings.TimeOffset;
        Lanes = AllLanes(map, folder);
    }

    public DecompiledMap Map { get; }

    public IReadOnlyDictionary<MomentKey, Moment> MomentByKey { get; }

    /// <summary>Every moment with its key, in time order.</summary>
    public IReadOnlyList<(Moment Moment, MomentKey Key)> Moments { get; }

    /// <summary>The lanes you can draw in: the map's, every set-named sample file in the folder, and all 12 skin sounds.</summary>
    public IReadOnlyList<Lane> Lanes { get; }

    public EditState State { get; private set; }

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    public bool IsEdited => undo.Count > 0 || gestureStart is not null;

    /// <summary>The name of every undo step, oldest first: those done, then those undone (Recent edits).</summary>
    public IReadOnlyList<string> Steps => [.. undo.Select(u => u.Label), .. redo.AsEnumerable().Reverse().Select(r => r.Label)];

    /// <summary>How many of <see cref="Steps"/> are done: the state is just after step <c>Position</c> (0 = as loaded).</summary>
    public int Position => undo.Count;

    /// <summary>Undoes or redoes until <paramref name="position"/> steps are done (clamped). Ignored during a drag.</summary>
    public void GoTo(int position)
    {
        if (gestureStart is not null) return;
        position = Math.Clamp(position, 0, undo.Count + redo.Count);
        while (undo.Count > position) StepBack();
        while (undo.Count < position) StepForward();
        Announce(); // once, for the whole jump
    }

    /// <summary>How many steps <see cref="Steps"/> holds, without building it.</summary>
    public int StepCount => undo.Count + redo.Count;

    public event Action? Changed;

    /// <summary>The moment a hit at <paramref name="time"/> plays on, or null when no object is that close.</summary>
    public MomentKey? MomentAt(double time) =>
        Sorted.Nearest(Moments, time, AnchorTolerance, x => x.Moment.Time) is >= 0 and var i ? Moments[i].Key : null;

    /// <summary>A hit in a lane at a time, placed on the moment there if any.</summary>
    public EditHit HitAt(Lane lane, double time) => MomentAt(time) is { } key
        ? new EditHit(lane, MomentByKey[key].Time, key)
        : new EditHit(lane, time, null);

    /// <summary>Volume of a moment, or of a hit with no object (100).</summary>
    public int VolumeOf(EditHit hit) => hit.Anchor is { } key ? State.Volumes[key] : 100;

    // ---- edits; each call outside a gesture is one undo step ----

    public void Add(IEnumerable<EditHit> hits, string label = "Add") => Change(label, s =>
    {
        var existing = s.Hits.ToHashSet();
        var added = hits.Where(existing.Add).ToList();
        return added.Count == 0 ? s : s with { Hits = s.Hits.AddRange(added) };
    });

    public void Remove(IEnumerable<EditHit> hits, string label = "Delete") => Change(label, s =>
    {
        var gone = hits.ToHashSet();
        return s with { Hits = s.Hits.RemoveAll(gone.Contains) };
    });

    /// <summary>
    /// Moves hits by a time offset and to other lanes (laneOf maps each hit to its new lane). Each lands on the moment
    /// at its new time, or becomes a hit with no object. Returns the moved hits, to keep them selected.
    /// </summary>
    public IReadOnlyList<EditHit> Move(IReadOnlyCollection<EditHit> hits, double offset, Func<EditHit, Lane> laneOf, string label = "Move") =>
        MoveEach([.. hits.Select(h => (h, laneOf(h), h.Time + offset))], label);

    /// <summary>Moves each hit to its own lane and time (Quantize), as one undo step. Returns the moved hits.</summary>
    public IReadOnlyList<EditHit> MoveEach(IReadOnlyList<(EditHit Hit, Lane Lane, double Time)> moves, string label)
    {
        var moved = moves.Select(m => HitAt(m.Lane, m.Time)).ToList();
        if (moved.SequenceEqual(moves.Select(m => m.Hit))) return moved.Distinct().ToList(); // nothing moves: not an edit
        Change(label, s =>
        {
            var gone = moves.Select(m => m.Hit).ToHashSet();
            var kept = s.Hits.RemoveAll(gone.Contains);
            var present = kept.ToHashSet();
            return s with { Hits = kept.AddRange(moved.Where(present.Add)) };
        });
        return moved.Distinct().ToList();
    }

    /// <summary>The hits in a time range (its end excluded; the ends may come in either order) and in the given lanes.</summary>
    public IReadOnlyList<EditHit> InRange(double start, double end, IReadOnlySet<Lane> lanes)
    {
        (start, end) = (Math.Min(start, end), Math.Max(start, end));
        return [.. State.Hits.Where(h => h.Time >= start && h.Time < end && lanes.Contains(h.Lane))];
    }

    /// <summary>
    /// Copies, moves or deletes a range's hits, as one undo step. Copies and moved hits keep their lanes and land on the
    /// moment at their new time, or have no object there. Objects never move. Returns the new hits, to select them.
    /// </summary>
    public IReadOnlyList<EditHit> EditRange(double start, double end, IReadOnlySet<Lane> lanes, RangeOperation operation, double offset)
    {
        var hits = InRange(start, end, lanes);
        if (hits.Count == 0) return [];
        switch (operation)
        {
            case RangeOperation.Move:
                return Move(hits, offset, h => h.Lane, "Range move");
            case RangeOperation.Copy:
                return Copy(hits, offset, "Range copy");
            default:
                Remove(hits, "Range delete");
                return [];
        }
    }

    /// <summary>
    /// The moments whose volume is within <paramref name="from"/>–<paramref name="to"/> % (inclusive) and that at least
    /// one of <paramref name="scope"/>'s hits plays on. Hits with no object have no volume yet and are skipped.
    /// </summary>
    public IReadOnlyList<MomentKey> MomentsWithVolume(IEnumerable<EditHit> scope, int from, int to) =>
        [.. scope.Where(h => h.Anchor is not null).Select(h => h.Anchor!.Value).Distinct()
            .Where(k => State.Volumes[k] is var v && v >= from && v <= to)];

    /// <summary>Copies hits by a time offset, in their lanes (one undo step); each lands on the moment there, if any. Returns the copies.</summary>
    public IReadOnlyList<EditHit> Copy(IEnumerable<EditHit> hits, double offset, string label = "Copy")
    {
        var copies = hits.Select(h => HitAt(h.Lane, h.Time + offset)).Distinct().ToList();
        Add(copies, label);
        return copies;
    }

    public void SetVolume(IEnumerable<MomentKey> keys, int volume, string label = "Volume")
    {
        int v = Math.Clamp(volume, 0, 100);
        SetVolumes(keys.Distinct().ToDictionary(k => k, _ => v), label);
    }

    /// <summary>Puts back hits from a saved project (hits with no object): not an edit, so not an undo step.</summary>
    public void Restore(IEnumerable<EditHit> hits)
    {
        var existing = State.Hits.ToHashSet();
        var added = hits.Where(existing.Add).ToList();
        if (added.Count == 0) return;
        State = State with { Hits = State.Hits.AddRange(added) };
        Announce();
    }

    /// <summary>Sets several moments' volumes at once (ramps, paint strokes, relative edits); values are kept within 0–100.</summary>
    public void SetVolumes(IReadOnlyDictionary<MomentKey, int> volumes, string label = "Volume") => Change(label, s =>
    {
        // Only the values that differ: a drag that stays on the same value is not an edit (no redraw, no recompile).
        var changed = volumes.Select(kv => new KeyValuePair<MomentKey, int>(kv.Key, Math.Clamp(kv.Value, 0, 100)))
            .Where(kv => !s.Volumes.TryGetValue(kv.Key, out int old) || old != kv.Value).ToList();
        return changed.Count == 0 ? s : s with { Volumes = s.Volumes.SetItems(changed) };
    });

    /// <summary>Starts a drag or stroke: the changes until <see cref="EndGesture"/> become one undo step, named <paramref name="label"/>.</summary>
    public void BeginGesture(string label = "Edit")
    {
        if (gestureStart is not null) return;
        gestureStart = State;
        gestureLabel = label;
    }

    /// <summary>During a drag: go back to where the gesture began, so the next edit applies the drag's total offset.</summary>
    public void ResetToGestureStart()
    {
        if (gestureStart is not null) State = gestureStart;
    }

    public void EndGesture()
    {
        if (gestureStart is { } start && !ReferenceEquals(start, State))
        {
            undo.Add((start, gestureLabel));
            redo.Clear();
        }

        gestureStart = null;
    }

    /// <summary>Steps back one edit. Ignored during a drag or stroke, whose later moves would act on hits it removed.</summary>
    public void Undo()
    {
        if (gestureStart is not null || undo.Count == 0) return;
        StepBack();
        Announce();
    }

    public void Redo()
    {
        if (gestureStart is not null || redo.Count == 0) return;
        StepForward();
        Announce();
    }

    /// <summary>The compiler's input: every moment's lanes and volume. Hits with no object aren't included.</summary>
    public Dictionary<MomentKey, WantedSound> Wanted() => Wanted(State);

    /// <summary>
    /// The compiler's input for a given state. States are immutable, so a background check can take <see cref="State"/>
    /// now and build this later off the UI thread.
    /// </summary>
    public Dictionary<MomentKey, WantedSound> Wanted(EditState state)
    {
        var lanes = state.Hits.Where(h => h.Anchor is not null).ToLookup(h => h.Anchor!.Value, h => h.Lane);
        return Moments.ToDictionary(x => x.Key, x => new WantedSound([.. lanes[x.Key]], state.Volumes[x.Key]));
    }


    /// <summary>Bookmarks on the timeline (shown times), sorted.</summary>
    public IEnumerable<double> BookmarkTimes => State.Bookmarks.Select(b => b + timeOffset);

    /// <summary>Adds a bookmark at a timeline time (whole ms, as osu! writes them). One undo step.</summary>
    public void AddBookmark(double time) => Change("Add bookmark", s =>
    {
        int ms = (int)Math.Round(time - timeOffset);
        return s.Bookmarks.Contains(ms) ? s : s with { Bookmarks = [.. s.Bookmarks.Add(ms).Order()] };
    });

    /// <summary>The bookmark (timeline time) nearest <paramref name="time"/> within <paramref name="tolerance"/> ms, or null.</summary>
    public double? NearestBookmark(double time, double tolerance) =>
        Sorted.Nearest(State.Bookmarks, time - timeOffset, tolerance, b => b) is >= 0 and var i ? State.Bookmarks[i] + timeOffset : null;

    /// <summary>The first bookmark before (direction -1) or after (+1) <paramref name="time"/>, more than 1 ms away, or null.</summary>
    public double? AdjacentBookmark(double time, int direction) => direction < 0
        ? BookmarkTimes.Where(b => b < time - 1).Cast<double?>().LastOrDefault()
        : BookmarkTimes.Where(b => b > time + 1).Cast<double?>().FirstOrDefault();

    /// <summary>Removes the bookmark nearest <paramref name="time"/> within <paramref name="tolerance"/> ms, if any.</summary>
    public void RemoveBookmarkNear(double time, double tolerance) => Change("Remove bookmark", s =>
        NearestBookmark(time, tolerance) is { } b ? s with { Bookmarks = s.Bookmarks.Remove((int)Math.Round(b - timeOffset)) } : s);
    /// <summary>Hits that have no object to play on.</summary>
    public IEnumerable<EditHit> Unanchored => State.Hits.Where(h => h.Anchor is null);

    private void StepBack()
    {
        var (before, label) = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        redo.Add((State, label));
        State = before;
    }

    private void StepForward()
    {
        var (after, label) = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        undo.Add((State, label));
        State = after;
    }

    private void Change(string label, Func<EditState, EditState> edit)
    {
        var next = edit(State);
        if (!ReferenceEquals(next, State))
        {
            if (gestureStart is null)
            {
                undo.Add((State, label));
                redo.Clear();
            }

            State = next;
        }

        Announce(); // also after a drag went back to its start and then made no change
    }

    private EditState? announced;

    /// <summary>Raises <see cref="Changed"/> when the state differs from the one last announced.</summary>
    private void Announce()
    {
        if (ReferenceEquals(State, announced)) return;
        announced = State;
        Changed?.Invoke();
    }

    private static List<Lane> AllLanes(DecompiledMap map, MapFolder folder)
    {
        var lanes = new List<Lane>(map.Lanes);
        var seen = lanes.ToHashSet();
        string[] sounds = ["hitnormal", "hitwhistle", "hitfinish", "hitclap"];
        foreach (var set in new[] { SampleSet.Normal, SampleSet.Soft, SampleSet.Drum })
        {
            foreach (var sound in sounds)
            {
                for (int index = 1; index <= 100; index++)
                {
                    var lane = Decompiler.ResolveLane(folder, set, sound, index);
                    if (lane.Source == LaneSource.BeatmapFile && seen.Add(lane)) lanes.Add(lane);
                }

                var skin = new Lane(LaneSource.Skin, HitsoundRules.SampleName(set, sound));
                if (seen.Add(skin)) lanes.Add(skin);
            }
        }

        return lanes;
    }
}

using System.Collections.Immutable;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>
/// One sound placed in a lane. <see cref="Anchor"/> is the moment (object or slider node) it plays on, or null when
/// no object is at its time: such a hit can't be saved in Apply mode (Generate mode will add a circle for it).
/// Times are timeline milliseconds, like <see cref="Moment.Time"/>.
/// </summary>
public sealed record EditHit(Lane Lane, double Time, MomentKey? Anchor)
{
    /// <summary>Distinguishes overlapping copies of one sample that need different layer volumes.</summary>
    public int Voice { get; init; }
    public Guid SetPlacement { get; init; }
    public Guid SetMember { get; init; }
}

/// <summary>
/// The editable state: every placed hit, the volume of each moment (one per object or node in osu!), and the editor
/// bookmarks (ms as written in the .osu, sorted).
/// </summary>
public sealed record EditState(ImmutableList<EditHit> Hits, ImmutableDictionary<MomentKey, int> Volumes, ImmutableList<int> Bookmarks)
{
    public ImmutableDictionary<int, string> BookmarkLabels { get; init; } = ImmutableDictionary<int, string>.Empty;
    public ImmutableList<Lane> LaneOrder { get; init; } = [];
    public bool HsMode { get; init; }
    public ImmutableDictionary<MomentKey, double> HsCircles { get; init; } = ImmutableDictionary<MomentKey, double>.Empty;
    public ImmutableDictionary<EditHit, int> LayerVolumes { get; init; } = ImmutableDictionary<EditHit, int>.Empty;
    public ImmutableList<SoundSet> SoundSets { get; init; } = [];
    public ImmutableList<SoundSetPlacement> SoundPlacements { get; init; } = [];
    /// <summary>Slider body sounds changed in the editor, by object index (see <see cref="SlideSound"/>).</summary>
    public ImmutableDictionary<int, SlideSound> Slides { get; init; } = ImmutableDictionary<int, SlideSound>.Empty;
}

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
public sealed partial class EditSession
{
    /// <summary>How close (ms) a hit must be to a moment to play on it. Slider nodes land on fractional times.</summary>
    public const double AnchorTolerance = 2;

    // Each entry is the state on one side of a step and the step's name: undo holds the state before it, redo the state after.
    private sealed record HistoryStep(EditState State, string Label, Action? Undo = null, Action? Redo = null);
    private readonly List<HistoryStep> undo = [];
    private readonly List<HistoryStep> redo = [];
    private EditState? gestureStart;
    private string gestureLabel = "Edit";
    private readonly double timeOffset; // v3/v4 maps show 24 ms later than written; bookmarks are stored as written
    private MapFolder sampleFolder; // listed again when the folder is rescanned

    public EditSession(OsuFile file, MapFolder folder, DecompiledMap map)
    {
        baseMap = map;
        sampleFolder = folder;
        sourceFile = file;
        var objectIndex = file.HitObjects.Select((o, i) => (o, i)).ToDictionary(x => x.o, x => x.i); // HitObject compares by reference
        baseMoments = [.. map.Moments.Select(m => (m, new MomentKey(objectIndex[m.Object], m.Node)))];
        State = new EditState(
            [.. baseMoments.SelectMany(x => x.Moment.Hits.Select(h => new EditHit(h.Lane, x.Moment.Time, x.Key)))],
            baseMoments.ToImmutableDictionary(x => x.Key, x => x.Moment.Volume),
            [.. file.Bookmarks.Order()]);
        announced = State;
        timeOffset = map.Settings.TimeOffset;
        baseLanes = AllLanes(map, folder);
        HsStartedEmpty = file.HitObjects.Count == 0;
    }

    public DecompiledMap Map => State.HsCircles.Count == 0 && ownedOriginals.Count == 0
        ? State.Slides.Count == 0 ? baseMap : baseMap with { Passive = PassiveNow() }
        : baseMap with { Moments = Moments.Select(m => m.Moment).ToList(), Passive = PassiveNow() };

    public IReadOnlyDictionary<MomentKey, Moment> MomentByKey { get { RefreshMoments(); return currentByKey; } }

    /// <summary>Every moment with its key, in time order.</summary>
    public IReadOnlyList<(Moment Moment, MomentKey Key)> Moments { get { RefreshMoments(); return currentMoments; } }

    /// <summary>The lanes you can draw in: the map's, every set-named sample file in the folder, and all 12 skin sounds.</summary>
    public IReadOnlyList<Lane> Lanes => baseLanes;

    public EditState State { get; private set; }

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    public bool IsGestureActive => gestureStart is not null;

    public bool IsEdited => undo.Any(u => MapChanged(u.State, State)) || gestureStart is { } start && MapChanged(start, State);

    private static bool MapChanged(EditState a, EditState b) =>
        !ReferenceEquals(a.Hits, b.Hits) || !ReferenceEquals(a.Volumes, b.Volumes) || !ReferenceEquals(a.Bookmarks, b.Bookmarks)
        || !ReferenceEquals(a.HsCircles, b.HsCircles) || !ReferenceEquals(a.LayerVolumes, b.LayerVolumes)
        || !ReferenceEquals(a.SoundSets, b.SoundSets) || !ReferenceEquals(a.SoundPlacements, b.SoundPlacements)
        || !ReferenceEquals(a.BookmarkLabels, b.BookmarkLabels) || !ReferenceEquals(a.Slides, b.Slides);

    /// <summary>Seeds the editor-only layout on load or after adding lanes, without creating an edit.</summary>
    public void RestoreLaneOrder(IEnumerable<Lane> lanes)
    {
        State = State with { LaneOrder = [.. lanes] };
        announced = State;
    }

    /// <summary>A visual reorder in the same chronological undo history as note edits.</summary>
    public void SetLaneOrder(IEnumerable<Lane> lanes, string label)
    {
        var next = lanes.Distinct().ToImmutableList();
        Change(label, s => s.LaneOrder.SequenceEqual(next) ? s : s with { LaneOrder = next });
    }

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
        Sorted.Nearest(Moments, time, AnchorTolerance, x => x.Moment.Time,
            x => !State.HsMode || !State.HsCircles.ContainsKey(x.Key) || HsTime(x.Moment.Time) == HsTime(time)) is >= 0 and var i ? Moments[i].Key : null;

    /// <summary>A hit in a lane at a time, placed on the moment there if any.</summary>
    public EditHit HitAt(Lane lane, double time) => MomentAt(time) is { } key
        ? new EditHit(lane, MomentByKey[key].Time, key)
        : State.HsMode ? HsHit(lane, time) : new EditHit(lane, time, null);

    /// <summary>Volume of a moment, or of a hit with no object (100).</summary>
    public int VolumeOf(EditHit hit) => VolumeOf(State, hit);

    internal static int VolumeOf(EditState state, EditHit hit) => state.LayerVolumes.TryGetValue(hit, out int volume)
        ? volume : hit.Anchor is { } key ? state.Volumes.GetValueOrDefault(key, 100) : 100;

    // ---- edits; each call outside a gesture is one undo step ----

    public void Add(IEnumerable<EditHit> hits, string label = "Add", int? volume = null) => Change(label, s =>
    {
        var existing = s.Hits.ToHashSet();
        var added = hits.Where(existing.Add).ToList();
        return added.Count == 0 ? s : s with
        {
            Hits = s.Hits.AddRange(added),
            LayerVolumes = volume is { } v
                ? s.LayerVolumes.SetItems(added.Select(h => KeyValuePair.Create(h, Math.Clamp(v, 0, 100))))
                : s.LayerVolumes,
        };
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

    /// <summary>Changes only the lane, retaining each object's anchor. Companion editor state shares undo/redo.</summary>
    public void RetargetLane(Lane original, Lane replacement, Action? undo = null, Action? redo = null, IReadOnlyList<Lane>? laneOrder = null)
    {
        if (original == replacement || gestureStart is not null) return;
        Change("Replace lane sample", s =>
        {
            var next = PlaceLayers(s, s.Hits.Where(h => h.Lane == original).Select(h =>
                (h with { Lane = replacement }, Volume: s.HsMode || s.LayerVolumes.ContainsKey(h) ? (int?)VolumeOf(s, h) : null)),
                s.Hits.Where(h => h.Lane == original).ToHashSet(), out _);
            if (laneOrder is not null) next = next with { LaneOrder = [.. laneOrder] };
            return ReferenceEquals(next, s) ? s with { } : next; // Empty lanes still change companion layout state.
        }, undo, redo);
    }

    public void ChangeLaneLayout(string label, IReadOnlyList<Lane> order, Action undoLayout, Action redoLayout)
    {
        if (IsGestureActive) return;
        Change(label, s => s with { LaneOrder = [.. order] }, undoLayout, redoLayout);
    }

    /// <summary>Moves each hit to its own lane and time (Quantize), as one undo step. Returns the moved hits.</summary>
    public IReadOnlyList<EditHit> MoveEach(IReadOnlyList<(EditHit Hit, Lane Lane, double Time)> moves, string label)
    {
        var targets = moves.Select(m => HitAt(m.Lane, m.Time) with { Voice = m.Hit.Voice, SetPlacement = m.Hit.SetPlacement, SetMember = m.Hit.SetMember }).ToList();
        if (targets.SequenceEqual(moves.Select(m => m.Hit))) return targets.Distinct().ToList();
        IReadOnlyList<EditHit> moved = [];
        Change(label, s => PlaceLayers(s, moves.Select((m, i) =>
            (targets[i],
             Volume: s.HsMode || s.LayerVolumes.ContainsKey(m.Hit) ? (int?)VolumeOf(s, m.Hit) : null)),
            moves.Select(m => m.Hit).ToHashSet(), out moved));
        return moved;
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
        var originals = hits.ToList();
        IReadOnlyList<EditHit> copies = [];
        var whole = State.SoundPlacements.Where(p => State.Hits.Where(h => h.SetPlacement == p.Id).Any() && State.Hits.Where(h => h.SetPlacement == p.Id).All(originals.Contains))
            .ToDictionary(p => p.Id, p => p with { Id = Guid.NewGuid(), Time = p.Time + offset, Overrides = p.Overrides.Where(o => !o.Value.Shared).ToImmutableDictionary() });
        Change(label, s =>
        {
            var next = PlaceLayers(s, originals.Select(h =>
                (HitAt(h.Lane, h.Time + offset) with { Voice = h.Voice, SetPlacement = whole.GetValueOrDefault(h.SetPlacement)?.Id ?? Guid.Empty, SetMember = whole.ContainsKey(h.SetPlacement) ? h.SetMember : Guid.Empty },
                 Volume: s.HsMode || s.LayerVolumes.ContainsKey(h) ? (int?)VolumeOf(s, h) : null)), [], out copies);
            return whole.Count == 0 ? next : RenderSoundSets(next with { SoundPlacements = next.SoundPlacements.AddRange(whole.Values) });
        });
        return whole.Count == 0 ? copies : copies.Where(h => h.SetPlacement == Guid.Empty).Concat(State.Hits.Where(h => whole.Values.Any(p => p.Id == h.SetPlacement))).Distinct().ToList();
    }

    public void SetVolume(IEnumerable<MomentKey> keys, int volume, string label = "Volume")
    {
        int v = Math.Clamp(volume, 0, 100);
        SetVolumes(keys.Distinct().ToDictionary(k => k, _ => v), label);
    }

    /// <summary>Puts back hits from a saved project (hits with no object): not an edit, so not an undo step.</summary>
    public void Restore(IEnumerable<EditHit> hits, IReadOnlyDictionary<EditHit, int>? layerVolumes = null)
    {
        var existing = State.Hits.ToHashSet();
        var added = hits.Where(existing.Add).ToList();
        if (added.Count == 0) return;
        State = MaintainHsCircles(State with { Hits = State.Hits.AddRange(added),
            LayerVolumes = layerVolumes is null ? State.LayerVolumes : State.LayerVolumes.SetItems(layerVolumes) });
        Announce();
    }

    /// <summary>Sets several moments' volumes at once (ramps, paint strokes, relative edits); values are kept within 0–100.</summary>
    public void SetVolumes(IReadOnlyDictionary<MomentKey, int> volumes, string label = "Volume") => Change(label, s =>
    {
        // Only the values that differ: a drag that stays on the same value is not an edit (no redraw, no recompile).
        var changed = volumes.Select(kv => new KeyValuePair<MomentKey, int>(kv.Key, Math.Clamp(kv.Value, 0, 100)))
            .Where(kv => !s.Volumes.TryGetValue(kv.Key, out int old) || old != kv.Value).ToList();
        var layers = s.LayerVolumes.RemoveRange(s.LayerVolumes.Keys.Where(h => h.Anchor is { } key && volumes.ContainsKey(key)));
        return changed.Count == 0 && ReferenceEquals(layers, s.LayerVolumes) ? s
            : s with { Volumes = s.Volumes.SetItems(changed), LayerVolumes = layers };
    });

    /// <summary>Changes individual layers without changing other sounds on the same object.</summary>
    public void SetLayerVolumes(IReadOnlyDictionary<EditHit, int> volumes, string label = "Lane volumes") => Change(label, s =>
    {
        var existing = s.Hits.ToHashSet();
        var changed = volumes.Where(kv => existing.Contains(kv.Key))
            .Select(kv => KeyValuePair.Create(kv.Key, Math.Clamp(kv.Value, 0, 100)))
            .Where(kv => VolumeOf(s, kv.Key) != kv.Value).ToList();
        return changed.Count == 0 ? s : s with { LayerVolumes = s.LayerVolumes.SetItems(changed) };
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

    public void CancelGesture()
    {
        if (gestureStart is not { } start) return;
        State = start; gestureStart = null; Announce();
    }

    public void EndGesture()
    {
        if (gestureStart is { } start && !ReferenceEquals(start, State))
        {
            undo.Add(new(start, gestureLabel));
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
        var hits = state.Hits.Where(h => h.Anchor is not null).ToLookup(h => h.Anchor!.Value);
        return MomentsFor(state).ToDictionary(x => x.Key, x => new WantedSound([.. hits[x.Key].Select(h => h.Lane)],
            hits[x.Key].FirstOrDefault() is { } first ? VolumeOf(state, first) : state.Volumes.GetValueOrDefault(x.Key, 100)));
    }


    /// <summary>Bookmarks on the timeline (shown times), sorted.</summary>
    public IEnumerable<double> BookmarkTimes => State.Bookmarks.Select(b => b + timeOffset);

    public string BookmarkLabel(double time) => State.BookmarkLabels.GetValueOrDefault((int)Math.Round(time - timeOffset), "");

    public void RestoreBookmarkLabels(IReadOnlyDictionary<int, string>? labels)
    {
        State = State with { BookmarkLabels = (labels ?? new Dictionary<int, string>())
            .Where(kv => State.Bookmarks.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToImmutableDictionary(kv => kv.Key, kv => kv.Value.Trim()[..Math.Min(kv.Value.Trim().Length, 120)]) };
        announced = State;
    }

    public void RenameBookmark(double time, string label) => Change("Rename bookmark", s =>
    {
        int ms = (int)Math.Round(time - timeOffset);
        string name = label.Trim();
        name = name[..Math.Min(name.Length, 120)];
        if (!s.Bookmarks.Contains(ms) || s.BookmarkLabels.GetValueOrDefault(ms, "") == name) return s;
        return s with { BookmarkLabels = name.Length == 0 ? s.BookmarkLabels.Remove(ms) : s.BookmarkLabels.SetItem(ms, name) };
    });

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
        NearestBookmark(time, tolerance) is { } b ? s with { Bookmarks = s.Bookmarks.Remove((int)Math.Round(b - timeOffset)),
            BookmarkLabels = s.BookmarkLabels.Remove((int)Math.Round(b - timeOffset)) } : s);
    /// <summary>Hits that have no object to play on.</summary>
    public IEnumerable<EditHit> Unanchored => State.Hits.Where(h => h.Anchor is null);

    private void StepBack()
    {
        var step = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        redo.Add(step with { State = State });
        State = step.State;
        step.Undo?.Invoke();
    }

    private void StepForward()
    {
        var step = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        undo.Add(step with { State = State });
        State = step.State;
        step.Redo?.Invoke();
    }

    private void Change(string label, Func<EditState, EditState> edit, Action? undoCompanion = null, Action? redoCompanion = null)
    {
        var next = edit(State);
        if (!ReferenceEquals(next, State))
        {
            next = MaintainHsCircles(ConformIndexes(State, next));
            if (ReferenceEquals(next.SoundPlacements, State.SoundPlacements) && ReferenceEquals(next.SoundSets, State.SoundSets)) next = ReconcileSoundSets(State, next);
            if (next.LayerVolumes.Count > 0)
            {
                var present = next.Hits.ToHashSet();
                next = next with { LayerVolumes = next.LayerVolumes.RemoveRange(next.LayerVolumes.Keys.Where(h => !present.Contains(h))) };
            }
        }
        if (!ReferenceEquals(next, State))
        {
            if (gestureStart is null)
            {
                undo.Add(new(State, label, undoCompanion, redoCompanion));
                redo.Clear();
            }

            State = next;
            redoCompanion?.Invoke();
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

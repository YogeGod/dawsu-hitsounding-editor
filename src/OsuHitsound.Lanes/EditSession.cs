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

/// <summary>
/// Hitsound editing on top of a decompiled map: the hits you want, with undo and redo. Moments are fixed (Apply mode):
/// a hit placed within <see cref="AnchorTolerance"/> of a moment plays on it; elsewhere it is a hit with no object.
/// <see cref="Wanted"/> turns the state into the compiler's input.
/// </summary>
public sealed class EditSession
{
    /// <summary>How close (ms) a hit must be to a moment to play on it. Slider nodes land on fractional times.</summary>
    public const double AnchorTolerance = 2;

    private readonly List<EditState> undo = [];
    private readonly List<EditState> redo = [];
    private EditState? gestureStart;
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

    public void Add(IEnumerable<EditHit> hits) => Change(s =>
    {
        var existing = s.Hits.ToHashSet();
        var added = hits.Where(existing.Add).ToList();
        return added.Count == 0 ? s : s with { Hits = s.Hits.AddRange(added) };
    });

    public void Remove(IEnumerable<EditHit> hits) => Change(s =>
    {
        var gone = hits.ToHashSet();
        return s with { Hits = s.Hits.RemoveAll(gone.Contains) };
    });

    /// <summary>
    /// Moves hits by a time offset and to other lanes (laneOf maps each hit to its new lane). Each lands on the moment
    /// at its new time, or becomes a hit with no object. Returns the moved hits, to keep them selected.
    /// </summary>
    public IReadOnlyList<EditHit> Move(IReadOnlyCollection<EditHit> hits, double offset, Func<EditHit, Lane> laneOf)
    {
        var moved = hits.Select(h => HitAt(laneOf(h), h.Time + offset)).ToList();
        Change(s =>
        {
            var gone = hits.ToHashSet();
            var kept = s.Hits.RemoveAll(gone.Contains);
            var present = kept.ToHashSet();
            return s with { Hits = kept.AddRange(moved.Where(present.Add)) };
        });
        return moved.Distinct().ToList();
    }

    public void SetVolume(IEnumerable<MomentKey> keys, int volume) => Change(s =>
    {
        int v = Math.Clamp(volume, 0, 100);
        return s with { Volumes = s.Volumes.SetItems(keys.Select(k => new KeyValuePair<MomentKey, int>(k, v))) };
    });

    /// <summary>Sets several moments' volumes at once (ramps, paint strokes, relative edits); values are kept within 0–100.</summary>
    public void SetVolumes(IReadOnlyDictionary<MomentKey, int> volumes) => Change(s => volumes.Count == 0 ? s : s with
    {
        Volumes = s.Volumes.SetItems(volumes.Select(kv => new KeyValuePair<MomentKey, int>(kv.Key, Math.Clamp(kv.Value, 0, 100)))),
    });

    /// <summary>Starts a drag or stroke: the changes until <see cref="EndGesture"/> become one undo step.</summary>
    public void BeginGesture() => gestureStart ??= State;

    /// <summary>During a drag: go back to where the gesture began, so the next edit applies the drag's total offset.</summary>
    public void ResetToGestureStart()
    {
        if (gestureStart is not null) State = gestureStart;
    }

    public void EndGesture()
    {
        if (gestureStart is { } start && !ReferenceEquals(start, State))
        {
            undo.Add(start);
            redo.Clear();
        }

        gestureStart = null;
    }

    /// <summary>Steps back one edit. Ignored during a drag or stroke, whose later moves would act on hits it removed.</summary>
    public void Undo()
    {
        if (gestureStart is not null || undo.Count == 0) return;
        redo.Add(State);
        State = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (gestureStart is not null || redo.Count == 0) return;
        undo.Add(State);
        State = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        Changed?.Invoke();
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
    public void AddBookmark(double time) => Change(s =>
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
    public void RemoveBookmarkNear(double time, double tolerance) => Change(s =>
        NearestBookmark(time, tolerance) is { } b ? s with { Bookmarks = s.Bookmarks.Remove((int)Math.Round(b - timeOffset)) } : s);
    /// <summary>Hits that have no object to play on.</summary>
    public IEnumerable<EditHit> Unanchored => State.Hits.Where(h => h.Anchor is null);

    private void Change(Func<EditState, EditState> edit)
    {
        var next = edit(State);
        if (ReferenceEquals(next, State)) return;
        if (gestureStart is null)
        {
            undo.Add(State);
            redo.Clear();
        }

        State = next;
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

using System.Collections.Immutable;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

public sealed partial class EditSession
{
    // Set once the history came from the session before a save: the state that matches the file on disk. Until then
    // "edited" means "differs from a state in the history", as before.
    private EditState? cleanState;

    /// <summary>
    /// After Save reloaded the map (issue #39): takes the previous session's undo and redo steps, re-keyed onto this
    /// map's objects. A real object is found by its time, position and type; a circle that only the editor had (HS mode)
    /// by its time, as the circle Save wrote. Undoing past the save gives those edits back as unsaved changes, and the
    /// state reloaded from disk is the clean one. A step whose objects can't be found here ends the history there: it
    /// and every step beyond it (older undo steps, later redo steps) are dropped. The lane panel's own undo actions are
    /// not carried; lane order comes back with each state.
    /// </summary>
    /// <returns>How many steps were dropped.</returns>
    public int AdoptHistory(EditSession previous)
    {
        if (gestureStart is not null || previous.gestureStart is not null) return previous.undo.Count + previous.redo.Count;
        var objects = MatchObjects(previous.sourceFile, sourceFile);
        // This map's editor-owned circles (HS mode) by time: where an unsaved circle of the old session now is.
        var circles = ownedOriginals.Select(k => (Key: k, Time: State.HsCircles.GetValueOrDefault(k, double.NaN))).Where(c => !double.IsNaN(c.Time)).ToList();

        MomentKey? KeyOf(MomentKey old, EditState state)
        {
            if (old.ObjectIndex >= 0)
                return objects.TryGetValue(old.ObjectIndex, out int index) ? new MomentKey(index, old.Node) : null;
            if (!state.HsCircles.TryGetValue(old, out double time)) return null;
            var match = circles.Where(c => Math.Abs(c.Time - time) < 0.5).ToList();
            return match.Count == 1 ? match[0].Key : null;
        }

        var keptUndo = new List<HistoryStep>();
        int dropped = 0;
        for (int i = previous.undo.Count - 1; i >= 0; i--)
        {
            if (Remap(previous.undo[i].State, KeyOf) is not { } state) { dropped += i + 1; break; }
            keptUndo.Add(new HistoryStep(state, previous.undo[i].Label));
        }

        keptUndo.Reverse();
        var keptRedo = new List<HistoryStep>();
        for (int i = previous.redo.Count - 1; i >= 0; i--)
        {
            if (Remap(previous.redo[i].State, KeyOf) is not { } state) { dropped += i + 1; break; }
            keptRedo.Add(new HistoryStep(state, previous.redo[i].Label));
        }

        keptRedo.Reverse();
        undo.Clear(); undo.AddRange(keptUndo);
        redo.Clear(); redo.AddRange(keptRedo);
        cleanState = State;
        Changed?.Invoke();
        return dropped;
    }

    // Old object index → this file's, by time, position and type (new combo and colour skips ignored); objects at the
    // same place pair up in file order.
    private static Dictionary<int, int> MatchObjects(OsuFile before, OsuFile after)
    {
        static (string, double, double, int)? Identity(HitObject o)
        {
            try { return (o.Time.ToString("R"), o.X, o.Y, o.Type & ~(4 | 112)); }
            catch (FormatException) { return null; }
        }

        var free = new Dictionary<(string, double, double, int), Queue<int>>();
        for (int i = 0; i < after.HitObjects.Count; i++)
            if (Identity(after.HitObjects[i]) is { } id)
            {
                if (!free.TryGetValue(id, out var queue)) free[id] = queue = new();
                queue.Enqueue(i);
            }

        var map = new Dictionary<int, int>();
        for (int i = 0; i < before.HitObjects.Count; i++)
            if (Identity(before.HitObjects[i]) is { } id && free.TryGetValue(id, out var queue) && queue.Count > 0)
                map[i] = queue.Dequeue();
        return map;
    }

    // A history state with every object key re-keyed; null when one can't be.
    private static EditState? Remap(EditState state, Func<MomentKey, EditState, MomentKey?> keyOf)
    {
        var keys = new Dictionary<MomentKey, MomentKey>();
        bool Map(MomentKey old, out MomentKey now)
        {
            if (keys.TryGetValue(old, out now)) return true;
            if (keyOf(old, state) is not { } found) return false;
            keys[old] = now = found;
            return true;
        }

        EditHit? Hit(EditHit hit) =>
            hit.Anchor is not { } anchor ? hit : Map(anchor, out var now) ? hit with { Anchor = now } : null;

        var hits = ImmutableList.CreateBuilder<EditHit>();
        foreach (var hit in state.Hits)
            if (Hit(hit) is { } moved) hits.Add(moved); else return null;
        var volumes = ImmutableDictionary.CreateBuilder<MomentKey, int>();
        foreach (var (key, volume) in state.Volumes)
            if (Map(key, out var now)) volumes[now] = volume; else return null;
        var circles = ImmutableDictionary.CreateBuilder<MomentKey, double>();
        foreach (var (key, time) in state.HsCircles)
            if (Map(key, out var now)) circles[now] = time; else return null;
        var layers = ImmutableDictionary.CreateBuilder<EditHit, int>();
        foreach (var (hit, volume) in state.LayerVolumes)
            if (Hit(hit) is { } moved) layers[moved] = volume; else return null;
        var slides = ImmutableDictionary.CreateBuilder<int, SlideSound>();
        foreach (var (index, slide) in state.Slides)
            if (Map(new MomentKey(index, 0), out var now)) slides[now.ObjectIndex] = slide; else return null;

        return state with
        {
            Hits = hits.ToImmutable(), Volumes = volumes.ToImmutable(), HsCircles = circles.ToImmutable(),
            LayerVolumes = layers.ToImmutable(), Slides = slides.ToImmutable(),
        };
    }
}

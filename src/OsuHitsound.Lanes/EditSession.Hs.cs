using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

public sealed partial class EditSession
{
    private readonly OsuFile sourceFile;
    private readonly DecompiledMap baseMap;
    private readonly IReadOnlyList<(Moment Moment, MomentKey Key)> baseMoments;
    private readonly IReadOnlyList<Lane> baseLanes;
    private readonly HashSet<MomentKey> ownedOriginals = [];
    private bool restoredHsMoments;
    private readonly Dictionary<int, MomentKey> virtualKeys = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<MomentKey, HitObject> virtualObjects = new();
    private EditState? momentsFrom;
    private IReadOnlyList<(Moment Moment, MomentKey Key)> currentMoments = [];
    private IReadOnlyDictionary<MomentKey, Moment> currentByKey = new Dictionary<MomentKey, Moment>();

    public bool HsStartedEmpty { get; private set; }
    public bool HsEligible => baseMap.Settings.Mode == 0 && (HsStartedEmpty || IsHsName(sourceFile.Lines.FirstOrDefault(l =>
        l.Section == "Metadata" && l.Text.TrimStart().StartsWith("Version:", StringComparison.Ordinal))?.Text.TrimStart()[8..] ?? ""));
    public bool HasHsObjects => State.HsCircles.Count > 0 || ownedOriginals.Count > 0 || restoredHsMoments
        || State.LayerVolumes.Count > 0 || State.Hits.Any(h => h.Voice != 0);

    public static bool IsHsName(string name) => Regex.IsMatch(name, @"(?<![\p{L}\p{N}])(?:hs|hitsounds)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public bool SetHsMode(bool enabled)
    {
        if (IsGestureActive || enabled && !HsEligible) return false;
        Change(enabled ? "Enable HS mode" : "Disable HS mode", s => s.HsMode == enabled ? s : s with { HsMode = enabled });
        return true;
    }

    private int HsTime(double time)
    {
        double rounded = Math.Round(time - timeOffset);
        if (!double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(time), "Circle time must fit osu!'s whole-millisecond range.");
        return (int)rounded;
    }

    private EditHit HsHit(Lane lane, double time)
    {
        int at = HsTime(time);
        if (!virtualKeys.TryGetValue(at, out var key))
            virtualKeys[at] = key = new MomentKey(checked(-1 - virtualKeys.Count), 0);
        return new(lane, at + timeOffset, key);
    }

    private EditState MaintainHsCircles(EditState state)
    {
        if (!state.HsMode) return state;
        var circles = state.HsCircles.ToBuilder();
        var volumes = state.Volumes.ToBuilder();
        var layers = state.LayerVolumes.ToBuilder();
        var byTime = circles.GroupBy(c => HsTime(c.Value)).ToDictionary(g => g.Key, g => g.First().Key);
        var hits = state.Hits.Select(h =>
        {
            var input = h;
            if (h.Anchor is null)
            {
                int nearest = Sorted.Nearest(baseMoments, h.Time, AnchorTolerance, m => m.Moment.Time, m => !ownedOriginals.Contains(m.Key));
                if (nearest >= 0) h = h with { Time = baseMoments[nearest].Moment.Time, Anchor = baseMoments[nearest].Key };
                else if (byTime.TryGetValue(HsTime(h.Time), out var existing)) h = h with { Time = circles[existing], Anchor = existing };
                else h = HsHit(h.Lane, h.Time) with { Voice = h.Voice };
            }
            if (h.Anchor is { ObjectIndex: < 0 } key)
            {
                circles[key] = h.Time;
                byTime[HsTime(h.Time)] = key;
                if (!volumes.ContainsKey(key)) volumes[key] = VolumeOf(state, input);
            }
            if (input != h && layers.TryGetValue(input, out int volume))
            { layers.Remove(input); layers[h] = volume; }
            return h;
        }).Distinct().ToImmutableList();
        var used = hits.Where(h => h.Anchor is not null).Select(h => h.Anchor!.Value).ToHashSet();
        foreach (var key in circles.Keys.Where(k => !used.Contains(k)).ToList())
        { circles.Remove(key); volumes.Remove(key); }
        return state with { Hits = state.Hits.SequenceEqual(hits) ? state.Hits : hits,
            HsCircles = circles.ToImmutable(), Volumes = volumes.ToImmutable(), LayerVolumes = layers.ToImmutable() };
    }

    internal IReadOnlyList<(Moment Moment, MomentKey Key)> MomentsFor(EditState state)
    {
        if (state.HsCircles.Count == 0 && ownedOriginals.Count == 0) return baseMoments;
        var hits = state.Hits.Where(h => h.Anchor is not null).ToLookup(h => h.Anchor!.Value, h => new Hit(h.Lane));
        return baseMoments.Where(m => !ownedOriginals.Contains(m.Key)).Concat(state.HsCircles.Select(c =>
        {
            var obj = c.Key.ObjectIndex >= 0 ? sourceFile.HitObjects[c.Key.ObjectIndex] : virtualObjects.GetOrAdd(c.Key, _ =>
                OsuFile.Parse(Encoding.UTF8.GetBytes($"[HitObjects]\n256,192,{HsTime(c.Value)},1,0,0:0:0:0:\n")).HitObjects[0]);
            return (new Moment(c.Value, obj, 0, HitPosition.Circle, state.Volumes.GetValueOrDefault(c.Key, 100), hits[c.Key].ToList(), null), c.Key);
        })).OrderBy(m => m.Item1.Time).ToList();
    }

    private void RefreshMoments()
    {
        if (ReferenceEquals(momentsFrom, State)) return;
        currentMoments = MomentsFor(State);
        currentByKey = currentMoments.ToDictionary(m => m.Key, m => m.Moment);
        momentsFrom = State;
    }

    /// <summary>Adopts only circles from a sidecar that matches the exact saved file. External edits revoke ownership.</summary>
    public void RestoreHsProject(HsProject? project, string fileHash)
    {
        HsStartedEmpty |= project?.StartedEmpty == true;
        bool valid = project?.FileHash == fileHash && project?.Moments is not null;
        var circles = ImmutableDictionary.CreateBuilder<MomentKey, double>();
        var originals = baseMoments.ToDictionary(m => m.Key, m => m.Moment);
        var logicalHits = new Dictionary<MomentKey, List<EditHit>>();
        var logicalVolumes = new Dictionary<MomentKey, int>();
        var layerVolumes = ImmutableDictionary.CreateBuilder<EditHit, int>();
        ownedOriginals.Clear();
        if (valid)
            foreach (var saved in project!.Moments)
            {
                if (saved is null || saved.Hits is null) continue;
                var key = new MomentKey(saved.ObjectIndex, saved.Node);
                if (!originals.TryGetValue(key, out var original) || logicalHits.ContainsKey(key)
                    || !double.IsFinite(saved.Time) || Math.Abs(original.Time - timeOffset - saved.Time) > 1e-6) continue;
                var obj = original.Object;
                if (saved.Owned && (saved.Node != 0 || obj.Kind != HitObjectKind.Circle || obj.X != 256 || obj.Y != 192)) continue;
                var logical = saved.Hits.Select(key => key is null ? null : ProjectStore.LaneOf(key)).ToList();
                if (logical.Any(l => l is null || !Enum.IsDefined(l.Source) || l.Source == LaneSource.StoryboardFile || string.IsNullOrWhiteSpace(l.Name))
                    || saved.Volume is < 0 or > 100 || saved.LayerVolumes is { } lv && (lv.Count != logical.Count || lv.Any(v => v is < 0 or > 100))) continue;
                if (saved.Owned) { ownedOriginals.Add(key); circles[key] = original.Time; }
                var seen = new HashSet<EditHit>();
                logicalHits[key] = logical.Select((l, i) =>
                {
                    var hit = new EditHit(l!, original.Time, key);
                    while (!seen.Add(hit)) hit = hit with { Voice = checked(++nextVoice) };
                    if (saved.LayerVolumes is { } values) layerVolumes[hit] = values[i];
                    return hit;
                }).ToList();
                logicalVolumes[key] = saved.Volume;
            }
        restoredHsMoments = logicalHits.Count > 0;
        var hits = State.Hits.Where(h => h.Anchor is null || !logicalHits.ContainsKey(h.Anchor.Value))
            .Concat(logicalHits.Values.SelectMany(h => h)).Distinct().ToImmutableList();
        var volumes = State.Volumes.SetItems(logicalVolumes);
        State = State with { Hits = hits, Volumes = volumes, LayerVolumes = layerVolumes.ToImmutable(), HsCircles = circles.ToImmutable(), HsMode = project?.Enabled == true && HsEligible };
        announced = State;
        momentsFrom = null;
    }

    internal HsProjection ProjectHs(EditState state, byte[] opened)
    {
        var copy = OsuFile.Parse(opened);
        var originalObjects = copy.HitObjects.ToList();
        foreach (var key in ownedOriginals.Where(k => !state.HsCircles.ContainsKey(k))) copy.RemoveHitObject(originalObjects[key.ObjectIndex]);
        var generated = new Dictionary<MomentKey, HitObject>();
        foreach (var circle in state.HsCircles.Where(c => c.Key.ObjectIndex < 0).OrderBy(c => c.Value))
            generated[circle.Key] = copy.InsertHitCircle(HsTime(circle.Value));
        var indexes = copy.HitObjects.Select((obj, index) => (obj, index)).ToDictionary(p => p.obj, p => p.index);
        var keys = new Dictionary<MomentKey, MomentKey>();
        foreach (var m in MomentsFor(state))
        {
            var obj = m.Key.ObjectIndex >= 0 ? originalObjects[m.Key.ObjectIndex] : generated[m.Key];
            keys[m.Key] = new(indexes[obj], m.Key.Node);
        }
        var wanted = Wanted(state).ToDictionary(p => keys[p.Key], p => p.Value);
        return new(copy, wanted, keys);
    }
}

internal sealed record HsProjection(OsuFile File, Dictionary<MomentKey, WantedSound> Wanted, Dictionary<MomentKey, MomentKey> Keys);

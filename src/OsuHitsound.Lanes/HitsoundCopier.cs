using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>Which target sounds Copy hitsounds replaces (Mapping Tools' copy modes).</summary>
public enum CopyMode
{
    /// <summary>Clears the target's hitsounds first: every hit point plays what the source has at that time.</summary>
    OverwriteEverything,

    /// <summary>Changes only the hit points the source has a hit for; everything else keeps playing what it played.</summary>
    OverwriteOnlyDefined,
}

/// <summary>
/// The settings of Copy hitsounds, after Mapping Tools 2.0's Hitsound Copier (OliBomby, MIT): the same options with the
/// same defaults.
/// </summary>
public sealed record HitsoundCopyOptions
{
    public CopyMode Mode { get; init; } = CopyMode.OverwriteEverything;

    /// <summary>How far apart (ms, after rounding both times) a source and a target hit point may be to match.</summary>
    public double Leniency { get; init; } = 5;

    /// <summary>Copy what each hit point plays (the sounds and their sample sets).</summary>
    public bool CopyHitsounds { get; init; } = true;

    /// <summary>Copy sample sets and custom indexes (otherwise the target's are kept).</summary>
    public bool CopySampleSets { get; init; } = true;

    /// <summary>Copy volumes (otherwise the target's are kept).</summary>
    public bool CopyVolumes { get; init; } = true;

    /// <summary>With volumes copied, target hit points at 5 % stay at 5 % (they're muted on purpose).</summary>
    public bool AlwaysPreserve5Volume { get; init; } = true;

    /// <summary>Mute target slider (and spinner) ends the source has nothing for, filtered by snap and length.</summary>
    public bool MuteSliderEnds { get; init; }

    /// <summary>Beat divisors in priority order: an end snaps to the nearest, and the first it falls on decides.</summary>
    public IReadOnlyList<int> BeatDivisors { get; init; } = [1, 4, 3, 8, 6, 16, 12];

    /// <summary>The divisors whose ends are muted.</summary>
    public IReadOnlyList<int> MutedDivisors { get; init; } = [4, 3, 8, 6, 16, 12];

    /// <summary>Only sliders at least this long (in beats) get their end muted.</summary>
    public double MinLength { get; init; } = 0.5;

    /// <summary>The custom index muted ends play from; -1 keeps theirs.</summary>
    public int MutedIndex { get; init; } = -1;

    /// <summary>The sample set muted ends play in; None keeps theirs.</summary>
    public SampleSet MutedSampleSet { get; init; } = SampleSet.None;
}

/// <summary>What <see cref="HitsoundCopier.Plan"/> found: the target's new sounds per hit point, and counts for the summary.</summary>
public sealed record HitsoundCopyPlan(IReadOnlyDictionary<MomentKey, WantedSound> Wanted, int Matched, int Changed, int Muted = 0);

/// <summary>One target of <see cref="HitsoundCopier.CopyTo"/>: what changed, the backup, or why it wasn't written.</summary>
public sealed record HitsoundCopyTarget(string Path, int Matched, int Changed, int Conflicts, string? Backup, string? Error, int Muted = 0);

/// <summary>
/// Copy hitsounds: Mapping Tools' Hitsound Copier on the editor's lanes. Each source hit point goes to the nearest
/// target hit point within the leniency (both times rounded to the millisecond, as MT does; when two source points
/// pick the same target point, the later one wins). The result is a set of wanted sounds that the editor's own
/// compiler writes, so only the lines that change are rewritten.
/// </summary>
public static class HitsoundCopier
{
    private static readonly string[] HitSoundNames = ["hitnormal", "hitwhistle", "hitfinish", "hitclap"];

    public static HitsoundCopyPlan Plan(OsuFile source, MapFolder sourceFolder, OsuFile target, MapFolder targetFolder, HitsoundCopyOptions options)
    {
        if (!double.IsFinite(options.Leniency) || options.Leniency < 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.Leniency, "The leniency must be a number of milliseconds, 0 or more.");

        var from = Decompiler.Decompile(source, sourceFolder);
        var to = Decompiler.Decompile(target, targetFolder);
        var sourceTiming = new TimingPointIndex(source.TimingPoints);
        var targetTiming = new TimingPointIndex(target.TimingPoints);
        var objectIndex = target.HitObjects.Select((o, i) => (o, i)).ToDictionary(x => x.o, x => x.i); // by reference

        // Target hit points by their own time (without the old-format offset), for the nearest-point search.
        var targets = to.Moments.OrderBy(m => m.Time).ToArray();
        var times = targets.Select(m => m.Time - to.Settings.TimeOffset).ToArray();
        var matched = new Dictionary<int, Moment>();
        int matches = 0;
        if (options.CopyHitsounds)
            foreach (var s in from.Moments)
            {
                double time = s.Time - from.Settings.TimeOffset;
                if (Nearest(times, time) is { } i && Math.Abs(Math.Round(time) - Math.Round(times[i])) <= options.Leniency)
                {
                    matched[i] = s;
                    matches++;
                }
            }

        var wanted = new Dictionary<MomentKey, WantedSound>();
        for (int i = 0; i < targets.Length; i++)
        {
            var m = targets[i];
            double time = times[i];
            var targetState = Decompiler.PointState(targetTiming.SamplePointAt(time), to.Settings);
            var sourceState = Decompiler.PointState(sourceTiming.SamplePointAt(time), from.Settings);

            IReadOnlyList<Lane> lanes;
            int volume;
            if (matched.TryGetValue(i, out var s))
            {
                lanes = options.CopySampleSets ? [.. s.Hits.Select(h => h.Lane)] : Retarget(s.Hits.Select(h => h.Lane), targetState.Set, targetState.Index, targetFolder);
                volume = options.CopyVolumes ? s.Volume : m.Volume;
            }
            else if (options.Mode == CopyMode.OverwriteEverything)
            {
                // The target's hitsounds were cleared: a hit point plays a hitnormal in the sample set, index and volume
                // the source has at that time (its green lines are copied over the whole map).
                var (set, index) = options.CopySampleSets ? (sourceState.Set, sourceState.Index) : (targetState.Set, targetState.Index);
                lanes = options.CopyHitsounds
                    ? [Decompiler.ResolveLane(targetFolder, set, "hitnormal", index)]
                    : options.CopySampleSets ? Retarget(m.Hits.Select(h => h.Lane), set, index, targetFolder) : [.. m.Hits.Select(h => h.Lane)];
                volume = options.CopyVolumes ? sourceState.Volume : m.Volume;
            }
            else
            {
                continue; // only what the source defines changes
            }

            if (options.CopyVolumes && options.AlwaysPreserve5Volume && m.Volume == 5) volume = 5;
            var wish = new WantedSound(lanes, volume);
            if (!Same(wish, m)) wanted[new MomentKey(objectIndex[m.Object], m.Node)] = wish;
        }

        int muted = 0;
        if (options.MuteSliderEnds)
            for (int i = 0; i < targets.Length; i++)
            {
                var m = targets[i];
                var key = new MomentKey(objectIndex[m.Object], m.Node);
                var now = wanted.GetValueOrDefault(key) ?? Compiler.AsWanted(m);
                if (matched.ContainsKey(i) || !IsMutable(m, now, times[i], targetTiming, options)) continue;

                // A hitnormal at 5 %, in the muted sample set and index when they're set.
                var normal = now.Lanes.Select(l => l.Sample).FirstOrDefault(s => s?.Sound == "hitnormal");
                var state = Decompiler.PointState(targetTiming.SamplePointAt(times[i]), to.Settings);
                var set = options.MutedSampleSet is > SampleSet.None and <= SampleSet.Drum ? options.MutedSampleSet : normal?.Set ?? state.Set;
                int index = options.MutedIndex >= 0 ? options.MutedIndex : normal?.Index ?? state.Index;
                var mute = new WantedSound([Decompiler.ResolveLane(targetFolder, set, "hitnormal", index)], 5);
                muted++;
                if (Same(mute, m)) wanted.Remove(key);
                else wanted[key] = mute;
            }

        return new HitsoundCopyPlan(wanted, matches, wanted.Count, muted);
    }

    /// <summary>
    /// Mapping Tools' slider-end filter: the end of a slider without repeats (or a spinner's end), with no additions, at
    /// least the minimum length, whose time snaps first to one of the muted divisors.
    /// </summary>
    private static bool IsMutable(Moment m, WantedSound now, double time, TimingPointIndex timing, HitsoundCopyOptions options)
    {
        bool end = m.Position == HitPosition.SliderTail && m.Node == 1 || m.Position == HitPosition.SpinnerEnd;
        if (!end || now.Lanes.Any(l => l.Sample?.Sound is "hitwhistle" or "hitfinish" or "hitclap")) return false;
        if (timing.RedLineAt(time - 1) is not { BeatLength: > 0 and var beat } red) return false;
        if (time - m.Object.Time < options.MinLength * beat - 1e-6) return false;

        int[] all = [.. options.BeatDivisors.Concat(options.MutedDivisors).Where(d => d > 0)];
        if (all.Length == 0) return false;
        // Snap to the nearest grid line of any divisor (the first divisor wins a tie), then see which divisors it is on.
        double snapped = all.Select(d => red.Time + Math.Round((time - red.Time) / (beat / d)) * (beat / d))
            .OrderBy(t => Math.Abs(t - time)).First();
        double beats = (snapped - red.Time) / beat;
        var first = all.FirstOrDefault(d => OnDivisor(beats, d));
        return first != 0 && options.MutedDivisors.Contains(first);
    }

    private static bool OnDivisor(double beats, int divisor)
    {
        double steps = beats * divisor;
        return Math.Abs(steps - Math.Round(steps)) < 1e-6;
    }

    /// <summary>
    /// Copies into each target .osu and saves it like Save .osu: refused when it changed on disk, with a backup outside
    /// the mapset, and only when something changes. A target that can't be read or written is reported, not thrown.
    /// </summary>
    public static IReadOnlyList<HitsoundCopyTarget> CopyTo(OsuFile source, MapFolder sourceFolder, IEnumerable<string> targetPaths, HitsoundCopyOptions options, string backupRoot)
    {
        var results = new List<HitsoundCopyTarget>();
        foreach (var path in targetPaths)
        {
            try
            {
                var doc = MapDocument.Load(path);
                var plan = Plan(source, sourceFolder, doc.File, doc.Folder, options);
                var saved = MapSaver.Save(doc.Path, doc.Opened, doc.Folder, plan.Wanted, backupRoot);
                results.Add(new HitsoundCopyTarget(doc.Path, plan.Matched, plan.Changed, saved.Compile.Conflicts.Count, saved.Backup, null, plan.Muted));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
            {
                results.Add(new HitsoundCopyTarget(path, 0, 0, 0, null, ex.Message));
            }
        }

        return results;
    }

    // The index of the time nearest to `time` (the earlier one on a tie), or null when there are none.
    private static int? Nearest(double[] times, double time)
    {
        if (times.Length == 0) return null;
        int i = Array.BinarySearch(times, time);
        if (i >= 0) return i;
        i = ~i;
        if (i == 0) return 0;
        if (i == times.Length) return times.Length - 1;
        return time - times[i - 1] <= times[i] - time ? i - 1 : i;
    }

    /// <summary>
    /// The same sounds in another sample set and index: what the target's own green lines give them. Additions in a set
    /// of their own (not the hitnormal's) keep it, as an object's addition set field does; other files stay as they are.
    /// </summary>
    private static List<Lane> Retarget(IEnumerable<Lane> lanes, SampleSet set, int index, MapFolder folder)
    {
        var list = lanes.ToList();
        var normalSet = list.Select(l => l.Sample).FirstOrDefault(s => s?.Sound == "hitnormal")?.Set;
        return [.. list.Select(lane => lane.Sample is (var laneSet, var sound, _) && HitSoundNames.Contains(sound)
            ? Decompiler.ResolveLane(folder, sound == "hitnormal" || laneSet == normalSet ? set : laneSet, sound, index)
            : lane)];
    }

    private static bool Same(WantedSound wish, Moment m) =>
        wish.Volume == m.Volume && wish.Lanes.ToHashSet().SetEquals(m.Hits.Select(h => h.Lane));
}

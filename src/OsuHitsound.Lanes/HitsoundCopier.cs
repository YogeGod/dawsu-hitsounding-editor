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

    /// <summary>Copy a slider's body sounds (its own sample sets and whistle loop) from the matched source slider.</summary>
    public bool CopyBodyHitsounds { get; init; } = true;

    /// <summary>Copy sample sets and custom indexes (otherwise the target's are kept).</summary>
    public bool CopySampleSets { get; init; } = true;

    /// <summary>Copy volumes (otherwise the target's are kept).</summary>
    public bool CopyVolumes { get; init; } = true;

    /// <summary>With volumes copied, target hit points at 5 % stay at 5 % (they're muted on purpose).</summary>
    public bool AlwaysPreserve5Volume { get; init; } = true;

    /// <summary>Copy the source's storyboard samples (its own [Events] Sample lines) into the targets.</summary>
    public bool CopyStoryboardedSamples { get; init; }

    /// <summary>Skip a storyboard sample when a target hit point within the leniency already plays that file.</summary>
    public bool IgnoreHitsoundSatisfiedSamples { get; init; } = true;

    /// <summary>Skip a storyboard sample whenever a target hit point is within the leniency.</summary>
    public bool IgnoreWheneverHitsound { get; init; }

    /// <summary>
    /// Overwrite only defined: a source hit with no target hit point goes onto a target slider tick within the leniency,
    /// as a new mixed sample (a green line switches the tick to its index and reverts after its 5 ms lookup).
    /// </summary>
    public bool CopyToSliderTicks { get; init; }

    /// <summary>Overwrite only defined: a source hit with no hit point or tick goes onto the slide of a slider there, as a new mixed sample.</summary>
    public bool CopyToSliderSlides { get; init; }

    /// <summary>The first custom index new mixed samples may use (higher ones when it's taken).</summary>
    public int StartIndex { get; init; } = 100;

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
public sealed record HitsoundCopyPlan(IReadOnlyDictionary<MomentKey, WantedSound> Wanted, int Matched, int Changed, int Muted = 0)
{
    /// <summary>Storyboard sample lines to add (in time order), after removing the target's own when <see cref="ClearSamples"/>.</summary>
    public IReadOnlyList<(string Text, double Time)> SamplesToAdd { get; init; } = [];

    public bool ClearSamples { get; init; }

    /// <summary>Slider bodies to set (by the target object's place in [HitObjects]): the slider's own sound flags and sets.</summary>
    public IReadOnlyList<(int Object, HitSound Sound, SampleSet Normal, SampleSet Addition)> Bodies { get; init; } = [];

    /// <summary>New mixed samples for simultaneous hits, slider ticks and slides (written before the .osu).</summary>
    public IReadOnlyList<MixFile> Mixes { get; init; } = [];

    /// <summary>Green lines for those samples: at a tick or inside a slide; ticks get the old settings back after lookup.</summary>
    public IReadOnlyList<(double Time, SampleSet Set, int Index, int Volume, bool Revert)> GreenLines { get; init; } = [];

    /// <summary>Writes what the compiler doesn't (slider bodies, tick and slide samples, storyboard samples) into a compiled copy.</summary>
    public void ApplyExtras(OsuFile file)
    {
        foreach (var (index, sound, normal, addition) in Bodies)
            SetBody(file.HitObjects[index], s => s with { NormalSet = normal, AdditionSet = addition }, sound);
        foreach (var (time, set, index, volume, revert) in GreenLines.OrderBy(l => l.Time))
        {
            var timing = new TimingPointIndex(file.TimingPoints);
            // Tick switches cover their +5 ms lookup for one millisecond, including exact lookup boundaries.
            double restore = time + 1;
            string? back = revert && timing.PointAt(restore) is { } after ? LineLike(after, restore, after.SampleSet ?? SampleSet.None, after.SampleIndex ?? 0, after.Volume ?? 100) : null;
            if ((timing.PointAt(time) ?? file.TimingPoints.FirstOrDefault()) is not { } now) continue;
            file.InsertTimingPoint(LineLike(now, time, set, index, volume));
            if (back is not null) file.InsertTimingPoint(back);
        }

        if (ClearSamples)
            foreach (var sample in Storyboard.SampleLines(file).ToList()) file.RemoveLine(sample.Line);
        foreach (var (text, time) in SamplesToAdd) file.InsertStoryboardSample(text, time);
    }

    // A green line at `time` with these sounds, keeping the slider velocity, meter and effects of `like`.
    private static string LineLike(TimingPoint like, double time, SampleSet set, int index, int volume) =>
        TimingPoint.FormatLine(time, like.Uninherited ? "-100" : like.BeatLengthText, like.Meter, set, index, volume, false, like.Effects);

    // A slider's own sound flags and sets, with its node sounds written out first (a slider without edge fields plays its
    // own on the nodes). A line that can't be rewritten safely is left as it is.
    private static void SetBody(HitObject slider, Func<HitSample, HitSample> sets, HitSound? sound)
    {
        try
        {
            var nodeSounds = slider.NodeSounds;
            var nodeSets = slider.NodeSets;
            slider.SetNodeSound(nodeSounds.Length - 1, nodeSounds[^1]);
            slider.SetNodeSet(0, nodeSets[0].Normal, nodeSets[0].Addition);
            if (sound is { } s) slider.HitSound = s;
            slider.HitSample = sets(slider.HitSample ?? default);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        {
        }
    }
}

/// <summary>
/// Custom indexes for Copy hitsounds' new mixed samples: from the start index up, skipping every index the
/// mapset's difficulties and files use, and one per mix (equal mixes share a file, across all the targets).
/// </summary>
public sealed class CustomSampleIndexes(MapFolder folder, int start)
{
    private readonly HashSet<int> used = [.. HitsoundDiff.IndexesUsed(folder, except: null), .. folder.Files.Select(HitsoundDiff.IndexOf).OfType<int>()];
    private readonly Dictionary<string, (MixFile Mix, int Index)> mixes = [];
    private int next = Math.Max(start, 2);

    /// <summary>The sample for these sounds in a hit slot or slider role: a new file name and index, or the one given before.</summary>
    public (MixFile Mix, int Index) For(IEnumerable<Lane> lanes, string role, SampleSet set)
        => For(lanes.Select(l => (l, 100)), role, set);

    public (MixFile Mix, int Index) For(IEnumerable<(Lane Lane, int Volume)> layers, string role, SampleSet set)
    {
        var normalized = MixPlanner.Normalize(layers, includeMissing: true);
        string key = $"{HitsoundRules.SampleName(set, role)}|{MixFile.SignatureOf(normalized)}";
        if (mixes.TryGetValue(key, out var found)) return found;
        while (used.Contains(next)) next++;
        used.Add(next);
        return mixes[key] = (new MixFile($"{HitsoundRules.SampleName(set, role)}{next}.wav", normalized), next++);
    }
}

/// <summary>One target of <see cref="HitsoundCopier.CopyTo"/>: what changed, the backup, or why it wasn't written.</summary>
public sealed record HitsoundCopyTarget(string Path, int Matched, int Changed, int Conflicts, string? Backup, string? Error, int Muted = 0)
{
    /// <summary>New tick and slide sample files written into the mapset.</summary>
    public int Generated { get; init; }

    /// <summary>Source hits put onto slider ticks and slides.</summary>
    public int OnTicksAndSlides { get; init; }
}

/// <summary>
/// Copy hitsounds: Mapping Tools' Hitsound Copier on the editor's lanes. Each source hit point goes to the nearest
/// target hit point within the leniency (both times rounded to the millisecond, as MT does; when two source points
/// pick the same target point, the later one wins). The result is a set of wanted sounds that the editor's own
/// compiler writes, so only the lines that change are rewritten.
/// </summary>
public static class HitsoundCopier
{
    private static readonly string[] HitSoundNames = ["hitnormal", "hitwhistle", "hitfinish", "hitclap"];

    /// <param name="indexes">Hands out the custom indexes for new tick and slide samples; without it those aren't made.</param>
    public static HitsoundCopyPlan Plan(OsuFile source, MapFolder sourceFolder, OsuFile target, MapFolder targetFolder, HitsoundCopyOptions options,
        CustomSampleIndexes? indexes = null)
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
        var simultaneous = new Dictionary<int, List<Moment>>();
        var unmatched = new List<Moment>();
        int matches = 0;
        if (options.CopyHitsounds)
            foreach (var s in from.Moments)
            {
                double time = s.Time - from.Settings.TimeOffset;
                if (Nearest(times, time) is { } i && Math.Abs(Math.Round(time) - Math.Round(times[i])) <= options.Leniency)
                {
                    if (!matched.TryGetValue(i, out var previous) || previous.Time != s.Time)
                        simultaneous[i] = [];
                    simultaneous[i].Add(s);
                    matched[i] = s;
                    matches++;
                }
                else unmatched.Add(s);
            }

        var wanted = new Dictionary<MomentKey, WantedSound>();
        var hitMixes = new List<MixFile>();
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
                if (simultaneous[i].Count > 1)
                {
                    var layers = simultaneous[i].SelectMany(moment => moment.Hits.Select(h =>
                        (h.Lane, Volume: options.CopyVolumes ? moment.PlayedVolume : 100))).ToList();
                    if (options.CopySampleSets)
                    {
                        indexes ??= new CustomSampleIndexes(targetFolder, options.StartIndex);
                        var (mix, _) = indexes.For(layers, "hitnormal", SampleSet.Soft);
                        if (!hitMixes.Contains(mix)) hitMixes.Add(mix);
                        lanes = [new Lane(LaneSource.BeatmapFile, mix.FileName)];
                        volume = options.CopyVolumes ? 100 : m.Volume;
                    }
                    else
                        throw new InvalidOperationException("Enable Copy sample sets and indexes to preserve simultaneous source sounds in one target hit.");
                }
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

        var plan = new HitsoundCopyPlan(wanted, matches, wanted.Count, muted)
            { Bodies = Bodies(targets, matched, objectIndex, options), Mixes = hitMixes };
        // A generated sample needs its own index; keeping target indexes means no tick/slide reassignment.
        if (indexes is not null && options is { Mode: CopyMode.OverwriteOnlyDefined, CopyHitsounds: true, CopySampleSets: true } && (options.CopyToSliderTicks || options.CopyToSliderSlides))
            plan = TicksAndSlides(plan, unmatched, from.Settings.TimeOffset, to, targetTiming, objectIndex, options, indexes);
        if (!options.CopyStoryboardedSamples) return plan;

        // Storyboard samples (Mapping Tools): Overwrite everything clears the target's first; a sample is skipped when a
        // hit point within the leniency already plays its file (or, stricter, whenever there's one), and never doubled.
        bool clear = options.Mode == CopyMode.OverwriteEverything;
        var existing = clear ? [] : Storyboard.SampleLines(target).Select(s => (s.Time, s.Layer, s.Path.ToLowerInvariant(), s.Volume)).ToHashSet();
        var add = new List<(string Text, double Time)>();
        foreach (var s in Storyboard.SampleLines(source))
        {
            var near = targets.Where((_, i) => Math.Abs(times[i] - s.Time) <= options.Leniency).ToList();
            if (options.IgnoreWheneverHitsound && near.Count > 0) continue;
            if (options.IgnoreHitsoundSatisfiedSamples)
            {
                var file = Canonical(targetFolder, s.Path);
                bool playing = near.Any(m => (wanted.GetValueOrDefault(new MomentKey(objectIndex[m.Object], m.Node)) ?? Compiler.AsWanted(m)).Lanes
                    .Any(l => l.Source == LaneSource.BeatmapFile && Canonical(targetFolder, l.Name) == file));
                if (playing) continue;
            }

            if (!existing.Add((s.Time, s.Layer, s.Path.ToLowerInvariant(), s.Volume))) continue;
            add.Add(($"Sample,{s.Time.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},{s.Layer},{s.PathField},{s.Volume}", s.Time));
        }

        return plan with { SamplesToAdd = add, ClearSamples = clear };
    }

    /// <summary>
    /// Copy to slider ticks / slides (Mapping Tools' smart mode): a source hit no target hit point matched goes onto a
    /// target slider tick within the leniency (each tick once), else onto the slide of a slider at that time, as a new
    /// mixed sample on its own index. Ticks are done first, as in MT, so a slide never takes a tick's time.
    /// </summary>
    private static HitsoundCopyPlan TicksAndSlides(HitsoundCopyPlan plan, List<Moment> unmatched, double sourceOffset, DecompiledMap to,
        TimingPointIndex timing, Dictionary<HitObject, int> objectIndex, HitsoundCopyOptions options, CustomSampleIndexes indexes)
    {
        double offset = to.Settings.TimeOffset;
        var ticks = to.Passive.Where(p => p is { Kind: PassiveKind.SliderTick, Object: not null }).Select(p => (Time: p.Time - offset, Slider: p.Object!)).OrderBy(t => t.Time).ToList();
        var slides = to.Passive.Where(p => p is { Kind: PassiveKind.SliderSlide, Object: not null }).Select(p => (Start: p.Time - offset, End: p.EndTime - offset, Slider: p.Object!)).ToList();
        var taken = new HashSet<int>();
        var mixes = new List<MixFile>();
        var lines = new List<(double, SampleSet, int, int, bool)>();
        var bodySets = plan.Bodies.ToDictionary(b => b.Object, b => b.Normal);
        var later = new List<(List<Moment> Hits, double Time)>();

        void Add(List<Moment> hits, double at, HitObject slider, string role, bool revert)
        {
            var state = Decompiler.PointState(revert ? timing.SamplePointAt(at) : timing.PointAt(at), to.Settings);
            // Put the mix in the bank this slider already reads. Making the whole slider auto would change earlier sounds.
            var ownSet = bodySets.GetValueOrDefault(objectIndex[slider], (slider.HitSample ?? default).NormalSet);
            var set = Decompiler.DecodeSet(ownSet, state.Set);
            bool combineVolumes = hits.Count > 1 && options.CopyVolumes;
            var (mix, index) = indexes.For(hits.SelectMany(hit => hit.Hits.Select(h =>
                (h.Lane, Volume: combineVolumes ? hit.PlayedVolume : 100))), role, set);
            if (mix.Layers.Count == 0) return;
            if (!mixes.Contains(mix)) mixes.Add(mix);
            // Switch at the last whole millisecond before the tick's lookup, after any intervening original green line.
            double when = revert ? Math.Floor(at + HitsoundRules.ControlPointLeniency) : Math.Round(at);
            int volume = !options.CopyVolumes || options.AlwaysPreserve5Volume && state.Volume == 5 ? state.Volume : combineVolumes ? 100 : hits[0].Volume;
            lines.Add((when, state.Set, index, volume, revert));
        }

        foreach (var group in unmatched.GroupBy(h => h.Time))
        {
            var hits = group.ToList();
            double time = group.Key - sourceOffset;
            if (options.CopyToSliderTicks && ticks.FirstOrDefault(t => Math.Abs(t.Time - time) <= options.Leniency && !taken.Contains((int)t.Time)) is { Slider: not null } tick)
            {
                taken.Add((int)tick.Time);
                Add(hits, tick.Time, tick.Slider, "slidertick", revert: true);
            }
            else if (options.CopyToSliderSlides) later.Add((hits, time));
        }

        foreach (var (hits, time) in later)
            if (!taken.Contains((int)time) && slides.FirstOrDefault(s => s.Start < time && s.End > time) is { Slider: not null } slide)
                Add(hits, time, slide.Slider, "sliderslide", revert: false);

        return plan with { Mixes = plan.Mixes.Concat(mixes).Distinct().ToList(), GreenLines = lines };
    }

    /// <summary>
    /// Slider bodies (Mapping Tools): a target slider whose head matched a source slider head takes that slider's own
    /// sound flags and sets; with Overwrite everything, the other sliders lose theirs (MT clears every object's hitsounds).
    /// </summary>
    private static List<(int, HitSound, SampleSet, SampleSet)> Bodies(Moment[] targets, Dictionary<int, Moment> matched, Dictionary<HitObject, int> objectIndex, HitsoundCopyOptions options)
    {
        var bodies = new List<(int, HitSound, SampleSet, SampleSet)>();
        if (!options.CopyHitsounds) return bodies;
        for (int i = 0; i < targets.Length; i++)
        {
            var m = targets[i];
            if (m.Position != HitPosition.SliderHead) continue;
            var own = m.Object.HitSample ?? default;
            (HitSound, SampleSet, SampleSet)? body = matched.TryGetValue(i, out var s)
                ? options.CopyBodyHitsounds && s.Position == HitPosition.SliderHead ? (s.Object.HitSound, (s.Object.HitSample ?? default).NormalSet, (s.Object.HitSample ?? default).AdditionSet) : null
                : options.Mode == CopyMode.OverwriteEverything ? (HitSound.None, SampleSet.None, SampleSet.None) : null;
            if (body is var (sound, normal, addition) && (sound != m.Object.HitSound || normal != own.NormalSet || addition != own.AdditionSet))
                bodies.Add((objectIndex[m.Object], sound, normal, addition));
        }

        return bodies;
    }

    // A sample file as osu! finds it: the mapset's file with that name, or with that name and another extension.
    private static string Canonical(MapFolder folder, string path)
    {
        var found = folder.Find(path) ?? folder.Files.FirstOrDefault(f => string.Equals(
            Path.ChangeExtension(f, null), Path.ChangeExtension(path.Replace('\\', '/'), null), StringComparison.OrdinalIgnoreCase));
        return (found ?? path).Replace('\\', '/').ToLowerInvariant();
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
    /// <param name="render">Renders a new mixed sample into WAV bytes. Required when simultaneous hits need a mix.</param>
    public static IReadOnlyList<HitsoundCopyTarget> CopyTo(OsuFile source, MapFolder sourceFolder, IEnumerable<string> targetPaths, HitsoundCopyOptions options, string backupRoot,
        Func<MixFile, byte[]?>? render = null)
    {
        var results = new List<HitsoundCopyTarget>();
        CustomSampleIndexes? indexes = null;
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in targetPaths)
        {
            try
            {
                var doc = MapDocument.Load(path);
                if (render is not null) indexes ??= new CustomSampleIndexes(doc.Folder, options.StartIndex); // one set of indexes for the mapset
                var plan = Plan(source, sourceFolder, doc.File, doc.Folder, options, indexes);

                if (render is null && plan.Mixes.Count > 0)
                    throw new IOException("Simultaneous source sounds require a sample renderer; the target was not changed.");

                // New tick and slide samples go into the mapset first. If one can't be made, this target gets none of them
                // (a green line to a missing file would play the skin, and the sliders would lose their own sample set).
                int generated = 0;
                if (render is not null && plan.Mixes.Count > 0)
                {
                    var dir = Path.GetDirectoryName(doc.Path)!;
                    var made = plan.Mixes.Where(m => written.Contains(m.FileName) || File.Exists(Path.Combine(dir, m.FileName)))
                        .ToDictionary(m => m, _ => (byte[]?)null);
                    foreach (var mix in plan.Mixes.Where(m => !made.ContainsKey(m))) made[mix] = render(mix);
                    if (made.Any(kv => kv.Value is null && !written.Contains(kv.Key.FileName) && !File.Exists(Path.Combine(dir, kv.Key.FileName))))
                        throw new IOException("A required sample could not be rendered; the target was not changed.");
                    foreach (var (mix, bytes) in made.Where(kv => kv.Value is not null))
                    {
                        SafeFile.WriteAllBytes(Path.Combine(dir, mix.FileName), bytes!);
                        written.Add(mix.FileName);
                        generated++;
                    }
                }

                doc.RescanFolder();
                var saved = MapSaver.Save(doc.Path, doc.Opened, doc.Folder, plan.Wanted, backupRoot, alsoEdit: plan.ApplyExtras);
                results.Add(new HitsoundCopyTarget(doc.Path, plan.Matched, plan.Changed, saved.Compile.Conflicts.Count, saved.Backup, null, plan.Muted)
                    { Generated = generated, OnTicksAndSlides = plan.GreenLines.Count });
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

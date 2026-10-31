using System.Text.RegularExpressions;
using Avalonia.Media;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>A row of the lane list: a group header or one lane.</summary>
internal abstract record Row(LaneGroup Group);

internal sealed record GroupRow(LaneGroup Group, int LaneCount) : Row(Group);

internal sealed record LaneRow(LaneGroup Group, Lane Lane, int Number, int Shade, int? Index) : Row(Group)
{
    /// <summary>The lane colour: its group's shade, read from the theme each time so edits show at once.</summary>
    public Color Color => ArrangeModel.ShadeOf(Group, Shade);
}

/// <summary>
/// A note drawn in a lane: an editable hit (<see cref="Hit"/>), or a read-only slider tick, storyboard sample or slider
/// body span.
/// </summary>
internal readonly record struct Note(double Time, double EndTime, Lane Lane, EditHit? Hit)
{
    public bool Passive => Hit is null;
}

/// <summary>A beat or bar line on the timeline.</summary>
internal readonly record struct GridLine(double Time, bool IsBar, int Bar, int Beat, double BeatLength);

/// <summary>A labelled stretch of the song for the sections row: osu! has no song sections, so kiai time is shown.</summary>
internal readonly record struct Section(double Start, double End, string Name);

/// <summary>A gameplay object for the object strip: its time span and its hit positions (circle, slider nodes, spinner end).</summary>
internal sealed record ObjectSpan(double Start, double End, HitObjectKind Kind, IReadOnlyList<(double Time, MomentKey Key, HitPosition Position)> Nodes);

/// <summary>
/// What the arrange area shows for one map being edited: lanes in their groups with colours (docs/ui-design.md), the
/// notes per lane from the edit session, beat, bar and snap lines from the red lines, and kiai sections. Mute, solo and
/// collapsed groups live here too, so the view and the audio engine read the same state.
/// </summary>
internal sealed partial class ArrangeModel
{
    public static Color MutedColor => UiTheme.Current.Color("colors.muted");

    private readonly HashSet<Lane> muted = [];
    private readonly HashSet<Lane> soloed = [];
    private readonly HashSet<LaneGroup> collapsed = [];
    private readonly List<LaneRow> laneRows = [];

    public ArrangeModel(EditSession session, OsuFile file, IReadOnlySet<Lane> silent)
    {
        Session = session;
        Map = session.Map;
        Silent = silent;

        // Lanes the map uses come first in each group; the other drawable lanes (unused files, skin sounds) follow.
        var used = Map.Lanes.ToHashSet();
        int number = 1;
        foreach (var group in session.Lanes.GroupBy(l => l.Group).OrderBy(g => g.Key))
        {
            int i = 0;
            foreach (var lane in group.OrderBy(l => used.Contains(l) ? 0 : 1))
                laneRows.Add(new LaneRow(group.Key, lane, number++, i++, IndexOf(lane)));
        }

        double offset = Map.Settings.TimeOffset;
        (this.file, timeOffset) = (file, offset);
        segments = BuildSegments(file, offset, []);
        Sections = KiaiSections(file, offset);
        LastTime = Math.Max(Map.Moments.Count > 0 ? Map.Moments[^1].Time : 0, Map.Passive.Count > 0 ? Map.Passive.Max(p => p.EndTime) : 0);
        Rows = BuildRows();
        GroupCount = laneRows.Select(r => r.Group).Distinct().Count();
        passive = Map.Passive.GroupBy(p => p.Lane).ToDictionary(g => g.Key, g => g.Select(p => new Note(p.Time, p.EndTime, p.Lane, null)).ToList());
        RebuildNotes();
        Objects = BuildObjects(session, Map.Settings.TimeOffset);
    }

    // The map's read-only sounds never change, so their notes are built once.
    private readonly Dictionary<Lane, List<Note>> passive;

    public EditSession Session { get; }

    public DecompiledMap Map { get; }

    public IReadOnlySet<Lane> Silent { get; set; }

    public IReadOnlyDictionary<Lane, List<Note>> Notes { get; private set; } = new Dictionary<Lane, List<Note>>();

    /// <summary>Moments that won't save as wanted, from the last background compile.</summary>
    public IReadOnlySet<MomentKey> Conflicted { get; private set; } = new HashSet<MomentKey>();

    /// <summary>The group of each moment's first hit (its colour in the volume lane), for moments that have hits.</summary>
    public IReadOnlyDictionary<MomentKey, LaneGroup> MomentGroups { get; private set; } = new Dictionary<MomentKey, LaneGroup>();

    /// <summary>Times of everything that won't be saved (conflicts and hits with no object), sorted, for the red stripes.</summary>
    public IReadOnlyList<double> ProblemTimes { get; private set; } = [];

    private readonly HashSet<EditHit> previewMuted = [];

    /// <summary>Hits silenced while listening (the Mute tool). Never saved: osu! can't store a muted sound.</summary>
    public IReadOnlyCollection<EditHit> PreviewMutedHits => previewMuted;

    public bool IsPreviewMuted(EditHit hit) => previewMuted.Contains(hit);

    public void TogglePreviewMute(EditHit hit)
    {
        if (!previewMuted.Remove(hit)) previewMuted.Add(hit);
        Changed?.Invoke();
        PreviewMutesChanged?.Invoke();
    }

    /// <summary>The Mute tool changed what should sound, so playback must be rebuilt.</summary>
    public event Action? PreviewMutesChanged;

    /// <summary>A hit that won't be saved: no object to play on, or its moment is a conflict.</summary>
    public bool IsProblem(EditHit hit) => hit.Anchor is not { } key || Conflicted.Contains(key);

    /// <summary>Rebuilds the notes from the session's hits after an edit.</summary>
    public void RebuildNotes()
    {
        var notes = passive.ToDictionary(kv => kv.Key, kv => new List<Note>(kv.Value));
        var groups = new Dictionary<MomentKey, LaneGroup>();
        foreach (var hit in Session.State.Hits)
        {
            if (!notes.TryGetValue(hit.Lane, out var list)) notes[hit.Lane] = list = [];
            list.Add(new Note(hit.Time, hit.Time, hit.Lane, hit));
            if (hit.Anchor is { } key) groups.TryAdd(key, hit.Lane.Group);
        }

        foreach (var list in notes.Values) list.Sort((a, b) => a.Time.CompareTo(b.Time));
        previewMuted.IntersectWith(Session.State.Hits); // forget mutes of hits that were moved or removed
        (Notes, MomentGroups) = (notes, groups);
        UpdateProblemTimes();
        Changed?.Invoke();
    }

    /// <summary>Takes the conflicts of a finished background compile; no rebuild needed, drawing reads them.</summary>
    public void SetConflicts(IReadOnlySet<MomentKey> conflicted)
    {
        Conflicted = conflicted;
        UpdateProblemTimes();
        Changed?.Invoke();
    }

    private void UpdateProblemTimes() => ProblemTimes =
    [
        .. Conflicted.Where(Session.MomentByKey.ContainsKey).Select(k => Session.MomentByKey[k].Time)
            .Concat(Session.Unanchored.Select(h => h.Time)).Distinct().Order(),
    ];

    /// <summary>The nearest snap position: the beat divided by <paramref name="divisor"/>, from the red line in force.</summary>
    public double Snap(double time, int divisor)
    {
        if (SegmentAt(time) is not { } s) return time;
        double step = s.BeatLength / divisor;
        return s.GridStart + Math.Round((time - s.GridStart) / step) * step;
    }

    /// <summary>The time <paramref name="steps"/> snap steps (beat / divisor) after <paramref name="time"/>, each step as long as the red line in force there says.</summary>
    public double StepsAfter(double time, int steps, int divisor)
    {
        // Grid line to grid line: a step that would cross a red line stops on it, so the rest follow that red line's grid.
        for (int n = 0; n < steps; n++)
        {
            int i = SegmentIndex(time);
            if (i < 0)
            {
                time += 500.0 / divisor;
                continue;
            }

            var s = segments[i];
            double step = s.BeatLength / divisor;
            double next = s.GridStart + (Math.Floor((time - s.GridStart) / step + 1e-6) + 1) * step;
            int red = i + 1;
            while (red < segments.Length && segments[red].GridStart == s.GridStart) red++; // splits share their red line's grid
            if (red < segments.Length && next > segments[red].Start - 1e-6) next = segments[red].Start;
            time = next;
        }

        return time;
    }

    /// <summary>
    /// The snap lines between beats, each with the beat fraction it falls on in lowest terms: with 1/4 snap, the line
    /// halfway is a 1/2 (Fraction 2) and the others are 1/4 (Fraction 4), as osu!'s editor colours them.
    /// </summary>
    public IEnumerable<(double Time, int Fraction)> SubLines(double from, double to, int divisor)
    {
        // The map's beats from its red line, whatever the time signature: snapping follows them too. A beat that is not
        // also a time-signature beat (e.g. every other beat in 3/2) gets a line here, as Fraction 1.
        for (int i = Math.Max(0, SegmentIndex(from)); i < segments.Length; i++)
        {
            var s = segments[i];
            if (s.Start > to) yield break;
            for (long b = (long)Math.Floor((Math.Max(from, s.Start) - s.GridStart) / s.BeatLength); ; b++)
            {
                double beat = s.GridStart + b * s.BeatLength;
                if (beat >= s.End || beat > to) break;
                double units = (beat - s.Start) / s.Unit;
                if (beat >= s.Start && beat >= from && Math.Abs(units - Math.Round(units)) > 1e-6) yield return (beat, 1);
                if (divisor <= 1) continue;
                for (int k = 1; k < divisor; k++)
                {
                    double t = beat + k * s.BeatLength / divisor;
                    if (t >= s.End) break;
                    if (t >= s.Start && t >= from && t <= to) yield return (t, divisor / (int)System.Numerics.BigInteger.GreatestCommonDivisor(k, divisor));
                }
            }
        }
    }

    private Segment[] segments;
    private readonly OsuFile file;
    private readonly double timeOffset;

    /// <summary>Editor-only time signature changes (saved in the project, never in the .osu), sorted by bar.</summary>
    public IReadOnlyList<TimeSignature> TimeSignatures { get; private set; } = [];

    /// <summary>Replaces the time signature changes and redraws. Invalid entries are left out; one per bar, the last wins.</summary>
    public void SetTimeSignatures(IEnumerable<TimeSignature> changes)
    {
        TimeSignatures = [.. changes.Where(IsValid).GroupBy(t => t.Bar).Select(g => g.Last()).OrderBy(t => t.Bar)];
        segments = BuildSegments(file, timeOffset, TimeSignatures);
        Changed?.Invoke();
    }

    public static bool IsValid(TimeSignature t) => t.Bar >= 1 && t.Numerator is >= 1 and <= 32 && t.Denominator is 1 or 2 or 4 or 8 or 16;

    /// <summary>The time signature in force at a time: a change's, or the red line's meter over 4.</summary>
    public (int Numerator, int Denominator) SignatureAt(double time) =>
        SegmentAt(time) is { } s ? (s.Meter, s.Denominator) : (4, 4);

    public IReadOnlyList<Section> Sections { get; }

    public double LastTime { get; }


    /// <summary>Every gameplay object, in time order, for the object strip.</summary>
    public IReadOnlyList<ObjectSpan> Objects { get; }

    private static List<ObjectSpan> BuildObjects(EditSession session, double offset) =>
    [
        .. session.Moments.GroupBy(x => x.Key.ObjectIndex).Select(g =>
        {
            var nodes = g.Select(x => (x.Moment.Time, x.Key, x.Moment.Position)).OrderBy(n => n.Time).ToList();
            var obj = g.First().Moment.Object;
            // A spinner's only moment is its end; it starts at the object's own time.
            double start = obj.Kind == HitObjectKind.Spinner ? obj.Time + offset : nodes[0].Time;
            return new ObjectSpan(start, nodes[^1].Time, obj.Kind, nodes);
        }).OrderBy(o => o.Start),
    ];

    /// <summary>The object at a time: a span containing it, or a circle within <paramref name="tolerance"/> ms.</summary>
    public ObjectSpan? ObjectAt(double time, double tolerance)
    {
        int i = Sorted.LowerBound(Objects, time - tolerance - 60000, o => o.Start);
        ObjectSpan? best = null;
        double bestDistance = tolerance;
        for (; i < Objects.Count && Objects[i].Start <= time + tolerance; i++)
        {
            var o = Objects[i];
            double d = time < o.Start ? o.Start - time : time > o.End ? time - o.End : 0;
            if (d <= bestDistance) (best, bestDistance) = (o, d);
        }

        return best;
    }
    public IReadOnlyList<Row> Rows { get; private set; }

    public IReadOnlyList<LaneRow> LaneRows => laneRows;

    public int GroupCount { get; }


    /// <summary>A group's colour list in the theme: the group colour first, then the lane shades it steps through.</summary>
    private static Color[] ColorsOf(LaneGroup group) => UiTheme.Current.Colors(group switch
    {
        LaneGroup.SoftSet => "colors.soft",
        LaneGroup.NormalSet => "colors.normal",
        LaneGroup.DrumSet => "colors.drum",
        LaneGroup.OwnFiles => "colors.own",
        _ => "colors.skin",
    });

    public static Color GroupColor(LaneGroup group) => ColorsOf(group)[0];

    public static Color ShadeOf(LaneGroup group, int shade)
    {
        var colors = ColorsOf(group);
        return colors.Length == 1 ? colors[0] : colors[1 + shade % (colors.Length - 1)];
    }

    public static string GroupName(LaneGroup group) => group switch
    {
        LaneGroup.SoftSet => "Soft sampleset",
        LaneGroup.NormalSet => "Normal sampleset",
        LaneGroup.DrumSet => "Drum sampleset",
        LaneGroup.OwnFiles => "Own files",
        _ => "Skin sounds (no file)",
    };

    public event Action? Changed;

    public bool IsCollapsed(LaneGroup group) => collapsed.Contains(group);

    public bool IsMuted(Lane lane) => muted.Contains(lane);

    public bool IsSoloed(Lane lane) => soloed.Contains(lane);

    public bool IsGroupMuted(LaneGroup group) => LanesIn(group).All(muted.Contains);

    public bool AnySoloed => soloed.Count > 0;

    public bool IsGroupSoloed(LaneGroup group) => LanesIn(group).All(soloed.Contains);

    /// <summary>Whether a lane sounds: not muted, and soloed when anything is soloed.</summary>
    public bool IsAudible(Lane lane)
    {
        lock (muted)
            return !muted.Contains(lane) && (soloed.Count == 0 || soloed.Contains(lane));
    }

    public void ToggleCollapsed(LaneGroup group)
    {
        if (!collapsed.Remove(group)) collapsed.Add(group);
        Rows = BuildRows();
        Changed?.Invoke();
    }

    public void ToggleMute(Lane lane) => Toggle(muted, [lane]);

    public void ToggleSolo(Lane lane) => Toggle(soloed, [lane]);

    public void ToggleGroupMute(LaneGroup group) => Toggle(muted, LanesIn(group));

    public void ToggleGroupSolo(LaneGroup group) => Toggle(soloed, LanesIn(group));

    /// <summary>The red line in effect at a time (the first one before it): its beat length and meter.</summary>
    public (double BeatLength, int Meter)? RedLineAt(double time) =>
        SegmentAt(time) is { } s ? (s.BeatLength, s.Meter) : null;

    /// <summary>Beat and bar lines between two times. Bars restart counting at every red line, as in osu!'s editor.</summary>
    public IEnumerable<GridLine> GridLines(double from, double to)
    {
        for (int i = Math.Max(0, SegmentIndex(from)); i < segments.Length; i++)
        {
            var s = segments[i];
            if (s.Start > to) yield break;
            // Beats in the time signature's unit (eighths in 7/8); the line keeps the map's beat for snap spacing.
            for (long b = Math.Max(0, (long)Math.Floor((from - s.Start) / s.Unit)); ; b++)
            {
                double t = s.Start + b * s.Unit;
                if (t >= s.End || t > to) break;
                if (t >= from) yield return new GridLine(t, b % s.Meter == 0, s.FirstBar + (int)(b / s.Meter), (int)(b % s.Meter) + 1, s.BeatLength);
            }
        }
    }

    /// <summary>bar.beat.tick (tick in 1/16ths of a beat, 1-based like Cubase) for the LCD.</summary>
    public (int Bar, int Beat, int Tick) BarBeatTick(double time)
    {
        if (SegmentAt(time) is not { } s) return (1, 1, 1);
        double beats = Math.Max(0, (time - s.Start) / s.Unit);
        long whole = (long)Math.Floor(beats);
        return (s.FirstBar + (int)(whole / s.Meter), (int)(whole % s.Meter) + 1, (int)Math.Floor((beats - whole) * 16) + 1);
    }

    /// <summary>
    /// One stretch per red line, split where a time signature changes, with the number of its first bar. The first starts
    /// on a whole bar at or before 0, so bars before the first red line are counted too. BeatLength is the map's beat
    /// (snapping); Unit is the time signature's beat (Meter of them make a bar).
    /// </summary>
    private readonly record struct Segment(double Start, double End, double BeatLength, int Meter, double Unit, int Denominator, int FirstBar, double GridStart);

    private Segment? SegmentAt(double time) => segments.Length == 0 ? null : segments[Math.Max(0, SegmentIndex(time))];

    // The last segment starting at or before time, or -1.
    private int SegmentIndex(double time)
    {
        int lo = 0, hi = segments.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (segments[mid].Start <= time) lo = mid + 1;
            else hi = mid;
        }

        return lo - 1;
    }

    private static Segment[] BuildSegments(OsuFile file, double offset, IReadOnlyList<TimeSignature> signatures)
    {
        var reds = new List<(double Time, double BeatLength, int Meter)>();
        foreach (var p in file.TimingPoints)
        {
            try
            {
                // lazer clamps beat length to 6–60000 ms; it also keeps a hostile tiny value from flooding the grid.
                if (p.Uninherited && p.BeatLength > 0) reds.Add((p.Time + offset, Math.Clamp(p.BeatLength, 6, 60000), p.Meter));
            }
            catch (FormatException)
            {
            }
        }

        reds.Sort((a, b) => a.Time.CompareTo(b.Time));
        var segments = new List<Segment>();
        int bar = 1;
        for (int i = 0; i < reds.Count; i++)
        {
            var (start, beatLength, meter) = reds[i];
            if (i == 0)
            {
                // Bars before the first red line, in bar 1's time signature so the red line is a downbeat.
                var (n1, d1) = signatures.FirstOrDefault(t => t.Bar == 1) is { } first ? (first.Numerator, first.Denominator) : (meter, 4);
                double barLength1 = n1 * beatLength * 4 / d1;
                start -= Math.Ceiling(start / barLength1) * barLength1;
            }

            double end = i + 1 < reds.Count ? reds[i + 1].Time : double.PositiveInfinity;
            double grid = start; // the red line's beat grid (a whole number of beats from it), kept across time signature splits
            // A time signature change splits the stretch at its bar and lasts until the next change, past red lines too.
            while (true)
            {
                var (n, d) = signatures.LastOrDefault(t => t.Bar <= bar) is { } sig ? (sig.Numerator, sig.Denominator) : (meter, 4);
                double unit = beatLength * 4 / d, barLength = n * unit;
                int next = signatures.FirstOrDefault(t => t.Bar > bar)?.Bar ?? 0;
                double split = next > 0 ? start + (next - bar) * barLength : double.PositiveInfinity;
                if (split < end - 1e-6)
                {
                    segments.Add(new Segment(start, split, beatLength, n, unit, d, bar, grid));
                    (start, bar) = (split, next);
                    continue;
                }

                segments.Add(new Segment(start, end, beatLength, n, unit, d, bar, grid));
                if (!double.IsInfinity(end)) bar += (int)Math.Ceiling((end - start) / barLength - 1e-6);
                break;
            }
        }

        return [.. segments];
    }

    private IEnumerable<Lane> LanesIn(LaneGroup group) => laneRows.Where(r => r.Group == group).Select(r => r.Lane);

    private void Toggle(HashSet<Lane> set, IEnumerable<Lane> lanes)
    {
        var list = lanes.ToList();
        lock (muted)
        {
            if (list.All(set.Contains)) set.ExceptWith(list);
            else set.UnionWith(list);
        }

        Changed?.Invoke();
    }

    private List<Row> BuildRows()
    {
        // Hidden lanes (deleted) stay out unless they still have hits; the filter (Ctrl+F) matches names.
        var withHits = Session.State.Hits.Select(h => h.Lane).ToHashSet();
        var rows = new List<Row>();
        foreach (var group in laneRows.Where(r => (!hidden.Contains(r.Lane) || withHits.Contains(r.Lane)) && MatchesFilter(r.Lane)).GroupBy(r => r.Group))
        {
            rows.Add(new GroupRow(group.Key, group.Count()));
            if (!collapsed.Contains(group.Key)) rows.AddRange(group);
        }

        return rows;
    }

    // ---------------------------------------------------------------- lane management (kept in the project file)

    private readonly Dictionary<Lane, string> names = [];
    private readonly HashSet<Lane> hidden = [];
    private readonly HashSet<Lane> added = [];

    public string Filter { get; private set; } = "";

    /// <summary>The name shown for a lane: its display name if renamed, else its file or skin name.</summary>
    public string NameOf(Lane lane) => names.TryGetValue(lane, out var n) ? n : lane.Name;

    private bool MatchesFilter(Lane lane) =>
        Filter.Length == 0 || NameOf(lane).Contains(Filter, StringComparison.OrdinalIgnoreCase) || lane.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase);

    public void SetFilter(string text) => Relayout(() => Filter = text.Trim());

    /// <summary>Renames a lane on screen only; its file and what it saves as stay the same. Blank restores the file name.</summary>
    public void RenameLane(Lane lane, string name) => Relayout(() =>
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == lane.Name) names.Remove(lane);
        else names[lane] = name.Trim();
    });

    /// <summary>Moves a lane up (-1) or down (+1) within its group.</summary>
    public void MoveLane(Lane lane, int direction) => Relayout(() =>
    {
        int i = laneRows.FindIndex(r => r.Lane == lane), j = i + direction;
        if (i < 0 || j < 0 || j >= laneRows.Count || laneRows[j].Group != laneRows[i].Group) return;
        (laneRows[i], laneRows[j]) = (laneRows[j], laneRows[i]);
    });

    /// <summary>Adds a lane (a sample file in the mapset) at the end of its group, or shows it again if it was deleted.</summary>
    public void AddLane(Lane lane) => Relayout(() =>
    {
        hidden.Remove(lane);
        if (laneRows.Any(r => r.Lane == lane)) return;
        added.Add(lane);
        int last = laneRows.FindLastIndex(r => r.Group <= lane.Group);
        int shade = laneRows.Count(r => r.Group == lane.Group);
        laneRows.Insert(last + 1, new LaneRow(lane.Group, lane, 0, shade, IndexOf(lane)));
    });

    /// <summary>Hides a lane after its hits were deleted (the file is kept); undoing brings its hits, and the lane, back.</summary>
    public void HideLane(Lane lane) => Relayout(() => hidden.Add(lane));

    /// <summary>Whether any lane is hidden: only then can an edit (undo) make a lane reappear.</summary>
    public bool HasHiddenLanes => hidden.Count > 0;

    /// <summary>Lanes rebuilt after hits change (a hidden lane with hits again shows up).</summary>
    public void RefreshRows() => Relayout(() => { });

    private void Relayout(Action change)
    {
        change();
        for (int i = 0; i < laneRows.Count; i++) laneRows[i] = laneRows[i] with { Number = i + 1 };
        Rows = BuildRows();
        Changed?.Invoke();
    }

    /// <summary>The lane part of the project file.</summary>
    public (Dictionary<string, string> Names, List<string> Order, List<string> Added, List<string> Hidden) LaneLayout() =>
        (names.ToDictionary(kv => ProjectStore.KeyOf(kv.Key), kv => kv.Value),
         laneRows.Select(r => ProjectStore.KeyOf(r.Lane)).ToList(),
         added.Select(ProjectStore.KeyOf).ToList(),
         hidden.Select(ProjectStore.KeyOf).ToList());

    /// <summary>Restores lane names, added and hidden lanes and order from a project; unknown or vanished lanes are skipped.</summary>
    public void ApplyLaneLayout(ProjectData project, Func<Lane, bool> exists)
    {
        foreach (var lane in project.AddedLanes.Select(ProjectStore.LaneOf).OfType<Lane>().Where(exists)) AddLane(lane);
        Relayout(() =>
        {
            foreach (var (key, name) in project.LaneNames)
                if (ProjectStore.LaneOf(key) is { } lane) names[lane] = name;
            foreach (var lane in project.HiddenLanes.Select(ProjectStore.LaneOf).OfType<Lane>()) hidden.Add(lane);
            var order = project.LaneOrder.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);
            var sorted = laneRows.OrderBy(r => r.Group).ThenBy(r => order.TryGetValue(ProjectStore.KeyOf(r.Lane), out int i) ? i : int.MaxValue).ToList();
            laneRows.Clear();
            laneRows.AddRange(sorted);
        });
    }

    /// <summary>
    /// Which file Add lane should start on, following the lane added last (or the lane right-clicked): the next index
    /// of the same sound ("drum-hitclap3" → "drum-hitclap4"), else that sound at any index, else the same set, else the
    /// first file. Null when there are no candidates.
    /// </summary>
    public static string? SuggestNewLane(IReadOnlyList<string> candidates, Lane? previous)
    {
        if (candidates.Count == 0) return null;
        if (previous?.Sample is not (var set, var sound, var index)) return candidates[0];
        var parsed = candidates.Select(c => (File: c, Sample: new Lane(LaneSource.BeatmapFile, c).Sample)).ToList();
        var sameSound = parsed.Where(c => c.Sample is { } s && s.Set == set && s.Sound == sound).OrderBy(c => c.Sample!.Value.Index).ToList();
        return sameSound.FirstOrDefault(c => c.Sample!.Value.Index > index).File
               ?? sameSound.FirstOrDefault().File
               ?? parsed.FirstOrDefault(c => c.Sample?.Set == set).File
               ?? candidates[0];
    }

    /// <summary>The custom index a lane plays as (0 for the skin); null for files osu! only plays by name.</summary>
    private static int? IndexOf(Lane lane) => lane.Sample?.Index;

    private static List<Section> KiaiSections(OsuFile file, double offset)
    {
        var sections = new List<Section>();
        double? start = null;
        foreach (var p in file.TimingPoints)
        {
            double time;
            bool kiai;
            try
            {
                (time, kiai) = (p.Time + offset, p.Kiai);
            }
            catch (FormatException)
            {
                continue;
            }

            if (kiai && start is null) start = time;
            else if (!kiai && start is { } s)
            {
                sections.Add(new Section(s, time, "Kiai"));
                start = null;
            }
        }

        if (start is { } open) sections.Add(new Section(open, double.PositiveInfinity, "Kiai"));
        return sections;
    }
}

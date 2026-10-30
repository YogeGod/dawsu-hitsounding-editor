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
        segments = BuildSegments(file, offset);
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

    public IReadOnlySet<Lane> Silent { get; }

    public IReadOnlyDictionary<Lane, List<Note>> Notes { get; private set; } = new Dictionary<Lane, List<Note>>();

    /// <summary>Moments that won't save as wanted, from the last background compile.</summary>
    public IReadOnlySet<MomentKey> Conflicted { get; private set; } = new HashSet<MomentKey>();

    /// <summary>The group of each moment's first hit (its colour in the volume lane), for moments that have hits.</summary>
    public IReadOnlyDictionary<MomentKey, LaneGroup> MomentGroups { get; private set; } = new Dictionary<MomentKey, LaneGroup>();

    /// <summary>Times of everything that won't be saved (conflicts and hits with no object), sorted, for the red stripes.</summary>
    public IReadOnlyList<double> ProblemTimes { get; private set; } = [];

    private readonly HashSet<EditHit> previewMuted = [];

    /// <summary>Hits silenced while listening (the Mute tool). Never saved: osu! can't store a muted sound.</summary>
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
        return s.Start + Math.Round((time - s.Start) / step) * step;
    }

    /// <summary>Snap lines between beats (beat / divisor), for drawing when zoomed in enough.</summary>
    public IEnumerable<double> SubLines(double from, double to, int divisor)
    {
        if (divisor <= 1) yield break;
        // From one beat earlier, so a beat that starts before the view still gets its subdivisions.
        double back = SegmentAt(from)?.BeatLength ?? 0;
        foreach (var line in GridLines(from - back, to))
            for (int k = 1; k < divisor; k++)
            {
                double t = line.Time + k * line.BeatLength / divisor;
                if (t >= from && t <= to) yield return t;
            }
    }

    private readonly Segment[] segments;

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
        LaneGroup.SoftSet => "Soft set",
        LaneGroup.NormalSet => "Normal set",
        LaneGroup.DrumSet => "Drum set",
        LaneGroup.OwnFiles => "Own files",
        _ => "Skin (no file)",
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
            for (long b = Math.Max(0, (long)Math.Floor((from - s.Start) / s.BeatLength)); ; b++)
            {
                double t = s.Start + b * s.BeatLength;
                if (t >= s.End || t > to) break;
                if (t >= from) yield return new GridLine(t, b % s.Meter == 0, s.FirstBar + (int)(b / s.Meter), (int)(b % s.Meter) + 1, s.BeatLength);
            }
        }
    }

    /// <summary>bar.beat.tick (tick in 1/16ths of a beat, 1-based like Cubase) for the LCD.</summary>
    public (int Bar, int Beat, int Tick) BarBeatTick(double time)
    {
        if (SegmentAt(time) is not { } s) return (1, 1, 1);
        double beats = Math.Max(0, (time - s.Start) / s.BeatLength);
        long whole = (long)Math.Floor(beats);
        return (s.FirstBar + (int)(whole / s.Meter), (int)(whole % s.Meter) + 1, (int)Math.Floor((beats - whole) * 16) + 1);
    }

    /// <summary>
    /// One stretch per red line, with the number of its first bar. The first starts on a whole bar at or before 0, so
    /// bars before the first red line are counted too.
    /// </summary>
    private readonly record struct Segment(double Start, double End, double BeatLength, int Meter, int FirstBar);

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

    private static Segment[] BuildSegments(OsuFile file, double offset)
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
        var segments = new Segment[reds.Count];
        int bar = 1;
        for (int i = 0; i < reds.Count; i++)
        {
            var (start, beatLength, meter) = reds[i];
            if (i == 0) start -= Math.Ceiling(start / (beatLength * meter)) * beatLength * meter;
            double end = i + 1 < reds.Count ? reds[i + 1].Time : double.PositiveInfinity;
            segments[i] = new Segment(start, end, beatLength, meter, bar);
            if (!double.IsInfinity(end)) bar += (int)Math.Ceiling((end - start) / (beatLength * meter) - 1e-6);
        }

        return segments;
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
        var rows = new List<Row>();
        foreach (var group in laneRows.GroupBy(r => r.Group))
        {
            rows.Add(new GroupRow(group.Key, group.Count()));
            if (!collapsed.Contains(group.Key)) rows.AddRange(group);
        }

        return rows;
    }

    /// <summary>The custom index a lane plays as: the number at the end of a set-named file, 1 without one, 0 for the skin.</summary>
    private static int? IndexOf(Lane lane)
    {
        if (lane.Source == LaneSource.Skin) return 0;
        if (lane.Group == LaneGroup.OwnFiles) return null;
        var match = TrailingNumber().Match(Path.GetFileNameWithoutExtension(lane.Name));
        return match.Success ? int.Parse(match.Value) : 1;
    }

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingNumber();

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

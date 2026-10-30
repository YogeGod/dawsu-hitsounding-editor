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

/// <summary>A note drawn in a lane: a moment's hit, a slider tick or a storyboard sample, or a slider body span.</summary>
internal readonly record struct Note(double Time, double EndTime, Lane Lane, int Volume, bool Passive, int MomentIndex);

/// <summary>A beat or bar line on the timeline.</summary>
internal readonly record struct GridLine(double Time, bool IsBar, int Bar, int Beat, double BeatLength);

/// <summary>A labelled stretch of the song for the sections row: osu! has no song sections, so kiai time is shown.</summary>
internal readonly record struct Section(double Start, double End, string Name);

/// <summary>
/// What the arrange area shows for one decompiled map: lanes in their groups with colours (docs/ui-design.md), the
/// notes per lane, beat and bar lines from the red lines, and kiai sections. Mute, solo and collapsed groups live here
/// too, so the view and the audio engine read the same state.
/// </summary>
internal sealed partial class ArrangeModel
{
    public static Color MutedColor => UiTheme.Current.Color("colors.muted");

    private readonly HashSet<Lane> muted = [];
    private readonly HashSet<Lane> soloed = [];
    private readonly HashSet<LaneGroup> collapsed = [];
    private readonly List<LaneRow> laneRows = [];

    public ArrangeModel(DecompiledMap map, OsuFile file, IReadOnlySet<Lane> silent)
    {
        Map = map;
        Silent = silent;

        int number = 1;
        foreach (var group in map.Lanes.GroupBy(l => l.Group).OrderBy(g => g.Key))
        {
            int i = 0;
            foreach (var lane in group)
                laneRows.Add(new LaneRow(group.Key, lane, number++, i++, IndexOf(lane)));
        }

        var notes = new Dictionary<Lane, List<Note>>();
        for (int m = 0; m < map.Moments.Count; m++)
            foreach (var hit in map.Moments[m].Hits)
                Add(new Note(map.Moments[m].Time, map.Moments[m].Time, hit.Lane, map.Moments[m].Volume, false, m));
        foreach (var p in map.Passive)
            Add(new Note(p.Time, p.EndTime, p.Lane, p.Volume, true, -1));
        foreach (var list in notes.Values) list.Sort((a, b) => a.Time.CompareTo(b.Time));
        Notes = notes;

        double offset = map.Settings.TimeOffset;
        segments = BuildSegments(file, offset);
        Sections = KiaiSections(file, offset);
        LastTime = Math.Max(map.Moments.Count > 0 ? map.Moments[^1].Time : 0, map.Passive.Count > 0 ? map.Passive.Max(p => p.EndTime) : 0);
        Rows = BuildRows();
        GroupCount = laneRows.Select(r => r.Group).Distinct().Count();

        void Add(Note n)
        {
            if (!notes.TryGetValue(n.Lane, out var list)) notes[n.Lane] = list = [];
            list.Add(n);
        }
    }

    public DecompiledMap Map { get; }

    public IReadOnlySet<Lane> Silent { get; }

    public IReadOnlyDictionary<Lane, List<Note>> Notes { get; }

    private readonly Segment[] segments;

    public IReadOnlyList<Section> Sections { get; }

    public double LastTime { get; }

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

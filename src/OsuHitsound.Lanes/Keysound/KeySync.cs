namespace OsuHitsound.Lanes.Keysound;

/// <summary>A note moved onto a gameplay position by Sync: its new start tick and the target it keeps.</summary>
public sealed record KeySyncMove(Guid Track, Guid Note, long Start, KeySyncTarget Target);

/// <summary>Where a note's sync stands against the current reference.</summary>
public enum KeySyncStatus { Unsynced, Synced, Moved, Missing }

/// <summary>The options of a Sync preview.</summary>
public sealed record KeySyncOptions
{
    public const double DefaultMaxShift = 20;
    /// <summary>The largest start-time move, in ms.</summary>
    public double MaxShift { get; init; } = DefaultMaxShift;
    public IReadOnlySet<HitPosition> Positions { get; init; } = new HashSet<HitPosition>(Enum.GetValues<HitPosition>());
    /// <summary>Also re-sync notes whose assignment is still valid (otherwise they are left alone).</summary>
    public bool ResyncAll { get; init; }
}

/// <summary>
/// One note in a Sync preview: the targets in range, nearest first. <see cref="Ambiguous"/> when the nearest two are
/// equally close (the user picks); none in range leaves the note unsynced.
/// </summary>
public sealed record KeySyncProposal(Guid Track, KeyNote Note, double NoteTime, IReadOnlyList<KeySyncTarget> Candidates)
{
    public bool InRange => Candidates.Count > 0;
    public bool Ambiguous => Candidates.Count > 1 && Math.Abs(Distance(Candidates[0]) - Distance(Candidates[1])) < KeySync.TieTolerance;
    public double Distance(KeySyncTarget target) => Math.Abs(target.Time - NoteTime);
}

/// <summary>
/// Explicit Sync (docs/ideas/keysounding.md): notes move onto the nearest reference position within the maximum shift.
/// Nothing is assigned without a preview, ties are never resolved automatically, and moved or missing targets are
/// flagged rather than retargeted.
/// </summary>
public static class KeySync
{
    /// <summary>Two targets closer than this to each other's distance count as equally close (ms).</summary>
    public const double TieTolerance = 0.5;
    /// <summary>A target whose position moved by more than this no longer matches (ms).</summary>
    public const double MovedTolerance = 1;

    /// <summary>The reference's positions by (object index, node), for <see cref="Status(KeyNote, IReadOnlyDictionary{ValueTuple{int, int}, KeySyncTarget})"/>.</summary>
    public static Dictionary<(int Object, int Node), KeySyncTarget> Index(IEnumerable<KeySyncTarget> targets)
    {
        var index = new Dictionary<(int, int), KeySyncTarget>();
        foreach (var t in targets) index.TryAdd((t.ObjectIndex, t.Node), t);
        return index;
    }

    /// <summary>A note's sync against the reference's current positions.</summary>
    public static KeySyncStatus Status(KeyNote note, IReadOnlyDictionary<(int Object, int Node), KeySyncTarget> targets)
    {
        if (note.Target is not { } t) return KeySyncStatus.Unsynced;
        if (!targets.TryGetValue((t.ObjectIndex, t.Node), out var now)) return KeySyncStatus.Missing;
        return now.Position == t.Position && Math.Abs(now.Time - t.Time) <= MovedTolerance ? KeySyncStatus.Synced : KeySyncStatus.Moved;
    }

    public static KeySyncStatus Status(KeyNote note, IReadOnlyList<KeySyncTarget> targets) => Status(note, Index(targets));

    /// <summary>
    /// The preview for these notes (track, note): every note that needs syncing (all of them with
    /// <see cref="KeySyncOptions.ResyncAll"/>), with the allowed targets in range.
    /// </summary>
    public static List<KeySyncProposal> Preview(IEnumerable<(Guid Track, KeyNote Note)> notes, IReadOnlyList<KeySyncTarget> targets, TempoMap tempo, KeySyncOptions options)
    {
        var allowed = targets.Where(t => options.Positions.Contains(t.Position)).ToArray(); // sorted by time
        var times = allowed.Select(t => t.Time).ToArray();
        var index = Index(targets);
        var result = new List<KeySyncProposal>();
        foreach (var (track, note) in notes)
        {
            if (!options.ResyncAll && Status(note, index) == KeySyncStatus.Synced) continue;
            double at = tempo.TimeAt(note.Start);
            int i = LowerBound(times, at - options.MaxShift);
            var near = new List<KeySyncTarget>();
            for (; i < allowed.Length && allowed[i].Time <= at + options.MaxShift; i++) near.Add(allowed[i]);
            near.Sort((a, b) => Math.Abs(a.Time - at).CompareTo(Math.Abs(b.Time - at)));
            result.Add(new KeySyncProposal(track, note, at, near));
        }

        return result;
    }

    /// <summary>
    /// The moves a preview applies: each in-range note to its chosen target (<paramref name="choices"/>, by note id), or
    /// to its nearest one when that isn't a tie. Tied notes without a choice stay unsynced.
    /// </summary>
    public static List<KeySyncMove> Moves(IEnumerable<KeySyncProposal> proposals, TempoMap tempo, IReadOnlyDictionary<Guid, KeySyncTarget>? choices = null)
    {
        var moves = new List<KeySyncMove>();
        foreach (var p in proposals)
        {
            var target = choices?.GetValueOrDefault(p.Note.Id) ?? (p.InRange && !p.Ambiguous ? p.Candidates[0] : null);
            if (target is null || !p.Candidates.Contains(target)) continue;
            moves.Add(new KeySyncMove(p.Track, p.Note.Id, tempo.RoundTick(target.Time), target));
        }

        return moves;
    }

    private static int LowerBound(double[] sorted, double value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sorted[mid] < value) lo = mid + 1; else hi = mid;
        }

        return lo;
    }
}

namespace OsuHitsound.Lanes.Keysound;

/// <summary>
/// Note operations of the key editor, after Cubase's Key Editor: split, glue, quantize, duplicate, paste and
/// navigation. Pure functions over note lists; the session applies the results as one undo step.
/// </summary>
public static class KeyEditing
{
    /// <summary>Notes ordered as navigation and duplication see them: by start, then pitch.</summary>
    public static IEnumerable<KeyNote> InTimeOrder(IEnumerable<KeyNote> notes) => notes.OrderBy(n => n.Start).ThenBy(n => n.Pitch).ThenBy(n => n.Id);

    /// <summary>
    /// Splits every note that contains <paramref name="tick"/> (strictly inside it) into two at that tick. The left part
    /// keeps the note's id, so its gameplay assignment (later) stays with the start. Returns the replacements.
    /// </summary>
    public static (List<KeyNote> Changed, List<KeyNote> Added) Split(IEnumerable<KeyNote> notes, long tick)
    {
        var changed = new List<KeyNote>();
        var added = new List<KeyNote>();
        foreach (var n in notes.Where(n => n.Start < tick && tick < n.End))
        {
            changed.Add(n with { Length = tick - n.Start });
            added.Add(n with { Id = Guid.NewGuid(), Start = tick, Length = n.End - tick, Target = null }); // the right part starts elsewhere
        }

        return (changed, added);
    }

    /// <summary>
    /// Cubase's Glue: joins a note with the next note of the same pitch that starts at or after its start, so the result
    /// runs to the end of that note. Null when there is none.
    /// </summary>
    public static (KeyNote Joined, KeyNote Removed)? Glue(IReadOnlyList<KeyNote> notes, KeyNote note)
    {
        var next = InTimeOrder(notes.Where(n => n.Pitch == note.Pitch && n.Id != note.Id && n.Start >= note.Start)).FirstOrDefault();
        if (next is null) return null;
        return (note with { Length = Math.Max(note.Length, next.End - note.Start) }, next);
    }

    /// <summary>Moves note starts onto the grid (lengths unchanged). <paramref name="snapTick"/> maps a start to its grid tick.</summary>
    public static List<KeyNote> Quantize(IEnumerable<KeyNote> notes, Func<long, long> snapTick) =>
        [.. notes.Select(n => n with { Start = snapTick(n.Start) }).Where(n => n.IsValid)];

    /// <summary>Copies of the notes placed right after them (Cubase's Duplicate): starting where the selection ends.</summary>
    public static List<KeyNote> Duplicate(IReadOnlyCollection<KeyNote> notes) => notes.Count == 0 ? [] : PasteAt(notes, notes.Max(n => n.End));

    /// <summary>
    /// Moves the notes' starts by <paramref name="delta"/> ticks keeping their ends, never shorter than
    /// <paramref name="minLength"/> (a note already shorter keeps its length).
    /// </summary>
    public static List<KeyNote> ResizeStart(IEnumerable<KeyNote> notes, long delta, long minLength) =>
    [
        .. notes.Select(n =>
        {
            long d = Math.Min(delta, Math.Max(0, n.Length - minLength));
            return n with { Start = n.Start + d, Length = n.Length - d };
        }),
    ];

    /// <summary>Copies of the notes with the earliest one starting at <paramref name="at"/>, keeping their spacing.</summary>
    public static List<KeyNote> PasteAt(IReadOnlyCollection<KeyNote> notes, long at)
    {
        if (notes.Count == 0) return [];
        long offset = at - notes.Min(n => n.Start);
        return [.. notes.Select(n => n with { Id = Guid.NewGuid(), Start = n.Start + offset, Target = null })]; // copies aren't synced
    }

    /// <summary>
    /// The note after (or before) the current one in time order, for Left/Right navigation. With nothing selected, the
    /// first (or last) note.
    /// </summary>
    public static KeyNote? Neighbour(IEnumerable<KeyNote> notes, KeyNote? current, int direction)
    {
        var ordered = InTimeOrder(notes).ToList();
        if (ordered.Count == 0) return null;
        if (current is null) return direction > 0 ? ordered[0] : ordered[^1];
        int i = ordered.FindIndex(n => n.Id == current.Id);
        if (i < 0) return null;
        int j = i + Math.Sign(direction);
        return j >= 0 && j < ordered.Count ? ordered[j] : null;
    }

    /// <summary>The note and every later note of its pitch (Cubase's Shift+double-click).</summary>
    public static IEnumerable<KeyNote> FollowingOfPitch(IEnumerable<KeyNote> notes, KeyNote from) =>
        notes.Where(n => n.Pitch == from.Pitch && n.Start >= from.Start);
}

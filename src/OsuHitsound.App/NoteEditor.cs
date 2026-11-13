using OsuHitsound.Lanes;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

/// <summary>A range marked with the Range tool: a time span (ms, end excluded) over the pitches from Low to High.</summary>
internal sealed record KeyRange(double Start, double End, int Low, int High);

/// <summary>Which notes Scale by velocity looks at, as Scale by volume's scopes.</summary>
internal enum VelocityScope { Selected, SelectedTrack, AllTracks }

/// <summary>
/// The Keysound editor's notes selection and the editing commands that act on it (keyboard, menus and dialogs): select,
/// delete, transpose, nudge, quantize, repeat, copy, cut and paste, Edit range and Scale by velocity, with snapping. The
/// Keysound twin of <see cref="HitEditor"/>: kept apart from the drawing so it can be tested on its own. It edits the
/// selected track of the attached document; the piano roll draws it and turns pointer gestures into changes to it.
/// </summary>
internal sealed class NoteEditor
{
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private KeysoundDocument? doc;
    private double[] objectMoments = [];
    // Copied notes, kept when another project opens (not the system clipboard).
    private IReadOnlyList<KeyNote> clipboard = [];

    /// <summary>
    /// The selected notes' ids, on the selected track. Pointer gestures change it directly while dragging and call
    /// <see cref="Notify"/> after.
    /// </summary>
    public HashSet<Guid> SelectedIds { get; } = [];

    /// <summary>Raised when the selection or the range changes through a command.</summary>
    public event Action? Changed;

    /// <summary>A note to play once: Left/Right selected it (pitch, velocity).</summary>
    public event Action<int, int>? AuditionRequested;

    /// <summary>The open project; changing it empties the selection and the range (the clipboard is kept, to paste into another project).</summary>
    public KeysoundDocument? Document
    {
        get => doc;
        set
        {
            doc = value;
            SelectedIds.Clear();
            Range = null;
            objectMoments = value is null ? [] : [.. value.Objects.SelectMany(o => o.Nodes.Select(n => n.Time)).Order()];
        }
    }

    /// <summary>The range marked with the Range tool, kept apart from the selection and the locators; null when none.</summary>
    public KeyRange? Range { get; set; }

    public bool SnapOn { get; set; } = true;
    public SnapSpacing Spacing { get; set; } = new(1, 4);
    public SnapMode SnapMode { get; set; } = SnapMode.Grid;

    /// <summary>The selected track, or null when none.</summary>
    public KeyTrack? Track => doc?.SelectedTrack is { } id ? doc.Session.State.Track(id) : null;

    /// <summary>The selected notes of the selected track.</summary>
    public IReadOnlyList<KeyNote> Selected => SelectedNotes();

    /// <summary>True when Paste has notes to paste.</summary>
    public bool CanPaste => clipboard.Count > 0;

    public void Notify() => Changed?.Invoke();

    // Cubase's names: middle C (60) is C3.
    public static string PitchName(int pitch) => $"{NoteNames[pitch % 12]}{pitch / 12 - 2}";

    // Snapping.

    /// <summary>One snap step in ticks. Ticks count beats, so one step is the same number of ticks under every red line.</summary>
    public long StepTicks => Math.Max(1, TempoMap.Ppq * Spacing.Beats / Spacing.Divisions);

    /// <summary>The shortest a note can be made by resizing: one snap step, or one tick with Snap off.</summary>
    public long MinLength => SnapOn ? StepTicks : 1;

    /// <summary>A time on the snap: the grid, or with snap mode Objects the nearest gameplay-object moment.</summary>
    public double SnapTime(double time)
    {
        if (!SnapOn) return time;
        if (SnapMode != SnapMode.Objects || objectMoments.Length == 0) return doc!.Tempo.Snap(time, Spacing.Beats, Spacing.Divisions);
        int next = Sorted.LowerBound(objectMoments, time, x => x);
        if (next == 0) return objectMoments[0];
        if (next == objectMoments.Length) return objectMoments[^1];
        double before = objectMoments[next - 1], after = objectMoments[next];
        return time - before <= after - time ? before : after;
    }

    /// <summary>
    /// Where a note at <paramref name="time"/> lands when dragged by <paramref name="raw"/> ms: on the snap, or with Grid
    /// relative by whole grid steps, keeping its offset from its own grid line (as in the Hitsound editor).
    /// </summary>
    public double SnapMoved(double time, double raw) => SnapOn && SnapMode == SnapMode.GridRelative
        ? time + doc!.Tempo.Snap(time + raw, Spacing.Beats, Spacing.Divisions) - doc.Tempo.Snap(time, Spacing.Beats, Spacing.Divisions)
        : SnapTime(time + raw);

    /// <summary>The grid cell's start at or before a time: where Draw puts a note.</summary>
    public double FloorSnap(double time)
    {
        if (!SnapOn) return time;
        if (SnapMode == SnapMode.Objects) return SnapTime(time);
        double snapped = doc!.Tempo.Snap(time, Spacing.Beats, Spacing.Divisions);
        return snapped <= time + 0.01 ? snapped : doc.Tempo.Snap(time - Spacing.Step(doc.Tempo.SegmentAtTime(time).BeatLength), Spacing.Beats, Spacing.Divisions);
    }

    /// <summary>The next grid line from <paramref name="time"/> in <paramref name="direction"/>, at the snap's spacing.</summary>
    public double GridStep(double time, int direction)
    {
        if (doc is null) return time;
        double step = Spacing.Step(doc.Tempo.SegmentAtTime(time).BeatLength);
        double target = doc.Tempo.Snap(time + direction * step * 0.51, Spacing.Beats, Spacing.Divisions);
        return Math.Max(0, Math.Abs(target - time) < 0.5 ? time + direction * step : target);
    }

    // The selection.

    /// <summary>Called when the selected track changes: the selection belongs to one track.</summary>
    public void TrackChanged()
    {
        SelectedIds.Clear();
        Notify();
    }

    /// <summary>Drops selected ids that are no longer notes of the selected track (after an undo, say).</summary>
    public void KeepExisting()
    {
        if (Track is { } t) SelectedIds.IntersectWith(t.Notes.Select(n => n.Id));
        else SelectedIds.Clear();
    }

    public void SelectAll()
    {
        if (Track is not { } t) return;
        SelectedIds.UnionWith(t.Notes.Select(n => n.Id));
        Notify();
    }

    /// <summary>Esc: selects nothing and drops the range, as in the Hitsound editor.</summary>
    public void Deselect()
    {
        Range = null;
        SelectedIds.Clear();
        Notify();
    }

    /// <summary>Selects exactly these notes.</summary>
    public void SelectOnly(IEnumerable<Guid> ids)
    {
        SelectedIds.Clear();
        SelectedIds.UnionWith(ids);
        Notify();
    }

    private List<KeyNote> SelectedNotes() => Track is { } t ? [.. t.Notes.Where(n => SelectedIds.Contains(n.Id))] : [];

    /// <summary>Left/Right: the previous or next note in time; with <paramref name="extend"/> (Shift) it is added.</summary>
    public void SelectNeighbour(int direction, bool extend)
    {
        if (Track is not { } t) return;
        var ordered = KeyEditing.InTimeOrder(SelectedNotes()).ToList();
        var current = direction > 0 ? ordered.LastOrDefault() : ordered.FirstOrDefault();
        if (KeyEditing.Neighbour(t.Notes, current, direction) is not { } next) return;
        if (!extend) SelectedIds.Clear();
        SelectedIds.Add(next.Id);
        AuditionRequested?.Invoke(next.Pitch, next.Velocity);
        Notify();
    }

    /// <summary>Selects the selected track's notes that <paramref name="match"/> (the controller's velocity range).</summary>
    public int SelectWhere(Func<KeyNote, bool> match)
    {
        if (Track is not { } t) return 0;
        var ids = t.Notes.Where(match).Select(n => n.Id).ToList();
        SelectOnly(ids);
        return ids.Count;
    }

    public string SelectionText()
    {
        if (Track is null || SelectedIds.Count == 0) return "No notes selected";
        var notes = SelectedNotes();
        if (notes.Count != 1) return $"{notes.Count} notes selected";
        var n = notes[0];
        var (bar, beat) = doc!.Tempo.BarBeatAt(doc.Tempo.TimeAt(n.Start));
        return $"{PitchName(n.Pitch)} · bar {bar}.{beat} · {n.Length / (double)TempoMap.Ppq:0.###} beats · velocity {n.Velocity}";
    }

    // Commands on the selection.

    public void DeleteSelection(string label = "Delete notes")
    {
        if (Track is not { } t || SelectedIds.Count == 0) return;
        doc!.Session.RemoveNotes(t.Id, SelectedIds.ToHashSet(), label);
        SelectedIds.Clear();
        Notify();
    }

    /// <summary>Puts the selected notes on the clipboard.</summary>
    public void Copy() => clipboard = SelectedNotes();

    /// <summary>Copy, then delete the selection.</summary>
    public void Cut()
    {
        Copy();
        DeleteSelection();
    }

    /// <summary>Pastes the clipboard with its earliest note at <paramref name="time"/>; the pasted notes become the selection.</summary>
    public void Paste(double time)
    {
        if (Track is not { } t || clipboard.Count == 0) return;
        var pasted = KeyEditing.PasteAt(clipboard, doc!.Tempo.RoundTick(time));
        doc.Session.AddNotes(t.Id, pasted, "Paste");
        SelectOnly(pasted.Select(n => n.Id));
    }

    /// <summary>Ctrl+D: copies of the selection right after it; the copies become the selection.</summary>
    public void Duplicate()
    {
        if (Track is not { } t || SelectedNotes() is not { Count: > 0 } notes) return;
        var copies = KeyEditing.Duplicate(notes);
        doc!.Session.AddNotes(t.Id, copies, "Duplicate");
        SelectOnly(copies.Select(n => n.Id));
    }

    public void Transpose(int semitones)
    {
        if (Track is not { } t || SelectedIds.Count == 0) return;
        var notes = SelectedNotes();
        semitones = Math.Clamp(semitones, KeyNote.MinPitch - notes.Min(n => n.Pitch), KeyNote.MaxPitch - notes.Max(n => n.Pitch));
        if (semitones == 0) return;
        doc!.Session.UpdateNotes(t.Id, [.. notes.Select(n => n with { Pitch = n.Pitch + semitones })], "Transpose");
    }

    /// <summary>The info bar's velocity edit: a new velocity for every selected note, as one undo step.</summary>
    public void SetSelectionVelocity(Func<int, int> velocity)
    {
        if (Track is not { } track) return;
        var notes = SelectedNotes();
        if (notes.Count == 0) return;
        doc!.Session.UpdateNotes(track.Id, [.. notes.Select(n => n with { Velocity = Math.Clamp(velocity(n.Velocity), KeyNote.MinVelocity, KeyNote.MaxVelocity) })], "Velocity");
    }

    /// <summary>Quantize, as the Hitsound editor's: the selected notes move <paramref name="strength"/> % of the way to the grid.</summary>
    public void QuantizeSelection(double strength = 100)
    {
        if (Track is not { } t || SelectedNotes() is not { Count: > 0 } notes) return;
        var tempo = doc!.Tempo;
        double f = Math.Clamp(strength, 1, 100) / 100;
        doc.Session.UpdateNotes(t.Id, KeyEditing.Quantize(notes, tick =>
        {
            double at = tempo.TimeAt(tick);
            return tempo.RoundTick(at + (SnapTime(at) - at) * f);
        }), "Quantize");
    }

    /// <summary>Repeat's gap: the selection's length in snap steps, at least one.</summary>
    public int DefaultRepeatGap()
    {
        var notes = SelectedNotes();
        if (notes.Count == 0) return 1;
        long span = notes.Max(n => n.End) - notes.Min(n => n.Start);
        return Math.Max(1, (int)Math.Ceiling((double)span / StepTicks - 0.01));
    }

    /// <summary>Ctrl+R, as in the Hitsound editor: <paramref name="copies"/> copies of the selection, each <paramref name="gap"/> snap steps after the last, as one step.</summary>
    public void Repeat(int copies, int gap)
    {
        if (Track is not { } t || SelectedNotes() is not { Count: > 0 } notes || copies is < 1 or > HitEditor.MaxRepeatCopies || gap is < 1 or > HitEditor.MaxRepeatGap) return;
        long first = notes.Min(n => n.Start), step = gap * StepTicks;
        var added = new List<KeyNote>();
        for (int c = 1; c <= copies; c++) added.AddRange(KeyEditing.PasteAt(notes, first + c * step));
        doc!.Session.AddNotes(t.Id, added, "Repeat");
        SelectOnly(added.Select(n => n.Id));
    }

    /// <summary>Moves the selected notes by snap steps.</summary>
    public void Nudge(int steps)
    {
        if (Track is not { } t || SelectedIds.Count == 0) return;
        long dt = steps * StepTicks;
        doc!.Session.UpdateNotes(t.Id, [.. SelectedNotes().Select(n => n with { Start = n.Start + dt })], "Move notes");
    }

    /// <summary>Ctrl+Alt+Left/Right, as in the Hitsound editor: the selection moves so its first note lands on the previous or next grid line.</summary>
    public void NudgeToGrid(int direction)
    {
        if (Track is not { } t || SelectedNotes() is not { Count: > 0 } notes) return;
        var tempo = doc!.Tempo;
        long start = notes.Min(n => n.Start);
        long dt = tempo.RoundTick(GridStep(tempo.TimeAt(start), direction)) - start;
        if (dt != 0) doc.Session.UpdateNotes(t.Id, [.. notes.Select(n => n with { Start = Math.Max(0, n.Start + dt) })], "Move notes");
    }

    /// <summary>Cubase's Nudge Start/End: moves the selected notes' starts and/or ends by snap steps; the end stays put when the start moves, and a note keeps at least one step.</summary>
    public void NudgeEdges(int startSteps, int endSteps)
    {
        if (Track is not { } t || SelectedIds.Count == 0) return;
        long step = StepTicks, ds = startSteps * step, de = endSteps * step;
        var moved = SelectedNotes().Select(n =>
        {
            long start = Math.Max(0, n.Start + ds), end = n.End + de;
            if (end - start < MinLength) { if (ds != 0) start = end - MinLength; else end = start + MinLength; }
            return n with { Start = start, Length = end - start };
        }).ToList();
        doc!.Session.UpdateNotes(t.Id, moved, ds != 0 ? "Nudge start" : "Nudge end");
    }

    /// <summary>The info bar's Start: the selected notes move so the first starts at this tick (the others keep their spacing).</summary>
    public void MoveSelectionTo(long tick)
    {
        if (Track is not { } t || SelectedIds.Count == 0) return;
        var notes = SelectedNotes();
        long dt = Math.Max(0, tick) - notes.Min(n => n.Start);
        if (dt != 0) doc!.Session.UpdateNotes(t.Id, [.. notes.Select(n => n with { Start = n.Start + dt })], "Move notes");
    }

    // The range.

    /// <summary>The selected track's notes in the range: starting in it (end excluded) on its pitches.</summary>
    public IReadOnlyList<KeyNote> NotesInRange() =>
        Range is { } r && Track is { } t && doc is { } d
            ? [.. t.Notes.Where(n => n.Pitch >= r.Low && n.Pitch <= r.High && d.Tempo.TimeAt(n.Start) is var at && at >= r.Start - 0.5 && at < r.End - 0.5)]
            : [];

    /// <summary>
    /// Edit range, as in the Hitsound editor: copies, moves or deletes the notes in the range by an offset in ms (one undo
    /// step) and selects the copies or moved notes. Returns how many notes it changed.
    /// </summary>
    public int EditRange(RangeOperation operation, double offsetMs)
    {
        if (Track is not { } t || doc is not { } d) return 0;
        var notes = NotesInRange();
        if (notes.Count == 0) return 0;
        long Shift(KeyNote n) => Math.Max(0, d.Tempo.RoundTick(d.Tempo.TimeAt(n.Start) + offsetMs));
        switch (operation)
        {
            case RangeOperation.Delete:
                d.Session.RemoveNotes(t.Id, notes.Select(n => n.Id).ToHashSet(), "Range delete");
                SelectedIds.Clear();
                break;
            case RangeOperation.Move:
            {
                var moved = notes.Select(n => n with { Start = Shift(n) }).ToList();
                d.Session.UpdateNotes(t.Id, moved, "Range move");
                SelectOnly(moved.Select(n => n.Id));
                break;
            }
            default:
            {
                var copies = notes.Select(n => n with { Id = Guid.NewGuid(), Start = Shift(n), Target = null }).ToList();
                d.Session.AddNotes(t.Id, copies, "Range copy");
                SelectOnly(copies.Select(n => n.Id));
                break;
            }
        }

        return notes.Count;
    }

    // Scale by velocity.

    /// <summary>The notes Scale by velocity would change: those in scope with a velocity from–to (inclusive).</summary>
    public IReadOnlyList<(Guid Track, KeyNote Note)> NotesToScale(VelocityScope scope, int from, int to)
    {
        if (doc is null) return [];
        var tracks = scope == VelocityScope.AllTracks ? doc.Session.State.Tracks : Track is { } t ? [t] : [];
        return [.. tracks.SelectMany(tr => tr.Notes.Select(n => (tr.Id, n)))
            .Where(x => (scope != VelocityScope.Selected || SelectedIds.Contains(x.n.Id)) && x.n.Velocity >= from && x.n.Velocity <= to)];
    }

    /// <summary>Scale by velocity, as the Hitsound editor's Scale by volume: one undo step. Scaled velocities stay 1–127.</summary>
    public void ScaleVelocity(VelocityScope scope, int from, int to, double percent)
    {
        if (doc is null) return;
        var groups = NotesToScale(scope, from, to).GroupBy(x => x.Track).ToList();
        if (groups.Count == 0) return;
        doc.Session.Change("Scale by velocity", state => groups.Aggregate(state, (st, g) =>
        {
            var changed = g.ToDictionary(x => x.Note.Id, x => x.Note with
            {
                Velocity = Math.Clamp((int)Math.Round(x.Note.Velocity * percent / 100), KeyNote.MinVelocity, KeyNote.MaxVelocity),
            });
            return st.WithTrack(g.Key, tr => tr with { Notes = [.. tr.Notes.Select(n => changed.GetValueOrDefault(n.Id, n))] });
        }));
    }
}

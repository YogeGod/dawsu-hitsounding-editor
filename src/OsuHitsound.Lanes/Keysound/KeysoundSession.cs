using System.Collections.Immutable;

namespace OsuHitsound.Lanes.Keysound;

/// <summary>
/// The editable Keysound project: immutable <see cref="KeysoundState"/> snapshots with a labelled undo history. A mouse
/// drag is one gesture: <see cref="BeginGesture"/>, any number of changes, then <see cref="EndGesture"/> makes one step.
/// Mute and solo are playback-only and not part of Undo.
/// </summary>
public sealed class KeysoundSession
{
    private readonly Stack<(KeysoundState State, string Label)> undo = new(), redo = new();
    private KeysoundState? gestureStart;
    private string gestureLabel = "";
    private KeysoundState saved;

    public KeysoundSession(KeysoundState state)
    {
        State = saved = state;
    }

    public KeysoundState State { get; private set; }
    public event Action? Changed;

    public HashSet<Guid> Muted { get; } = [];
    public HashSet<Guid> Soloed { get; } = [];

    public bool IsEdited => !ReferenceEquals(State, saved);
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public string? UndoLabel => undo.Count > 0 ? undo.Peek().Label : null;
    public string? RedoLabel => redo.Count > 0 ? redo.Peek().Label : null;

    /// <summary>Marks a state (the one just written) as the one on disk.</summary>
    public void MarkSaved(KeysoundState state) => saved = state;

    /// <summary>True when a track sounds in editing playback: not muted, and soloed when any track is.</summary>
    public bool IsAudible(Guid track) => !Muted.Contains(track) && (Soloed.Count == 0 || Soloed.Contains(track));

    public void Change(string label, Func<KeysoundState, KeysoundState> change)
    {
        var next = change(State);
        if (ReferenceEquals(next, State)) return;
        if (gestureStart is null)
        {
            undo.Push((State, label));
            redo.Clear();
        }

        State = next;
        Changed?.Invoke();
    }

    public void BeginGesture(string label)
    {
        if (gestureStart is not null) EndGesture();
        gestureStart = State;
        gestureLabel = label;
    }

    /// <summary>Puts the state back to where the gesture began, keeping the gesture open (a drag recomputes from its start).</summary>
    public void ResetToGestureStart()
    {
        if (gestureStart is null || ReferenceEquals(State, gestureStart)) return;
        State = gestureStart;
        Changed?.Invoke();
    }

    public void EndGesture()
    {
        if (gestureStart is not { } start) return;
        gestureStart = null;
        if (ReferenceEquals(start, State)) return;
        undo.Push((start, gestureLabel));
        redo.Clear();
        Changed?.Invoke();
    }

    public void CancelGesture()
    {
        ResetToGestureStart();
        gestureStart = null;
    }

    public void Undo() => Step(undo, redo);
    public void Redo() => Step(redo, undo);

    private void Step(Stack<(KeysoundState State, string Label)> from, Stack<(KeysoundState State, string Label)> to)
    {
        if (gestureStart is not null) CancelGesture();
        if (from.Count == 0) return;
        var (state, label) = from.Pop();
        to.Push((State, label));
        State = state;
        Changed?.Invoke();
    }

    // Track commands. Each is one undo step.

    public KeyTrack AddTrack(string name)
    {
        var track = KeyTrack.Create(name);
        Change("Add track", s => s with { Tracks = s.Tracks.Add(track) });
        return track;
    }

    /// <summary>Adds several tracks as one step (a MIDI import).</summary>
    public void AddTracks(IReadOnlyCollection<KeyTrack> tracks, string label)
    {
        if (tracks.Count == 0) return;
        Change(label, s => s with { Tracks = s.Tracks.AddRange(tracks) });
    }

    /// <summary>Replaces a track by a new version as one step (a recording pass).</summary>
    public void ReplaceTrack(KeyTrack track, string label) =>
        Change(label, s => s.Track(track.Id) is { } old && old != track ? s.WithTrack(track.Id, _ => track) : s);

    public void DeleteTrack(Guid id) => Change("Delete track", s => s.Track(id) is { } t ? s with { Tracks = s.Tracks.Remove(t) } : s);

    public void RenameTrack(Guid id, string name) =>
        Change("Rename track", s => s.Track(id)?.Name == name ? s : s.WithTrack(id, t => t with { Name = name }));

    public void SetIncludeInGenerate(Guid id, bool include) =>
        Change(include ? "Include in Generate" : "Exclude from Generate",
            s => s.Track(id)?.IncludeInGenerate == include ? s : s.WithTrack(id, t => t with { IncludeInGenerate = include }));

    // Note commands.

    public void AddNotes(Guid track, IReadOnlyCollection<KeyNote> notes, string label = "Draw note")
    {
        if (notes.Count == 0) return;
        Change(label, s => s.WithTrack(track, t => t with { Notes = t.Notes.AddRange(notes) }));
    }

    public void RemoveNotes(Guid track, IReadOnlySet<Guid> ids, string label = "Delete notes")
    {
        if (ids.Count == 0) return;
        Change(label, s => s.Track(track) is { } t && t.Notes.Any(n => ids.Contains(n.Id))
            ? s.WithTrack(track, t => t with { Notes = t.Notes.RemoveAll(n => ids.Contains(n.Id)) })
            : s);
    }

    /// <summary>One step that removes, changes and adds notes together (split, glue, paste over).</summary>
    public void ReplaceNotes(Guid track, IReadOnlySet<Guid> remove, IReadOnlyCollection<KeyNote> changed, IReadOnlyCollection<KeyNote> add, string label)
    {
        if (remove.Count == 0 && changed.Count == 0 && add.Count == 0) return;
        var byId = changed.ToDictionary(n => n.Id);
        Change(label, s => s.Track(track) is null ? s : s.WithTrack(track, t => t with
        {
            Notes = t.Notes.RemoveAll(n => remove.Contains(n.Id)).ConvertAll(n => byId.GetValueOrDefault(n.Id, n)).AddRange(add),
        }));
    }

    /// <summary>Replaces the notes with these ids by the given versions (moves, resizes, velocity and pitch changes).</summary>
    public void UpdateNotes(Guid track, IReadOnlyCollection<KeyNote> updated, string label)
    {
        if (updated.Count == 0) return;
        var byId = updated.ToDictionary(n => n.Id);
        Change(label, s =>
        {
            if (s.Track(track) is not { } t) return s;
            bool any = false;
            var notes = t.Notes.ConvertAll(n =>
            {
                if (!byId.TryGetValue(n.Id, out var u) || u == n) return n;
                any = true;
                return u;
            });
            return any ? s.WithTrack(track, x => x with { Notes = notes }) : s;
        });
    }
}

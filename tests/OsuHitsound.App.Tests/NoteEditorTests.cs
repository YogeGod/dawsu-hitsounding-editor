using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App.Tests;

public class NoteEditorTests
{
    // 120 BPM from 0 ms: a beat is 500 ms and 960 ticks, so the default 1/4 snap is 125 ms and 240 ticks.
    private const long Beat = TempoMap.Ppq, Step = Beat / 4;

    private static KeysoundDocument Document(IEnumerable<KeyNote> notes, IReadOnlyList<KeyObject>? objects = null)
    {
        var file = OsuFile.Parse("""
            osu file format v14
            [General]
            Mode:0
            [TimingPoints]
            0,500,4,2,0,45,1,0
            [HitObjects]
            """u8);
        var track = KeyTrack.Create("Piano") with { Notes = [.. notes] };
        return new KeysoundDocument
        {
            ProjectPath = "test.dawsuks",
            File = new KeysoundProjectFile(),
            Reference = file,
            Settings = MapSettings.Read(file),
            Tempo = new TempoMap([(0, 500, 4)]),
            Objects = objects ?? [],
            ComboColours = [],
            Session = new KeysoundSession(new KeysoundState([track])),
            SelectedTrack = track.Id,
        };
    }

    private static KeyNote Note(long start, int pitch = 60, long length = Step, int velocity = 100) => new(Guid.NewGuid(), start, length, pitch, velocity);

    private static (NoteEditor Editor, KeysoundDocument Doc) Editor(params KeyNote[] notes)
    {
        var doc = Document(notes);
        return (new NoteEditor { Document = doc }, doc);
    }

    private static IReadOnlyList<KeyNote> Notes(KeysoundDocument doc) => doc.Session.State.Track(doc.SelectedTrack!.Value)!.Notes;

    [Fact]
    public void Commands_act_on_the_selection_and_select_their_result()
    {
        var a = Note(0);
        var (editor, doc) = Editor(a, Note(Beat));
        int changes = 0;
        editor.Changed += () => changes++;

        editor.SelectWhere(n => n.Id == a.Id);
        editor.Duplicate();

        Assert.Equal(3, Notes(doc).Count);
        var copy = Assert.Single(editor.Selected);
        Assert.NotEqual(a.Id, copy.Id);
        Assert.Equal(a.End, copy.Start);
        Assert.Equal(2, changes);
        Assert.Equal("Duplicate", doc.Session.UndoLabel);
    }

    [Fact]
    public void Cut_and_paste_move_notes_to_the_given_time_and_the_clipboard_outlives_the_project()
    {
        var a = Note(Beat, 64);
        var (editor, doc) = Editor(a);
        editor.SelectAll();
        editor.Cut();
        Assert.Empty(Notes(doc));
        Assert.True(editor.CanPaste);

        var other = Document([]);
        editor.Document = other;
        Assert.Empty(editor.SelectedIds);
        editor.Paste(1000); // ms: two beats

        var pasted = Assert.Single(Notes(other));
        Assert.Equal((2 * Beat, 64), (pasted.Start, pasted.Pitch));
        Assert.Equal(pasted.Id, Assert.Single(editor.Selected).Id);
    }

    [Fact]
    public void Copy_with_nothing_selected_empties_the_clipboard()
    {
        var (editor, _) = Editor(Note(0));
        editor.SelectAll();
        editor.Copy();
        editor.Deselect();
        editor.Copy();
        Assert.False(editor.CanPaste);
    }

    [Fact]
    public void Transpose_stops_at_the_pitch_limits()
    {
        var (editor, doc) = Editor(Note(0, 120), Note(Beat, 60));
        editor.SelectAll();
        editor.Transpose(12);
        Assert.Equal([127, 67], Notes(doc).Select(n => n.Pitch));
        editor.Transpose(1);
        Assert.Equal("Transpose", doc.Session.UndoLabel);
        Assert.Equal([127, 67], Notes(doc).Select(n => n.Pitch)); // no room left: nothing changes
    }

    [Fact]
    public void Nudge_moves_by_snap_steps_and_nudging_an_end_keeps_at_least_one_step()
    {
        var a = Note(Beat, length: 2 * Step);
        var (editor, doc) = Editor(a);
        editor.SelectAll();

        editor.Nudge(1);
        Assert.Equal(Beat + Step, Notes(doc)[0].Start);

        editor.NudgeEdges(0, -5);
        Assert.Equal(Step, Notes(doc)[0].Length);
        Assert.Equal("Nudge end", doc.Session.UndoLabel);
    }

    [Fact]
    public void Quantize_moves_starts_by_the_strength_toward_the_grid()
    {
        var (editor, doc) = Editor(Note(Step / 2 + 20)); // 140 ticks: the nearest 1/4 line is 240
        editor.SelectAll();
        editor.QuantizeSelection(50);
        Assert.Equal(190, Notes(doc)[0].Start);
        editor.QuantizeSelection();
        Assert.Equal(Step, Notes(doc)[0].Start);
    }

    [Fact]
    public void Repeat_places_copies_a_gap_of_snap_steps_apart_and_the_default_gap_covers_the_selection()
    {
        var (editor, doc) = Editor(Note(0, length: Step + Step / 2));
        editor.SelectAll();
        Assert.Equal(2, editor.DefaultRepeatGap());

        editor.Repeat(3, 2);
        Assert.Equal([0, 2 * Step, 4 * Step, 6 * Step], Notes(doc).Select(n => n.Start).Order());
        Assert.Equal(3, editor.Selected.Count);

        editor.Repeat(HitEditor.MaxRepeatCopies + 1, 1);
        Assert.Equal(4, Notes(doc).Count); // over the limit: nothing happens
    }

    [Fact]
    public void Edit_range_copies_moves_or_deletes_only_the_notes_starting_in_the_range_on_its_pitches()
    {
        var inside = Note(Beat, 62);
        var (editor, doc) = Editor(inside, Note(Beat, 70), Note(3 * Beat, 62));
        editor.Range = new KeyRange(500, 1000, 60, 65);
        Assert.Equal(inside.Id, Assert.Single(editor.NotesInRange()).Id);

        Assert.Equal(1, editor.EditRange(RangeOperation.Copy, 250));
        var copy = Assert.Single(editor.Selected);
        Assert.Equal((Beat + Beat / 2, 62), (copy.Start, copy.Pitch));
        Assert.Equal(4, Notes(doc).Count);

        doc.Session.Undo();
        Assert.Equal(1, editor.EditRange(RangeOperation.Delete, 0));
        Assert.DoesNotContain(Notes(doc), n => n.Id == inside.Id);
        Assert.Empty(editor.SelectedIds);

        editor.Range = new KeyRange(5000, 6000, 0, 127);
        Assert.Equal(0, editor.EditRange(RangeOperation.Move, 100));
    }

    [Fact]
    public void Scale_by_velocity_scales_the_notes_in_scope_and_range_as_one_step()
    {
        var soft = Note(0, velocity: 40);
        var (editor, doc) = Editor(soft, Note(Beat, velocity: 100));
        editor.SelectWhere(n => n.Id == soft.Id);

        Assert.Single(editor.NotesToScale(VelocityScope.Selected, 1, 127));
        Assert.Single(editor.NotesToScale(VelocityScope.SelectedTrack, 90, 127));

        editor.ScaleVelocity(VelocityScope.SelectedTrack, 1, 127, 200);
        Assert.Equal([80, 127], Notes(doc).Select(n => n.Velocity));
        doc.Session.Undo();
        Assert.Equal([40, 100], Notes(doc).Select(n => n.Velocity));
    }

    [Fact]
    public void Select_neighbour_steps_in_time_order_and_auditions_the_note()
    {
        var a = Note(0, 60);
        var b = Note(Beat, 67, velocity: 90);
        var (editor, _) = Editor(b, a);
        (int Pitch, int Velocity)? heard = null;
        editor.AuditionRequested += (p, v) => heard = (p, v);

        editor.SelectWhere(n => n.Id == a.Id);
        editor.SelectNeighbour(1, extend: false);
        Assert.Equal(b.Id, Assert.Single(editor.Selected).Id);
        Assert.Equal((67, 90), heard);

        editor.SelectNeighbour(-1, extend: true);
        Assert.Equal(2, editor.Selected.Count);
    }

    [Fact]
    public void Keep_existing_drops_notes_an_undo_removed_and_a_new_track_empties_the_selection()
    {
        var (editor, doc) = Editor(Note(0));
        editor.SelectAll();
        editor.Duplicate();
        doc.Session.Undo();
        editor.KeepExisting();
        Assert.Empty(editor.SelectedIds);

        editor.SelectAll();
        editor.TrackChanged();
        Assert.Empty(editor.SelectedIds);
    }

    [Fact]
    public void Esc_drops_the_range_and_a_new_project_drops_selection_and_range()
    {
        var (editor, _) = Editor(Note(0));
        editor.SelectAll();
        editor.Range = new KeyRange(0, 500, 0, 127);
        editor.Deselect();
        Assert.Null(editor.Range);
        Assert.Empty(editor.SelectedIds);

        editor.SelectAll();
        editor.Range = new KeyRange(0, 500, 0, 127);
        editor.Document = Document([]);
        Assert.Null(editor.Range);
        Assert.Empty(editor.SelectedIds);
    }

    [Fact]
    public void Snap_follows_the_grid_or_the_nearest_object_moment_and_is_off_when_snap_is_off()
    {
        var objects = new List<KeyObject> { new(330, 330, HitObjectKind.Circle, 1, 0, [(330, HitPosition.Circle)]) };
        var editor = new NoteEditor { Document = Document([], objects) };

        Assert.Equal(375, editor.SnapTime(380));
        Assert.Equal(250, editor.FloorSnap(370));
        Assert.Equal(375, editor.GridStep(250, 1));

        editor.SnapMode = SnapMode.Objects;
        Assert.Equal(330, editor.SnapTime(380));

        editor.SnapOn = false;
        Assert.Equal(380, editor.SnapTime(380));
        Assert.Equal(1, editor.MinLength);
    }

    [Fact]
    public void Selection_text_names_one_note_or_counts_several()
    {
        var a = Note(Beat, 60, length: 2 * Beat, velocity: 90);
        var (editor, _) = Editor(a, Note(2 * Beat));
        Assert.Equal("No notes selected", editor.SelectionText());
        editor.SelectWhere(n => n.Id == a.Id);
        Assert.Equal("C3 · bar 1.2 · 2 beats · velocity 90", editor.SelectionText());
        editor.SelectAll();
        Assert.Equal("2 notes selected", editor.SelectionText());
    }
}

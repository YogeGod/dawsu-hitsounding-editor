using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class HitEditorTests
{
    [Fact]
    public void Midi_resize_snaps_each_end_and_undoes_the_whole_gesture()
    {
        var e = Editor();
        var s = Session(e);
        var a = s.HitAt(new Lane(LaneSource.Skin, "soft-hitclap"), 2000);
        var b = s.HitAt(new Lane(LaneSource.Skin, "soft-hitclap"), 2250);
        var midi = new MidiHitData(0, 1, 60, 90, 0, 100);
        s.ImportMidi([(a, midi), (b, midi with { Duration = 200 })], new MidiDocument([], [], 480));
        var original = new Dictionary<EditHit, double> { [a] = 100, [b] = 200 };
        s.BeginGesture("Resize MIDI lengths");
        Assert.True(e.ResizeMidi(original, 65));
        Assert.Equal(125, s.MidiOf(a)!.Duration);
        Assert.Equal(250, s.MidiOf(b)!.Duration);
        s.EndGesture();
        s.Undo();
        Assert.Equal(100, s.MidiOf(a)!.Duration);
        Assert.Equal(200, s.MidiOf(b)!.Duration);
        s.Redo();
        Assert.Equal(250, s.MidiOf(b)!.Duration);
        e.SnapOn = false;
        Assert.True(e.ResizeMidi(original, -500));
        Assert.Equal(0, s.MidiOf(a)!.Duration);
        Assert.Equal(0, s.MidiOf(b)!.Duration);
    }

    [Fact]
    public void Midi_bar_geometry_includes_offscreen_starts_and_minimum_width()
    {
        Assert.True(ArrangeView.MidiBarContains(-100, 1000, 0.2, 0));
        Assert.False(ArrangeView.MidiBarContains(-100, 100, 0.2, 0));
        Assert.Equal(7, ArrangeView.MidiBarWidth(0, 0.2));
        Assert.True(ArrangeView.MidiBarEndHit(100, 0, 0.2, 107));
        Assert.False(ArrangeView.MidiBarEndHit(100, 1000, 0.2, 107));
    }

    [Fact]
    public void Midi_resize_returning_to_start_creates_no_undo_step()
    {
        var e = Editor();
        var s = Session(e);
        var hit = s.HitAt(new Lane(LaneSource.Skin, "soft-hitclap"), 2000);
        var midi = new MidiHitData(0, 1, 60, 90, 0, 100);
        s.ImportMidi([(hit, midi)], new MidiDocument([], [], 480));
        int before = s.Position;
        var original = new Dictionary<EditHit, double> { [hit] = 100 };
        s.BeginGesture("Resize MIDI lengths");
        Assert.True(e.ResizeMidi(original, 200));
        s.ResetToGestureStart();
        Assert.True(s.SetMidiDurations(original));
        s.EndGesture();
        Assert.Equal(before, s.Position);
        Assert.Equal(100, s.MidiOf(hit)!.Duration);
    }
    [Fact]
    public void Midi_metadata_follows_paste_repeat_and_cross_map_clipboard()
    {
        var e = Editor();
        var s = Session(e);
        var hit = Clap(e);
        var midi = new MidiHitData(2, 10, 38, 72, 9, 125);
        s.ImportMidi([(hit, midi)], new MidiDocument([], [], 480));
        e.Select([hit]);
        e.Copy();
        e.Paste(3000);
        Assert.Equal(midi, s.MidiOf(Assert.Single(e.Selected)));
        e.Repeat(2, 1);
        Assert.Equal(2, e.Selected.Count);
        Assert.All(e.Selected, h => Assert.Equal(midi, s.MidiOf(h)));
        s.Undo();
        Assert.DoesNotContain(s.State.Hits, h => h.Time == 3125);
        s.Redo();
        Assert.Equal(midi, s.MidiOf(s.State.Hits.Single(h => h.Time == 3125)));

        var another = Editor();
        e.Model = another.Model;
        e.Paste(4000);
        Assert.Equal(midi, Session(e).MidiOf(Assert.Single(e.Selected)));
    }

    // 120 BPM (500 ms beats), so a 1/4 snap step is 125 ms. Circles at 1000 (clap) and 1500.
    private static HitEditor Editor(string timing = "0,500,4,2,0,100,1,0")
    {
        var text = "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\nSampleSet: Soft\r\n\r\n" +
                   "[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
                   $"[TimingPoints]\r\n{timing.Replace("\n", "\r\n")}\r\n\r\n[HitObjects]\r\n256,192,1000,1,8,0:0:0:0:\r\n256,192,1500,1,0,0:0:0:0:\r\n";
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(text));
        var map = Decompiler.Decompile(file, new MapFolder([]));
        var session = new EditSession(file, new MapFolder([]), map);
        return new HitEditor { Model = new ArrangeModel(session, file, new HashSet<Lane>()) };
    }

    private static EditSession Session(HitEditor e) => e.Model!.Session;

    private static EditHit Clap(HitEditor e) => Session(e).State.Hits.Single(h => h.Lane.Name == "soft-hitclap");

    [Fact]
    public void Paste_lands_snapped_keeps_offsets_and_selects_what_was_pasted()
    {
        var e = Editor();
        e.Select(Session(e).State.Hits.Where(h => h.Time == 1000 || h.Time == 1500 && h.Lane.Name == "soft-hitnormal"));
        e.Copy();
        e.Paste(3010); // snaps to 3000

        Assert.Equal([3000.0, 3000, 3500], e.Selected.Select(h => h.Time).Order());
        Assert.All(e.Selected, h => Assert.Contains(h, Session(e).State.Hits));
    }

    [Fact]
    public void Duplicate_places_the_selection_after_itself_and_keeps_the_clipboard()
    {
        var e = Editor();
        e.Select([Clap(e)]);
        e.Copy();
        e.Select(Session(e).State.Hits.Where(h => h.Time == 1500));
        e.Duplicate();

        Assert.All(e.Selected, h => Assert.Equal(1625, h.Time)); // one snap step after a single moment
        e.Paste(4000);
        Assert.Equal("soft-hitclap", Assert.Single(e.Selected).Lane.Name); // still the clap from the clipboard
    }

    [Theory]
    [InlineData(false, false, 1125)]
    [InlineData(false, true, 1125)]
    public void Nudge_moves_by_a_snap_step_a_millisecond_or_to_the_next_grid_line(bool fine, bool toGrid, double expected)
    {
        var e = Editor();
        e.Select([Clap(e)]);
        e.Nudge(1, fine, toGrid);
        Assert.Equal(expected, Assert.Single(e.Selected).Time);
        Assert.Equal(expected, Clap(e).Time);
    }

    [Fact]
    public void A_fine_nudge_moves_a_hit_with_no_object_by_1_ms_and_to_grid_finds_the_next_line()
    {
        var e = Editor();
        e.SnapOn = false;
        e.Select([Clap(e)]);
        e.Copy();
        e.Paste(3010); // no object here
        e.Nudge(1, fine: true, toGrid: false);
        Assert.Equal(3011, Assert.Single(e.Selected).Time);
        e.Nudge(-1, fine: false, toGrid: true);
        Assert.Equal(3000, Assert.Single(e.Selected).Time);
    }

    [Fact]
    public void A_fine_nudge_cannot_pull_a_hit_off_its_object()
    {
        // Hits within 2 ms of an object belong to it, so 1 ms lands back on the object.
        var e = Editor();
        e.Select([Clap(e)]);
        e.Nudge(1, fine: true, toGrid: false);
        Assert.Equal(1000, Clap(e).Time);
    }

    [Fact]
    public void Shifting_lanes_moves_to_the_next_visible_lane()
    {
        var e = Editor();
        var lanes = e.Model!.LaneRows.Select(r => r.Lane).ToList();
        var clap = Clap(e);
        e.Select([clap]);
        e.ShiftLanes(1);
        Assert.Equal(lanes[lanes.IndexOf(clap.Lane) + 1], Assert.Single(e.Selected).Lane);
    }

    [Fact]
    public void Delete_clears_the_selection_and_undo_brings_the_hits_back()
    {
        var e = Editor();
        int before = Session(e).State.Hits.Count;
        e.SelectAll();
        e.DeleteSelection();
        Assert.Empty(e.Selected);
        Assert.Empty(Session(e).State.Hits);
        Session(e).Undo();
        Assert.Equal(before, Session(e).State.Hits.Count);
    }

    [Fact]
    public void Prune_drops_selected_hits_that_undo_removed()
    {
        var e = Editor();
        e.Select([Clap(e)]);
        e.Copy();
        e.Paste(3000);
        Session(e).Undo();
        int changes = 0;
        e.Changed += () => changes++;
        e.Prune();
        Assert.Empty(e.Selected);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Snap_off_pastes_at_the_exact_time_and_a_new_map_clears_the_selection()
    {
        var e = Editor();
        e.SnapOn = false;
        e.Select([Clap(e)]);
        e.Copy();
        e.Paste(3010);
        Assert.Equal(3010, Assert.Single(e.Selected).Time);

        e.Model = Editor().Model;
        Assert.Empty(e.Selected);
    }

    // ---- Range tool (docs/spec-wave1.md) ----

    private static int RowOf(HitEditor e, string lane) =>
        e.Model!.Rows.Select((r, i) => (r, i)).Single(x => x.r is LaneRow l && l.Lane.Name == lane).i;

    [Fact]
    public void A_range_covers_the_lanes_dragged_across_in_either_direction()
    {
        var e = Editor();
        int normal = RowOf(e, "soft-hitnormal"), clap = RowOf(e, "soft-hitclap");

        e.MarkRange(900, 1600, clap, normal);
        Assert.Equal(["soft-hitclap", "soft-hitnormal"], e.Range!.Lanes.Select(l => l.Name).Order());

        e.MarkRange(900, 1600, clap, clap);
        Assert.Equal("soft-hitclap", Assert.Single(e.Range!.Lanes).Name);
    }

    [Fact]
    public void A_range_snaps_its_ends_when_snap_is_on()
    {
        var e = Editor();
        int clap = RowOf(e, "soft-hitclap");
        e.MarkRange(1610, 1010, clap, clap);
        Assert.Equal((1000.0, 1625.0), (e.Range!.Start, e.Range.End));

        e.SnapOn = false;
        e.MarkRange(1010, 1610, clap, clap);
        Assert.Equal((1010.0, 1610.0), (e.Range!.Start, e.Range.End));
    }

    [Fact]
    public void Editing_the_range_selects_the_copies_and_clearing_forgets_it()
    {
        var e = Editor();
        int normal = RowOf(e, "soft-hitnormal"), clap = RowOf(e, "soft-hitclap");
        e.MarkRange(1000, 1500, normal, clap);

        e.EditRange(RangeOperation.Copy, 500);
        Assert.Equal([1500.0, 1500], e.Selected.Select(h => h.Time));
        Assert.Contains(Session(e).State.Hits, h => h.Time == 1500 && h.Lane.Name == "soft-hitclap");

        e.ClearRange();
        Assert.Null(e.Range);
        Assert.Empty(e.EditRange(RangeOperation.Delete, 0));
    }

    // ---- Scale by volume ----

    [Fact]
    public void Scale_by_volume_looks_at_the_selection_its_lanes_or_everything()
    {
        var e = Editor(); // circles at 1000 (normal + clap) and 1500 (normal), both at volume 100
        e.Select([Clap(e)]);

        Assert.Single(e.MomentsToScale(VolumeScope.Selected, 1, 100));
        Assert.Single(e.MomentsToScale(VolumeScope.SelectedLanes, 1, 100)); // the clap lane has only the circle at 1000
        Assert.Equal(2, e.MomentsToScale(VolumeScope.All, 1, 100).Count);
        Assert.Empty(e.MomentsToScale(VolumeScope.All, 1, 99));
    }

    // ---- Quantize, Repeat and Grid relative (docs/spec-wave2.md, parts 1-3) ----

    private static EditHit Loose(HitEditor e, double time)
    {
        var hit = Session(e).HitAt(new Lane(LaneSource.Skin, "soft-hitwhistle"), time);
        Session(e).Add([hit]);
        return hit;
    }

    [Fact]
    public void Quantize_moves_only_loose_hits_by_the_strength()
    {
        var e = Editor();
        var loose = Loose(e, 1260);
        e.Select([loose, Clap(e)]);

        Assert.Equal(1, e.Quantize(50));
        Assert.Contains(e.Selected, h => h.Time == 1255);
        Assert.Contains(e.Selected, h => h.Time == 1000 && h.Lane.Name == "soft-hitclap"); // on its circle: stays
        Assert.Equal("Quantize", Session(e).Steps[^1]);
    }

    [Fact]
    public void A_quantized_hit_that_lands_on_a_circle_plays_on_it()
    {
        var e = Editor();
        e.Select([Loose(e, 1495)]);
        e.Quantize(100);
        Assert.NotNull(Assert.Single(e.Selected).Anchor);
    }

    [Fact]
    public void Quantize_with_nothing_loose_changes_nothing()
    {
        var e = Editor();
        e.Select([Clap(e)]);
        int steps = Session(e).Steps.Count;
        Assert.Equal(0, e.Quantize(100));
        Assert.Equal(steps, Session(e).Steps.Count);
    }

    [Fact]
    public void Quantizing_a_loose_hit_already_on_the_grid_preserves_redo()
    {
        var e = Editor();
        var onGrid = Loose(e, 2000);
        Loose(e, 3000);
        Session(e).Undo();
        e.Select([onGrid]);
        int position = Session(e).Position;

        e.Quantize(100);

        Assert.Equal(position, Session(e).Position);
        Assert.True(Session(e).CanRedo);
        Session(e).Redo();
        Assert.Contains(Session(e).State.Hits, h => h.Time == 3000);
    }

    [Fact]
    public void Repeat_places_copies_a_number_of_snap_steps_apart_as_one_step()
    {
        var e = Editor();
        e.Select([Clap(e)]);
        Assert.Equal(1, e.DefaultRepeatGap()); // one moment: one snap step, as Duplicate

        e.Repeat(3, 4); // 4 steps of 1/4 at 120 BPM = 500 ms
        Assert.Equal([1500.0, 2000, 2500], e.Selected.Select(h => h.Time).Order());
        Assert.NotNull(e.Selected.Single(h => h.Time == 1500).Anchor);
        Assert.Equal("Repeat", Session(e).Steps[^1]);
    }

    [Fact]
    public void Repeat_counts_its_gap_on_the_grid_across_a_tempo_change()
    {
        var e = Editor("0,500,4,2,0,100,1,0\n2000,250,4,2,0,100,1,0"); // twice as fast from 2000
        e.Select([Clap(e)]);
        e.Repeat(1, 16); // 8 steps of 125 ms to 2000, then 8 of 62.5 ms
        Assert.Equal(2500, Assert.Single(e.Selected).Time);
    }

    [Fact]
    public void Repeat_counts_a_red_line_that_interrupts_a_snap_step()
    {
        var e = Editor("0,500,4,2,0,100,1,0\n1200,250,4,2,0,100,1,0");
        e.Select([Clap(e)]);
        e.Repeat(1, 3); // 1125, 1200 (new red line), 1262.5
        Assert.Equal(1262.5, Assert.Single(e.Selected).Time);
    }

    [Fact]
    public void Grid_relative_nudge_keeps_the_offset_even_for_the_to_grid_command()
    {
        var e = Editor();
        e.SnapMode = SnapMode.GridRelative;
        e.Select([Loose(e, 1010)]);
        e.Nudge(1, fine: false, toGrid: true);
        Assert.Equal(1135, Assert.Single(e.Selected).Time);
        e.Nudge(-1, fine: false, toGrid: true);
        Assert.Equal(1010, Assert.Single(e.Selected).Time);
    }

    [Fact]
    public void Snap_uses_a_red_line_that_interrupts_the_previous_grid()
    {
        var e = Editor("0,500,4,2,0,100,1,0\n1200,250,4,2,0,100,1,0");
        Assert.Equal(1200, e.Model!.Snap(1190, 4));
        Assert.Equal(1125, e.Model.Snap(1140, 4));
    }

    [Fact]
    public void Grid_relative_nudge_follows_interrupted_steps_in_both_directions()
    {
        var e = Editor("0,500,4,2,0,100,1,0\n1200,250,4,2,0,100,1,0");
        e.SnapMode = SnapMode.GridRelative;
        e.Select([Loose(e, 1135)]); // 10 ms after the 1125 grid line

        e.Nudge(1, fine: false, toGrid: true);
        Assert.Equal(1210, Assert.Single(e.Selected).Time);
        e.Nudge(1, fine: false, toGrid: true);
        Assert.Equal(1272.5, Assert.Single(e.Selected).Time);
        e.Nudge(-1, fine: false, toGrid: true);
        e.Nudge(-1, fine: false, toGrid: true);
        Assert.Equal(1135, Assert.Single(e.Selected).Time);
    }

    [Fact]
    public void Time_signature_changes_do_not_shift_the_snap_grid()
    {
        var e = Editor();
        var model = e.Model!;
        model.SetTimeSignatures([new TimeSignature(1, 3, 8), new TimeSignature(2, 4, 4)]);
        Assert.Equal(1000, model.Snap(900, 1)); // bar 2 starts at 750, but beats still start at 0, 500, 1000
        Assert.Equal(1000, model.StepsAfter(500, 1, 1));
        Assert.Contains(model.SubLines(750, 1000, 4), line => line.Time == 875);
    }

    [Fact]
    public void Grid_relative_keeps_a_hit_s_offset_from_its_grid_line()
    {
        var e = Editor();
        Assert.Equal(240, e.SnapOffset(1010, 240)); // grid: lands on the 1250 line
        e.SnapMode = SnapMode.GridRelative;
        Assert.Equal(250, e.SnapOffset(1010, 240)); // relative: stays 10 ms after its line, at 1260
        e.SnapOn = false;
        Assert.Equal(240, e.SnapOffset(1010, 240));
    }
}

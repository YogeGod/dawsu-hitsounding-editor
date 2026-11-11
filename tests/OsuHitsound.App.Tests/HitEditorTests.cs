using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class HitEditorTests
{
    // 120 BPM (500 ms beats), so a 1/4 snap step is 125 ms. Circles at 1000 (clap) and 1500.
    private static HitEditor Editor(string timing = "0,500,4,2,0,100,1,0", string? objects = null)
    {
        var text = "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\nSampleSet: Soft\r\n\r\n" +
                   "[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
                   $"[TimingPoints]\r\n{timing.Replace("\n", "\r\n")}\r\n\r\n[HitObjects]\r\n" +
                   (objects ?? "256,192,1000,1,8,0:0:0:0:\r\n256,192,1500,1,0,0:0:0:0:\r\n");
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(text));
        var map = Decompiler.Decompile(file, new MapFolder([]));
        var session = new EditSession(file, new MapFolder([]), map);
        return new HitEditor { Model = new ArrangeModel(session, file, new HashSet<Lane>()) };
    }

    private static EditSession Session(HitEditor e) => e.Model!.Session;

    private static EditHit Clap(HitEditor e) => Session(e).State.Hits.Single(h => h.Lane.Name == "soft-hitclap");

    [Fact]
    public void Protected_bulk_delete_keeps_hitnormals_and_undo_restores_additions()
    {
        var e = Editor();
        e.ProtectHitnormals = true;
        var before = Session(e).State;
        e.SelectAll();
        e.DeleteSelection();
        Assert.Equal(2, Session(e).State.Hits.Count);
        Assert.All(Session(e).State.Hits, h => Assert.Equal("hitnormal", h.Lane.Sample!.Value.Sound));
        Assert.Equal(2, e.Selected.Count);
        var after = Session(e).State;
        e.DeleteSelection();
        Assert.Same(after, Session(e).State); // All protected: no extra undo step.
        Session(e).Undo();
        Assert.Same(before, Session(e).State);
    }

    [Fact]
    public void Protected_erase_is_a_noop_until_the_setting_is_disabled()
    {
        var e = Editor();
        var normal = Session(e).State.Hits.First(h => h.Lane.Sample is { Sound: "hitnormal" });
        e.ProtectHitnormals = true;
        e.Select([normal]);
        var before = Session(e).State;
        e.Erase(normal);
        Assert.Same(before, Session(e).State);
        Assert.Contains(normal, e.Selected);
        e.ProtectHitnormals = false;
        e.Erase(normal);
        Assert.DoesNotContain(normal, Session(e).State.Hits);
    }

    [Fact]
    public void Protected_cut_copies_only_the_hits_it_removes()
    {
        var e = Editor();
        e.ProtectHitnormals = true;
        e.SelectAll();
        e.Cut();
        Assert.Equal(2, Session(e).State.Hits.Count);
        e.Paste(3000);
        var pasted = Assert.Single(e.Selected);
        Assert.Equal("hitclap", pasted.Lane.Sample!.Value.Sound);
        Assert.Equal(3000, pasted.Time);
        Assert.Equal(2, Session(e).State.Hits.Count(h => h.Lane.Sample is { Sound: "hitnormal" }));
    }

    [Fact]
    public void Protected_range_delete_keeps_normals_and_does_not_create_noop_history()
    {
        var e = Editor();
        e.ProtectHitnormals = true;
        var before = Session(e).State;
        e.MarkRange(0, 2000, 0, e.Model!.Rows.Count - 1);
        e.EditRange(RangeOperation.Delete, 0);
        Assert.Equal(2, Session(e).State.Hits.Count);
        Assert.All(Session(e).State.Hits, h => Assert.Equal("hitnormal", h.Lane.Sample!.Value.Sound));
        var after = Session(e).State;
        e.EditRange(RangeOperation.Delete, 0);
        Assert.Same(after, Session(e).State);
        Session(e).Undo();
        Assert.Same(before, Session(e).State);
    }

    [Theory]
    [InlineData("normal-hitnormal3.wav")]
    [InlineData("drum-hitnormal2.ogg")]
    public void Protection_also_covers_numbered_map_hitnormal_files(string name)
    {
        var e = Editor();
        var hit = Session(e).HitAt(new Lane(LaneSource.BeatmapFile, name), 2000);
        Session(e).Add([hit]);
        e.ProtectHitnormals = true;
        e.Select([hit]);
        e.DeleteSelection();
        Assert.Contains(hit, Session(e).State.Hits);
    }

    [Theory]
    [InlineData(null, 64)]
    [InlineData(0, 0)]
    [InlineData(35, 35)]
    [InlineData(100, 100)]
    [InlineData(200, 100)]
    public void Drawn_hits_use_insert_volume_without_changing_existing_hits(int? preset, int expected)
    {
        var e = Editor();
        var session = Session(e);
        var original = session.State.Hits.Single(h => h.Time == 1500);
        session.SetVolume([original.Anchor!.Value], 64);
        var before = session.State;
        var view = new ArrangeView { InsertVolume = preset };
        view.SetModel(e.Model);
        var lane = e.Model!.LaneRows.First(r => r.Lane.Name == "soft-hitwhistle").Lane;
        int? previewVolume = null;
        view.PlacementPreviewRequested += h => previewVolume = session.VolumeOf(h);
        view.PlaceHit(lane, 1500);
        var added = session.State.Hits.Single(h => h.Lane == lane && h.Time == 1500);
        Assert.Equal(expected, session.VolumeOf(added));
        Assert.Equal(expected, previewVolume);
        Assert.Equal(64, session.VolumeOf(original));
        Assert.Same(before.Volumes, session.State.Volumes);
        var after = session.State;
        view.InsertVolume = 12;
        view.PlaceHit(lane, 1500); // Painting over the same hit must not change it.
        Assert.Same(after, session.State);
        session.Undo();
        Assert.Same(before, session.State); // Adding and setting volume are one undo step.
        session.Redo();
        Assert.Equal(expected, session.VolumeOf(added));
    }

    [Fact]
    public void Drawn_loose_hits_retain_the_insert_volume()
    {
        var e = Editor();
        var view = new ArrangeView { InsertVolume = 28 };
        view.SetModel(e.Model);
        var lane = Clap(e).Lane;
        view.PlaceHit(lane, 1234);
        var added = Session(e).State.Hits.Single(h => h.Time == 1234);
        Assert.Null(added.Anchor);
        Assert.Equal(28, Session(e).VolumeOf(added));
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1200, 1000)]
    [InlineData(1250, 1000)]
    [InlineData(1300, 1500)]
    [InlineData(9000, 1500)]
    public void Objects_snap_to_nearest_gameplay_moment(double time, double expected)
    {
        var e = Editor();
        e.SnapMode = SnapMode.Objects;
        Assert.Equal(expected, e.SnapTime(time));
        Assert.Equal(expected - 1000, e.SnapOffset(1000, time - 1000));
        e.SnapOn = false;
        Assert.Equal(time, e.SnapTime(time));
        Assert.Equal(time - 1000, e.SnapOffset(1000, time - 1000));
    }

    [Fact]
    public void Objects_snap_includes_off_grid_slider_nodes_and_spinner_end()
    {
        var e = Editor(objects: "256,192,1103,2,0,L|396:192,2,140\r\n256,192,2300,8,0,2707,0:0:0:0:\r\n");
        e.SnapMode = SnapMode.Objects;
        Assert.Equal(1103, e.SnapTime(1110));
        Assert.Equal(1603, e.SnapTime(1610));
        Assert.Equal(2103, e.SnapTime(2110));
        Assert.Equal(2707, e.SnapTime(2710));
    }

    [Fact]
    public void Objects_snap_with_no_objects_keeps_the_requested_time()
    {
        var e = Editor(objects: "");
        e.SnapMode = SnapMode.Objects;
        Assert.Equal(1234, e.SnapTime(1234));
    }

    [Fact]
    public void Objects_nudge_steps_between_objects_and_stops_at_the_ends()
    {
        var e = Editor();
        e.SnapMode = SnapMode.Objects;
        e.Select([Clap(e)]);
        e.Nudge(1, false, false);
        Assert.Equal(1500, Assert.Single(e.Selected).Time);
        e.Nudge(1, false, false);
        Assert.Equal(1500, Assert.Single(e.Selected).Time);
        e.Nudge(-1, false, false);
        Assert.Equal(1000, Assert.Single(e.Selected).Time);
        e.Nudge(-1, false, false);
        Assert.Equal(1000, Assert.Single(e.Selected).Time);
    }

    [Fact]
    public void Lane_moves_request_the_destination_sample_and_clamped_moves_are_silent()
    {
        var e = Editor();
        var previews = new List<EditHit>();
        e.LaneMovePreviewRequested += hits => previews.AddRange(hits);
        e.Select([Clap(e)]);
        e.ShiftLanes(1);
        Assert.Equal(Assert.Single(e.Selected), Assert.Single(previews));
        e.ShiftLanes(-1);
        Assert.Equal(2, previews.Count);
        Assert.Equal(Clap(e).Lane, previews[^1].Lane);
        var lastLane = e.Model!.Rows.OfType<LaneRow>().Last().Lane;
        e.Select([Session(e).HitAt(lastLane, 1000)]);
        e.ShiftLanes(1);
        Assert.Equal(2, previews.Count);
    }

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
        // Every lane between the two, both ends included: the defaults run hitnormal, whistle, finish, clap.
        Assert.Equal(["soft-hitclap", "soft-hitfinish", "soft-hitnormal", "soft-hitwhistle"], e.Range!.Lanes.Select(l => l.Name).Order());

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
    public void Repeat_restarts_on_a_red_lines_grid_when_a_step_would_cross_it()
    {
        var e = Editor("0,500,4,2,0,100,1,0\n2100,250,4,2,0,100,1,0"); // 2100 is not on the first grid
        e.Select([Clap(e)]);
        e.Repeat(1, 16); // 8 steps to 2000, one stopping on the red line at 2100, then 7 of 62.5 ms
        Assert.Equal(2537.5, Assert.Single(e.Selected).Time);
    }

    [Fact]
    public void Quantize_on_loose_hits_already_on_the_grid_is_not_an_edit()
    {
        var e = Editor();
        e.Select([Loose(e, 1250)]);
        int steps = Session(e).Steps.Count;
        Assert.Equal(0, e.Quantize(100));
        Assert.Equal(steps, Session(e).Steps.Count);
    }

    [Fact]
    public void Grid_relative_keeps_a_hit_s_offset_from_its_grid_line()
    {
        var e = Editor();
        Assert.Equal(240, e.SnapOffset(1010, 230)); // grid: lands on the 1250 line
        e.SnapMode = SnapMode.GridRelative;
        Assert.Equal(250, e.SnapOffset(1010, 240)); // relative: stays 10 ms after its line, at 1260
        e.SnapOn = false;
        Assert.Equal(240, e.SnapOffset(1010, 240));
    }
}

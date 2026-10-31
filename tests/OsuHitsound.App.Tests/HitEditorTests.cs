using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class HitEditorTests
{
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

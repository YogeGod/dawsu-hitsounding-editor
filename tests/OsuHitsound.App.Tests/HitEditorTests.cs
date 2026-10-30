using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class HitEditorTests
{
    // 120 BPM (500 ms beats), so a 1/4 snap step is 125 ms. Circles at 1000 (clap) and 1500.
    private static HitEditor Editor()
    {
        var text = "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\nSampleSet: Soft\r\n\r\n" +
                   "[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
                   "[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n256,192,1000,1,8,0:0:0:0:\r\n256,192,1500,1,0,0:0:0:0:\r\n";
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
}

using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Tests;

public class LiveReloadMergeTests
{
    private static EditSession Load(string hitObjects)
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(
            "osu file format v14\r\n\r\n[General]\r\nSampleSet: Soft\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
            $"[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n{hitObjects.Replace("\n", "\r\n")}\r\n"));
        var folder = new MapFolder([]);
        return new EditSession(file, folder, Decompiler.Decompile(file, folder));
    }

    private static readonly Lane Clap = new(LaneSource.Skin, "soft-hitclap"), Whistle = new(LaneSource.Skin, "soft-hitwhistle"),
        Normal = new(LaneSource.Skin, "soft-hitnormal");

    private static bool Has(EditSession s, Lane lane, double time) => s.State.Hits.Any(h => h.Lane == lane && Math.Abs(h.Time - time) < 1);

    [Fact]
    public void Unsaved_edits_land_on_osus_new_version_by_time()
    {
        // Opened: circles at 1000 and 1500 (no additions). Edited here: a clap at 1000, the 1500 volume to 40.
        var old = Load("256,192,1000,1,0,0:0:0:0:\n256,192,1500,1,0,0:0:0:0:");
        var baseline = old.State;
        old.Add([old.HitAt(Clap, 1000)]);
        old.SetVolumes(new Dictionary<MomentKey, int> { [old.MomentAt(1500)!.Value] = 40 });
        var diff = LiveReloadMerge.Between(old, baseline);
        Assert.False(diff.OtherEdits);

        // osu! saved meanwhile: a new circle at 750 shifts every object index, and it whistled the one at 1500.
        var reloaded = Load("256,192,750,1,0,0:0:0:0:\n256,192,1000,1,0,0:0:0:0:\n256,192,1500,1,2,0:0:0:0:");
        var result = LiveReloadMerge.Apply(reloaded, diff);

        Assert.True(Has(reloaded, Clap, 1000));                 // this edit carried over onto the moved index
        Assert.True(Has(reloaded, Whistle, 1500));              // osu!'s own hitsound change kept too
        Assert.True(Has(reloaded, Normal, 750));                // osu!'s new object untouched
        Assert.Equal(40, reloaded.State.Volumes[reloaded.MomentAt(1500)!.Value]);
        Assert.Equal(0, result.WithoutObject);
        Assert.True(reloaded.IsEdited);                         // still unsaved here, and one undo step
        Assert.Equal(["Keep unsaved edits"], reloaded.Steps);
    }

    [Fact]
    public void An_edit_on_an_object_osu_deleted_stays_as_a_hit_without_object()
    {
        var old = Load("256,192,1000,1,0,0:0:0:0:\n256,192,1500,1,0,0:0:0:0:");
        var baseline = old.State;
        old.Add([old.HitAt(Clap, 1500)]);
        var reloaded = Load("256,192,1000,1,0,0:0:0:0:"); // osu! deleted the 1500 circle
        var result = LiveReloadMerge.Apply(reloaded, LiveReloadMerge.Between(old, baseline));
        var clap = Assert.Single(reloaded.State.Hits, h => h.Lane == Clap);
        Assert.Null(clap.Anchor);
        Assert.Equal(1500, clap.Time);
        Assert.Equal(1, result.WithoutObject);
    }

    [Fact]
    public void A_hit_removed_here_stays_removed_after_reload()
    {
        var old = Load("256,192,1000,1,8,0:0:0:0:"); // a clap from the file
        var baseline = old.State;
        old.Remove(old.State.Hits.Where(h => h.Lane == Clap).ToList());
        var reloaded = Load("256,192,1000,1,8,0:0:0:0:\n256,192,2000,1,0,0:0:0:0:");
        LiveReloadMerge.Apply(reloaded, LiveReloadMerge.Between(old, baseline));
        Assert.False(Has(reloaded, Clap, 1000));
        Assert.True(Has(reloaded, Normal, 2000));
    }

    [Fact]
    public void A_layer_volume_cleared_here_stays_cleared_after_reload()
    {
        // The clap had its own layer volume (30); setting the moment's volume to 70 here cleared it.
        const string map = "256,192,1000,1,8,0:0:0:0:";
        var old = Load(map);
        var clap = old.State.Hits.Single(h => h.Lane == Clap);
        old.SetLayerVolumes(new Dictionary<EditHit, int> { [clap] = 30 });
        var baseline = old.State;
        old.SetVolumes(new Dictionary<MomentKey, int> { [old.MomentAt(1000)!.Value] = 70 });
        var diff = LiveReloadMerge.Between(old, baseline);

        // The reloaded map still has the old layer volume (as osu!'s save of it would).
        var reloaded = Load(map);
        reloaded.SetLayerVolumes(new Dictionary<EditHit, int> { [reloaded.State.Hits.Single(h => h.Lane == Clap)] = 30 });
        LiveReloadMerge.Apply(reloaded, diff);
        Assert.Equal(70, reloaded.VolumeOf(reloaded.State.Hits.Single(h => h.Lane == Clap)));
    }

    [Fact]
    public void No_unsaved_edits_means_nothing_to_apply_and_other_kinds_are_reported()
    {
        var old = Load("256,192,1000,1,0,0:0:0:0:");
        var baseline = old.State;
        Assert.True(LiveReloadMerge.Between(old, baseline).IsEmpty);
        old.AddBookmark(1200);
        var diff = LiveReloadMerge.Between(old, baseline);
        Assert.True(diff.IsEmpty);
        Assert.True(diff.OtherEdits); // a bookmark isn't carried: the caller asks before reloading
    }
}

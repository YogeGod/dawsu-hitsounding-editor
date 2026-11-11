using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Tests;

public class HsModeTests
{
    internal static byte[] Bytes(string objects = "", string version = "Source", int format = 14, int mode = 0) => Encoding.UTF8.GetBytes(
        $"osu file format v{format}\n[General]\nMode:{mode}\nSampleSet: Soft\n[Metadata]\nVersion:{version}\nArtist:Test\nTitle:HS\nCreator:Test\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n" + objects);
    internal static Lane Normal => new(LaneSource.Skin, "soft-hitnormal");
    internal static Lane Clap => new(LaneSource.Skin, "soft-hitclap");
    internal static MapDocument Doc(string objects = "", string version = "Source", int format = 14) =>
        MapDocument.FromBytes("source.osu", Bytes(objects, version, format), new MapFolder([]));

    [Fact]
    public void New_HS_circles_keep_the_insert_volume_and_undo_as_one_edit()
    {
        var session = Doc().Session;
        session.SetHsMode(true);
        var before = session.State;
        var hit = session.HitAt(Clap, 1000);
        session.Add([hit], volume: 37);
        Assert.Equal(37, session.VolumeOf(Assert.Single(session.State.Hits)));
        Assert.Single(session.State.HsCircles);
        session.Undo();
        Assert.Same(before, session.State);
    }

    [Theory]
    [InlineData("HS", true)]
    [InlineData("hitsounds", true)]
    [InlineData("Song [hItSoUnDs]", true)]
    [InlineData("Song HS draft", true)]
    [InlineData("HS2", false)]
    [InlineData("myhitsounds", false)]
    [InlineData("ÉHS", false)]
    public void Eligibility_recognizes_only_standalone_names(string name, bool expected)
    {
        var doc = Doc("256,192,1000,1,0,0:0:0:0:\n", name);
        Assert.Equal(expected, doc.Session.HsEligible);
        Assert.Equal(expected, doc.Session.SetHsMode(true));
    }

    [Fact]
    public void Initially_empty_standard_maps_qualify_but_other_game_modes_do_not()
    {
        Assert.True(Doc().Session.HsEligible);
        Assert.False(MapDocument.FromBytes("mania.osu", Bytes(mode: 3), new MapFolder([])).Session.HsEligible);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Raw_import_hits_reuse_existing_owned_or_protected_objects(bool protectedObject)
    {
        var session = protectedObject ? Doc("256,192,1000,1,0,0:0:0:0:\n", "HS").Session : Doc().Session;
        session.SetHsMode(true);
        if (!protectedObject) session.Add([session.HitAt(Normal, 1000)]);
        session.Add([new EditHit(Clap, 1000, null)]);
        Assert.Single(session.Moments);
        Assert.Single(session.State.Hits.Select(h => h.Anchor).Distinct());
        Assert.Equal(protectedObject ? 0 : 1, session.State.HsCircles.Count);
    }

    [Fact]
    public void Backfill_is_one_undo_step_and_multiple_layers_share_one_circle()
    {
        var session = Doc().Session;
        session.Add([session.HitAt(Normal, 1000), session.HitAt(Clap, 1000), session.HitAt(Clap, 2000)]);
        int before = session.Position;
        session.SetHsMode(true);
        Assert.Equal(before + 1, session.Position);
        Assert.Empty(session.Unanchored);
        Assert.Equal(2, session.State.HsCircles.Count);
        Assert.Equal(2, session.Moments.Count);
        session.Undo();
        Assert.False(session.State.HsMode);
        Assert.Empty(session.State.HsCircles);
        Assert.Equal(3, session.Unanchored.Count());
        session.Redo();
        Assert.True(session.State.HsMode);
        Assert.Equal(2, session.Moments.Count);
    }

    [Fact]
    public void Placements_deletions_and_history_share_the_circle_lifecycle()
    {
        var session = Doc().Session;
        session.SetHsMode(true);
        var a = session.HitAt(Normal, 1000);
        session.Add([a]);
        var b = session.HitAt(Clap, 1000);
        session.Add([b]);
        Assert.Single(session.Moments);
        session.Remove([a]);
        Assert.Single(session.Moments);
        session.Remove([b]);
        Assert.Empty(session.Moments);
        session.Undo(); Assert.Single(session.Moments);
        session.Redo(); Assert.Empty(session.Moments);
        session.GoTo(1); Assert.True(session.State.HsMode); Assert.Empty(session.Moments);
        session.GoTo(3); Assert.Single(session.Moments); Assert.Equal(2, session.State.Hits.Count);
    }

    [Fact]
    public void Disabling_keeps_empty_circles_and_enabling_resumes_maintenance()
    {
        var session = Doc().Session;
        session.SetHsMode(true);
        var hit = session.HitAt(Normal, 1000); session.Add([hit]);
        session.SetHsMode(false); session.Remove([hit]);
        Assert.Single(session.Moments);
        session.Add([session.HitAt(Clap, 2000)]);
        Assert.Single(session.Unanchored);
        session.SetHsMode(true);
        Assert.Single(session.Moments);
        Assert.Equal(2000, session.Moments[0].Moment.Time);
        Assert.Empty(session.Unanchored);
        session.Undo();
        Assert.False(session.State.HsMode);
        Assert.Equal(1000, Assert.Single(session.Moments).Moment.Time);
    }

    [Fact]
    public void Existing_objects_are_reused_and_never_owned_or_deleted()
    {
        var doc = Doc("100,150,1000,1,0,0:0:0:0:\n", "HS");
        var session = doc.Session;
        session.SetHsMode(true);
        session.Add([session.HitAt(Clap, 1000)]);
        Assert.Empty(session.State.HsCircles);
        session.Remove(session.State.Hits.ToList());
        Assert.Single(session.Moments);
        var compiled = doc.Compile(session.State).File;
        Assert.Equal(100, Assert.Single(compiled.HitObjects).X);
        Assert.Equal(150, compiled.HitObjects[0].Y);
    }

    [Fact]
    public void Moving_and_copying_keep_volume_and_valid_anchors()
    {
        var session = Doc().Session;
        session.SetHsMode(true);
        var hit = session.HitAt(Normal, 1000); session.Add([hit]);
        session.SetVolume([hit.Anchor!.Value], 35);
        var moved = Assert.Single(session.Move([hit], 500, h => h.Lane));
        Assert.Equal(35, session.VolumeOf(moved));
        Assert.Equal(1500, Assert.Single(session.Moments).Moment.Time);
        var copied = Assert.Single(session.Copy([moved], 500));
        Assert.Equal(35, session.VolumeOf(copied));
        Assert.Equal(2, session.Moments.Count);
        Assert.All(session.State.Hits, h => Assert.True(session.MomentByKey.ContainsKey(h.Anchor!.Value)));
    }

    [Fact]
    public void Adjacent_milliseconds_stay_distinct_and_gestures_undo_atomically()
    {
        var session = Doc().Session;
        session.SetHsMode(true);
        session.BeginGesture("Paint");
        foreach (int time in new[] { 1000, 1001, 1002 }) session.Add([session.HitAt(Clap, time)]);
        session.EndGesture();
        Assert.Equal(3, session.Moments.Count);
        session.Undo(); Assert.Empty(session.Moments);
        session.Redo(); Assert.Equal(3, session.Moments.Count);
        session.BeginGesture("Move");
        var hit = session.State.Hits[0];
        session.Move([hit], 500, h => h.Lane);
        session.ResetToGestureStart();
        session.Move([hit], 750, h => h.Lane);
        session.EndGesture();
        Assert.DoesNotContain(session.Moments, m => m.Moment.Time == 1500);
        Assert.Contains(session.Moments, m => m.Moment.Time == 1750);
    }

    [Fact]
    public void Legacy_offsets_are_written_in_file_time_and_centred()
    {
        var doc = Doc(format: 3);
        doc.Session.SetHsMode(true);
        doc.Session.Add([doc.Session.HitAt(Normal, 1024)]);
        var result = doc.Compile(doc.Session.State);
        Assert.Empty(result.Result.Conflicts);
        var circle = Assert.Single(result.File.HitObjects);
        Assert.Equal(1000, circle.Time);
        Assert.Equal(256, circle.X); Assert.Equal(192, circle.Y);
    }
}

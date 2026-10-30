using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class EditSessionTests
{
    private static (EditSession Session, OsuFile File, MapFolder Folder) Load(string hitObjects, string[]? files = null)
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(
            "osu file format v14\r\n\r\n[General]\r\nSampleSet: Soft\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
            $"[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n{hitObjects.Replace("\n", "\r\n")}\r\n"));
        var folder = new MapFolder(files ?? []);
        return (new EditSession(file, folder, Decompiler.Decompile(file, folder)), file, folder);
    }

    private static Lane Skin(string name) => new(LaneSource.Skin, name);

    [Fact]
    public void Starts_with_every_hit_of_the_map_on_its_moment()
    {
        var (s, _, _) = Load("256,192,1000,1,8,0:0:0:0:\n256,192,1500,1,0,0:0:0:0:");
        Assert.Equal(3, s.State.Hits.Count);
        Assert.All(s.State.Hits, h => Assert.NotNull(h.Anchor));
        Assert.False(s.IsEdited);
    }

    [Fact]
    public void Offers_all_twelve_skin_lanes_and_every_set_named_file()
    {
        var (s, _, _) = Load("256,192,1000,1,0,0:0:0:0:", ["drum-hitclap3.ogg", "boom.wav", "soft-hitwhistle1.wav"]);
        Assert.Equal(12, s.Lanes.Count(l => l.Source == LaneSource.Skin));
        Assert.Contains(new Lane(LaneSource.BeatmapFile, "drum-hitclap3.ogg"), s.Lanes);
        Assert.DoesNotContain(s.Lanes, l => l.Name is "boom.wav" or "soft-hitwhistle1.wav"); // not reachable by index
    }

    [Fact]
    public void A_hit_near_a_moment_plays_on_it_and_one_elsewhere_has_no_object()
    {
        var (s, _, _) = Load("256,192,1000,1,0,0:0:0:0:");
        Assert.NotNull(s.HitAt(Skin("soft-hitclap"), 1001.5).Anchor);
        Assert.Equal(1000, s.HitAt(Skin("soft-hitclap"), 1001.5).Time);
        Assert.Null(s.HitAt(Skin("soft-hitclap"), 1250).Anchor);
    }

    [Fact]
    public void Edits_become_compiler_wishes_and_undo_redo_step_through_them()
    {
        var (s, file, folder) = Load("256,192,1000,1,0,0:0:0:0:");
        s.Add([s.HitAt(Skin("soft-hitclap"), 1000)]);
        s.SetVolume([s.State.Hits[0].Anchor!.Value], 60);

        var result = Compiler.Apply(file, folder, s.Wanted());
        Assert.Empty(result.Conflicts);
        Assert.Contains("256,192,1000,1,8,0:0:0:60:", Encoding.UTF8.GetString(file.ToBytes()));

        s.Undo();
        Assert.Equal(100, s.State.Volumes.Values.Single());
        s.Undo();
        Assert.Single(s.State.Hits);
        Assert.False(s.CanUndo);
        s.Redo();
        Assert.Equal(2, s.State.Hits.Count);
    }

    [Fact]
    public void A_drag_is_one_undo_step_whatever_the_number_of_moves()
    {
        var (s, _, _) = Load("256,192,1000,1,8,0:0:0:0:\n256,192,1500,1,0,0:0:0:0:");
        var clap = s.State.Hits.Single(h => h.Lane.Name == "soft-hitclap");

        s.BeginGesture();
        foreach (double offset in new[] { 100.0, 300, 500 })
        {
            s.ResetToGestureStart();
            s.Move([clap], offset, h => h.Lane);
        }

        s.EndGesture();
        Assert.Contains(s.State.Hits, h => h.Lane.Name == "soft-hitclap" && h.Time == 1500 && h.Anchor is not null);
        s.Undo();
        Assert.Contains(s.State.Hits, h => h.Lane.Name == "soft-hitclap" && h.Time == 1000);
        Assert.False(s.CanUndo);
    }

    [Fact]
    public void Moving_off_every_object_leaves_a_hit_with_no_object_that_is_not_compiled()
    {
        var (s, _, _) = Load("256,192,1000,1,8,0:0:0:0:");
        var clap = s.State.Hits.Single(h => h.Lane.Name == "soft-hitclap");
        s.Move([clap], 125, h => h.Lane);

        Assert.Single(s.Unanchored);
        Assert.Equal(["soft-hitnormal"], s.Wanted().Values.Single().Lanes.Select(l => l.Name));
    }

    [Fact]
    public void Undo_and_redo_wait_until_a_drag_ends()
    {
        var (s, _, _) = Load("256,192,1000,1,8,0:0:0:0:\n256,192,1500,1,0,0:0:0:0:");
        s.SetVolume([s.State.Hits[0].Anchor!.Value], 50);
        var clap = s.State.Hits.Single(h => h.Lane.Name == "soft-hitclap");

        s.BeginGesture();
        s.Move([clap], 500, h => h.Lane);
        s.Undo(); // mid-drag: ignored
        s.ResetToGestureStart();
        s.Move([clap], 500, h => h.Lane);
        s.EndGesture();

        Assert.Single(s.State.Hits, h => h.Lane.Name == "soft-hitclap");
        s.Undo();
        Assert.Contains(s.State.Hits, h => h.Lane.Name == "soft-hitclap" && h.Time == 1000);
    }

    [Fact]
    public void Bookmarks_are_edits_with_undo()
    {
        var (s, _, _) = Load("256,192,1000,1,0,0:0:0:0:");
        s.AddBookmark(1500.4);
        s.AddBookmark(500);
        Assert.Equal([500, 1500], s.State.Bookmarks);

        s.RemoveBookmarkNear(1503, 5);
        Assert.Equal([500], s.State.Bookmarks);
        s.Undo();
        Assert.Equal([500, 1500], s.State.Bookmarks);
    }
}

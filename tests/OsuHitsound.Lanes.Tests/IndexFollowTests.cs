using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

/// <summary>Adding a sound from another sample index moves the whole object to that index, as osu!'s editor does.</summary>
public class IndexFollowTests
{
    // One circle at 1000 ms playing soft-hitnormal2 (custom index 2 on the red line).
    private static (EditSession Session, OsuFile File, MapFolder Folder) Load(params string[] files) =>
        LoadWith("0,500,4,2,2,100,1,0", files);

    private static (EditSession Session, OsuFile File, MapFolder Folder) LoadWith(string timingPoints, string[] files)
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(
            "osu file format v14\r\n\r\n[General]\r\nSampleSet: Soft\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
            $"[TimingPoints]\r\n{timingPoints}\r\n\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:0:0:\r\n"));
        var folder = new MapFolder(files);
        return (new EditSession(file, folder, Decompiler.Decompile(file, folder)), file, folder);
    }

    private static Lane File(string name) => new(LaneSource.BeatmapFile, name);

    private static Lane Skin(string name) => new(LaneSource.Skin, name);

    private static string[] LanesAt(EditSession s) => [.. s.State.Hits.Where(h => h.Time == 1000).Select(h => h.Lane.Name).Order()];

    [Fact]
    public void A_sound_from_another_index_moves_the_hitnormal_to_that_index()
    {
        var (s, _, _) = Load("soft-hitnormal2.wav", "soft-hitclap48.wav", "soft-hitnormal48.wav");
        Assert.Equal(["soft-hitnormal2.wav"], LanesAt(s));
        s.Add([s.HitAt(File("soft-hitclap48.wav"), 1000)]);
        Assert.Equal(["soft-hitclap48.wav", "soft-hitnormal48.wav"], LanesAt(s));
    }

    [Fact]
    public void Without_a_file_at_the_new_index_the_hitnormal_plays_from_the_skin()
    {
        var (s, _, _) = Load("soft-hitnormal2.wav", "soft-hitclap48.wav");
        s.Add([s.HitAt(File("soft-hitclap48.wav"), 1000)]);
        Assert.Equal(["soft-hitclap48.wav", "soft-hitnormal"], LanesAt(s));
    }

    [Fact]
    public void Saving_the_moved_object_writes_no_new_files_and_one_index()
    {
        var (s, file, folder) = Load("soft-hitnormal2.wav", "soft-hitclap48.wav");
        s.Add([s.HitAt(File("soft-hitclap48.wav"), 1000)]);
        var result = Compiler.Apply(file, folder, s.Wanted());
        Assert.Empty(result.Conflicts);
        var again = Decompiler.Decompile(OsuFile.Parse(file.ToBytes()), folder);
        Assert.Equal(["soft-hitclap48.wav", "soft-hitnormal"], again.Moments.Single().Hits.Select(h => h.Lane.Name).Order());
    }

    [Fact]
    public void A_skin_sound_the_index_cannot_play_moves_the_object_back_to_the_skin()
    {
        var (s, _, _) = Load("soft-hitnormal2.wav", "soft-hitclap2.wav");
        s.Add([s.HitAt(Skin("soft-hitclap"), 1000)]);
        Assert.Equal(["soft-hitclap", "soft-hitnormal"], LanesAt(s));
    }

    [Fact]
    public void A_skin_sound_the_index_plays_from_the_skin_changes_nothing_else()
    {
        var (s, _, _) = Load("soft-hitnormal2.wav");
        s.Add([s.HitAt(Skin("soft-hitclap"), 1000)]);
        Assert.Equal(["soft-hitclap", "soft-hitnormal2.wav"], LanesAt(s));
    }

    [Fact]
    public void Keeping_mixed_indexes_is_a_choice_and_leaves_the_hitnormal()
    {
        var (s, _, _) = Load("soft-hitnormal2.wav", "soft-hitclap48.wav");
        s.KeepMixedIndexes = true;
        s.Add([s.HitAt(File("soft-hitclap48.wav"), 1000)]);
        Assert.Equal(["soft-hitclap48.wav", "soft-hitnormal2.wav"], LanesAt(s));
    }

    [Fact]
    public void Undo_brings_back_the_old_index_in_one_step()
    {
        var (s, _, _) = Load("soft-hitnormal2.wav", "soft-hitclap48.wav", "soft-hitnormal48.wav");
        s.Add([s.HitAt(File("soft-hitclap48.wav"), 1000)]);
        s.Undo();
        Assert.Equal(["soft-hitnormal2.wav"], LanesAt(s));
        Assert.False(s.IsEdited);
    }

    [Fact]
    public void An_addition_from_another_set_moves_the_other_additions_to_that_set()
    {
        var (s, _, _) = Load("soft-hitnormal2.wav", "normal-hitclap2.wav");
        s.Add([s.HitAt(Skin("soft-hitwhistle"), 1000)]);
        s.Add([s.HitAt(File("normal-hitclap2.wav"), 1000)]);
        Assert.Equal(["normal-hitclap2.wav", "normal-hitwhistle", "soft-hitnormal2.wav"], LanesAt(s));
    }

    [Fact]
    public void A_green_line_just_after_the_object_is_the_one_changed()
    {
        // osu! reads timing points up to 5 ms after an object: the line at 1001 sets this circle's index to 7.
        var (s, file, folder) = LoadWith("0,500,4,2,2,100,1,0\r\n1001,-100,4,2,7,100,0,0", ["soft-hitnormal7.wav", "soft-hitnormal3.wav", "soft-hitclap3.wav"]);
        Assert.Equal(["soft-hitnormal7.wav"], LanesAt(s));
        s.Add([s.HitAt(File("soft-hitclap3.wav"), 1000)]);
        var result = Compiler.Apply(file, folder, s.Wanted(), ExportStyle.GreenLines);
        Assert.Empty(result.Conflicts);
        var again = Decompiler.Decompile(OsuFile.Parse(file.ToBytes()), folder);
        Assert.Equal(["soft-hitclap3.wav", "soft-hitnormal3.wav"], again.Moments.Single().Hits.Select(h => h.Lane.Name).Order());
    }
}

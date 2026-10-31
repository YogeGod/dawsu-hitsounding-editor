using System.Text;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class MapDocumentTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hse-doc-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private const string Text =
        "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\nSampleSet: Soft\r\n\r\n[Metadata]\r\nTitle:Song\r\nArtist:Band\r\nCreator:Me\r\nVersion:Insane\r\n\r\n" +
        "[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:0:0:\r\n";

    private string Map()
    {
        var mapset = Path.Combine(dir, "mapset");
        Directory.CreateDirectory(mapset);
        var path = Path.Combine(mapset, "Band - Song (Me) [Insane].osu");
        File.WriteAllText(path, Text);
        return path;
    }

    private static Lane Clap => new(LaneSource.Skin, "soft-hitclap");

    [Fact]
    public void An_edit_is_saved_and_the_reopened_map_starts_from_it()
    {
        var path = Map();
        var doc = MapDocument.Load(path);
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)]);

        var saved = doc.Save(Path.Combine(dir, "backups"));

        Assert.True(saved.Written);
        Assert.Equal(Text, File.ReadAllText(saved.Backup!));
        var reopened = MapDocument.Load(path);
        Assert.Contains(reopened.Session.State.Hits, h => h.Lane == Clap && h.Time == 1000);
        Assert.False(reopened.Session.IsEdited);
    }

    [Fact]
    public void Save_refuses_a_map_changed_on_disk_since_it_was_opened()
    {
        var path = Map();
        var doc = MapDocument.Load(path);
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)]);
        File.AppendAllText(path, "\r\n");

        Assert.Throws<IOException>(() => doc.Save(Path.Combine(dir, "backups")));
        Assert.EndsWith("\r\n\r\n", File.ReadAllText(path)); // left as the other program wrote it
    }

    [Fact]
    public void Compiling_works_on_a_copy_and_never_changes_the_map_as_opened()
    {
        var doc = MapDocument.Load(Map());
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)]);

        var (result, copy) = doc.Compile(doc.Session.State);

        Assert.Empty(result.Conflicts);
        Assert.Contains("256,192,1000,1,8,", Encoding.UTF8.GetString(copy.ToBytes()));
        Assert.Equal(Text, Encoding.UTF8.GetString(doc.Opened));
        Assert.False(doc.File.IsEdited);
    }

    [Fact]
    public void Generate_is_planned_next_to_the_map_with_the_previous_record()
    {
        var doc = MapDocument.Load(Map());
        doc.Session.Add([doc.Session.HitAt(new Lane(LaneSource.Skin, "drum-hitclap"), 1250)]); // no hitnormal: needs a mix

        var (plan, folder) = doc.PlanGenerate(Path.Combine(dir, "generated"));

        Assert.Equal(Path.Combine(Path.GetDirectoryName(doc.Path)!, "Band - Song (Me) [Hitsounds].osu"), plan.OsuPath);
        Assert.Equal("soft-hitnormal2.wav", Assert.Single(plan.Mixes).FileName);
        Assert.Contains(Path.GetFileName(doc.Path), folder.Files);
    }
}

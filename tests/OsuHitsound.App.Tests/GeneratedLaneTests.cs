using System.Text;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

/// <summary>
/// Mix files dawsu! wrote itself (Save's or Generate's, e.g. normal-hitnormal3.wav) never get notes: the hits stay on
/// their own lanes. So their lanes are hidden unless something plays them.
/// </summary>
public class GeneratedLaneTests
{
    private const string MapPath = @"C:\maps\x\Band - Song (Me) [Insane].osu";

    private static readonly byte[] MapBytes = Encoding.UTF8.GetBytes(
        "osu file format v14\r\n\r\n[General]\r\nSampleSet: Normal\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
        "[TimingPoints]\r\n0,500,4,1,0,100,1,0\r\n1500,-100,4,1,7,100,0,0\r\n\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:0:0:\r\n256,192,2000,1,0,0:0:0:0:\r\n");

    private static readonly Lane Mix = new(LaneSource.BeatmapFile, "normal-hitnormal3.wav");
    private static readonly Lane Own = new(LaneSource.BeatmapFile, "normal-hitnormal5.wav");
    private static readonly Lane Played = new(LaneSource.BeatmapFile, "normal-hitnormal7.wav");

    private static ArrangeModel Open()
    {
        var doc = MapDocument.FromBytes(MapPath, MapBytes, new MapFolder([Mix.Name, Own.Name, Played.Name], _ => [1]));
        return new ArrangeModel(doc.Session, doc.File, new HashSet<Lane>());
    }

    private static List<Lane> Shown(ArrangeModel model) => [.. model.Rows.OfType<LaneRow>().Select(r => r.Lane)];

    [Fact]
    public void A_generated_mix_with_no_hits_gets_no_lane_but_the_mappers_own_samples_do()
    {
        var model = Open();
        Assert.Contains(Mix, Shown(model)); // before: every numbered sample has a lane

        model.HideGenerated([Mix.Name, Played.Name]);

        Assert.DoesNotContain(Mix, Shown(model));
        Assert.Contains(Own, Shown(model));    // an unused sample the mapper added still shows
        Assert.Contains(Played, Shown(model)); // a generated file that a hit plays still shows (the 2000 ms circle, index 7)
    }

    [Fact]
    public void Only_mixes_dawsu_wrote_and_that_are_unchanged_count_as_generated()
    {
        var root = Directory.CreateTempSubdirectory("hse-generated-").FullName;
        try
        {
            byte[] written = [1, 2, 3];
            var files = new Dictionary<string, byte[]> { [Mix.Name] = written, [Own.Name] = [9, 9], [Played.Name] = [4] };
            var doc = MapDocument.FromBytes(MapPath, MapBytes, new MapFolder(files.Keys, name => files[name]));
            new GenerateManifest([
                new GeneratedMix(Mix.Name, "sig-a", GenerateManifest.Hash(written)),
                new GeneratedMix(Own.Name, "sig-b", GenerateManifest.Hash([1])), // replaced by the mapper since
            ]).Save(root, MapPath);

            Assert.Equal([Mix.Name], MainWindow.GeneratedMixFiles(doc, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Adding_a_hidden_generated_lane_by_hand_shows_it()
    {
        var model = Open();
        model.HideGenerated([Mix.Name]);

        model.AddLane(Mix);

        Assert.Contains(Mix, Shown(model));
    }
}

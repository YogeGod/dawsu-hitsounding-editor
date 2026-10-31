using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

/// <summary>The inspector's Selection and Recent edits cards (docs/spec-wave2.md, parts 6 and 7).</summary>
public class InspectorTests
{
    private static (EditSession Session, ArrangeModel Model) Load()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(
            "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\nSampleSet: Soft\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
            "[TimingPoints]\r\n0,500,4,2,0,80,1,0\r\n\r\n[HitObjects]\r\n256,192,1000,1,8,0:0:0:0:\r\n256,192,1500,1,0,0:0:0:0:\r\n"));
        var session = new EditSession(file, new MapFolder([]), Decompiler.Decompile(file, new MapFolder([])));
        return (session, new ArrangeModel(session, file, new HashSet<Lane>()));
    }

    private static string Ms(double t) => $"{t} ms";

    [Fact]
    public void Selection_says_nothing_one_hit_or_a_summary_of_several()
    {
        var (s, m) = Load();
        Assert.Equal("Nothing selected", InspectorText.Selection(s, m, [], Ms));

        var clap = s.State.Hits.Single(h => h.Lane.Name == "soft-hitclap");
        Assert.Equal("1000 ms  (1.3.1)\nsoft-hitclap\nVolume: 80 %", InspectorText.Selection(s, m, [clap], Ms));

        Assert.Equal("3 hits\n1000 ms → 1500 ms\nVolume: 80 %", InspectorText.Selection(s, m, s.State.Hits, Ms));
    }

    [Fact]
    public void A_hit_with_no_object_has_no_volume_yet()
    {
        var (s, m) = Load();
        var loose = s.HitAt(new Lane(LaneSource.Skin, "soft-hitwhistle"), 1250);
        Assert.EndsWith("Volume: no object yet", InspectorText.Selection(s, m, [loose], Ms));
    }

    [Fact]
    public void Recent_edits_lists_newest_first_marks_the_current_one_and_undone_ones()
    {
        var (s, _) = Load();
        s.Remove([s.State.Hits[0]]);
        s.AddBookmark(1000);
        s.Undo();

        var list = InspectorText.RecentEdits(s);
        Assert.Equal([(2, "Add bookmark (undone)", false), (1, "Delete", true), (0, "As opened", false)], list);
    }

    [Fact]
    public void Recent_edits_keeps_the_last_fifty()
    {
        var (s, _) = Load();
        for (int i = 0; i < 60; i++) s.AddBookmark(1000 + i * 10);
        var list = InspectorText.RecentEdits(s);
        Assert.Equal(50, list.Count);
        Assert.Equal(60, list[0].Position);
        Assert.Equal(11, list[^1].Position);
    }
}

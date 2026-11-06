using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class SnapAndLaneSuggestionTests
{
    // 120 BPM: 500 ms beats from 0.
    private static ArrangeModel Model()
    {
        var text = "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\nSampleSet: Soft\r\n\r\n" +
                   "[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
                   "[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:0:0:\r\n";
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(text));
        var map = Decompiler.Decompile(file, new MapFolder([]));
        return new ArrangeModel(new EditSession(file, new MapFolder([]), map), file, new HashSet<Lane>());
    }

    [Fact]
    public void Snap_lines_know_their_beat_fraction_in_lowest_terms()
    {
        Assert.Equal([(125.0, 4), (250.0, 2), (375.0, 4)], Model().SubLines(1, 499, 4));
        Assert.Equal([6, 3, 2, 3, 6], Model().SubLines(1, 499, 6).Select(l => l.Fraction));
    }

    [Fact]
    public void Snap_lines_are_one_grey_for_every_division_as_in_Cubase()
    {
        var pens = new[] { 2, 3, 4, 6, 8, 16 }.Select(Paint.SnapPen).ToList();
        Assert.All(pens, pen => Assert.Same(pens[0], pen));
        var colour = ((Avalonia.Media.ISolidColorBrush)pens[0].Brush!).Color;
        Assert.True(colour.R == colour.G && colour.G == colour.B, "grey");
    }

    private static readonly string[] Files = ["drum-hitclap2.ogg", "drum-hitclap4.wav", "drum-hitwhistle.wav", "kick.wav", "soft-hitfinish3.wav"];

    [Theory]
    [InlineData("drum-hitclap2.ogg", "drum-hitclap4.wav")] // the next index of the same sound
    [InlineData("drum-hitclap7.wav", "drum-hitclap2.ogg")] // none higher: that sound at any index
    [InlineData("drum-hitfinish.wav", "drum-hitclap2.ogg")] // no such sound: the same set
    [InlineData("normal-hitclap.wav", "drum-hitclap2.ogg")] // nothing alike: the first file
    [InlineData("kick2.wav", "drum-hitclap2.ogg")]
    public void Add_lane_starts_after_the_previous_lane(string previous, string expected) =>
        Assert.Equal(expected, ArrangeModel.SuggestNewLane(Files, new Lane(LaneSource.BeatmapFile, previous)));

    [Fact]
    public void A_skin_lane_suggests_the_files_of_its_sound()
    {
        Assert.Equal("soft-hitfinish3.wav", ArrangeModel.SuggestNewLane(Files, new Lane(LaneSource.Skin, "soft-hitfinish")));
        Assert.Null(ArrangeModel.SuggestNewLane([], null));
    }
}

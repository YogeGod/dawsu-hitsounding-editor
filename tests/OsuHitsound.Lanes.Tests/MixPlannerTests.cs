using System.Text;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class MixPlannerTests
{
    private static readonly List<(Lane Lane, int Volume)> Clap = MixPlanner.Normalize([(new Lane(LaneSource.Skin, "drum-hitclap"), 3)]);

    [Fact]
    public void Identical_layers_share_a_file_and_volumes_below_8_percent_play_at_8()
    {
        var planner = new MixPlanner(new MapFolder(["soft-hitclap2.wav"]), null, "a.osu", [Clap]);
        var a = planner.MixFor(Clap);
        var b = planner.MixFor(MixPlanner.Normalize([(new Lane(LaneSource.Skin, "drum-hitclap"), 5)]));

        Assert.Same(a, b);
        Assert.Equal("soft-hitnormal3.wav", a.FileName);
        Assert.Equal(8, a.Layers.Single().Volume);
        Assert.Single(planner.ToWrite);
    }

    [Fact]
    public void The_file_being_replaced_does_not_hold_up_indexes_but_other_difficulties_do()
    {
        byte[] Uses(int index) => Encoding.UTF8.GetBytes($"osu file format v14\r\n\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:{index}:0:\r\n");
        var files = new Dictionary<string, byte[]> { ["a.osu"] = Uses(9), ["b.osu"] = Uses(4) };
        var folder = new MapFolder(files.Keys, name => files[name]);

        Assert.Equal("soft-hitnormal5.wav", new MixPlanner(folder, null, "a.osu", [Clap]).MixFor(Clap).FileName);
        Assert.Equal("soft-hitnormal10.wav", new MixPlanner(folder, null, "other.osu", [Clap]).MixFor(Clap).FileName);
    }
}

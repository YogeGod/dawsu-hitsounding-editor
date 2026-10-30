using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Format.Tests;

public class HitsoundRulesTests
{
    private static List<TimingPoint> Points(params string[] lines) =>
        OsuFile.Parse(Encoding.UTF8.GetBytes("[TimingPoints]\r\n" + string.Join("\r\n", lines) + "\r\n")).TimingPoints.ToList();

    [Theory]
    [InlineData(1004, 2)] // 1 ms before a green line at 1005: inside the 5 ms lookahead
    [InlineData(1000, 2)] // exactly 5 ms before it
    [InlineData(999, 1)]  // 6 ms before it: too early
    public void Objects_read_the_timing_point_5ms_ahead(double objectTime, int expectedIndex)
    {
        var points = Points("0,500,4,2,1,100,1,0", "1005,-100,4,2,2,100,0,0");
        Assert.Equal(expectedIndex, HitsoundRules.SamplePointAt(points, objectTime)!.SampleIndex);
    }

    [Fact]
    public void Among_points_at_the_same_time_the_later_line_wins()
    {
        var points = Points("1000,500,4,2,1,100,1,0", "1000,-100,4,3,7,60,0,0");
        Assert.Equal(7, HitsoundRules.SamplePointAt(points, 1000)!.SampleIndex);
    }

    [Fact]
    public void Before_the_first_timing_point_the_first_one_applies()
    {
        Assert.Equal(1000, HitsoundRules.SamplePointAt(Points("1000,500,4,2,1,100,1,0", "2000,-100,4,2,2,100,0,0"), 500)!.Time);
        Assert.Null(HitsoundRules.SamplePointAt(Points(), 500));
    }

    [Theory]
    [InlineData(SampleSet.Soft, "hitclap", 0, null)]
    [InlineData(SampleSet.Soft, "hitclap", 1, "soft-hitclap")]
    [InlineData(SampleSet.Drum, "hitnormal", 3, "drum-hitnormal3")]
    [InlineData(SampleSet.Normal, "slidertick", 12, "normal-slidertick12")]
    public void Beatmap_sample_names_follow_the_index_rules(SampleSet set, string sound, int index, string? expected)
    {
        Assert.Equal(expected, HitsoundRules.BeatmapSampleName(set, sound, index));
    }

    [Fact]
    public void Sample_names_need_a_resolved_sample_set()
    {
        Assert.Throws<ArgumentException>(() => HitsoundRules.BeatmapSampleName(SampleSet.None, "hitnormal", 2));
    }
}

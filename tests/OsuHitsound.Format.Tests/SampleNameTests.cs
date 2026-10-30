using OsuHitsound.Format;

namespace OsuHitsound.Format.Tests;

public class SampleNameTests
{
    [Theory]
    [InlineData("soft-hitclap", SampleSet.Soft, "hitclap", 1)]
    [InlineData("drum-hitclap3", SampleSet.Drum, "hitclap", 3)]
    [InlineData("Normal-HitFinish12", SampleSet.Normal, "hitfinish", 12)]
    [InlineData("soft-sliderslide2", SampleSet.Soft, "sliderslide", 2)]
    public void Names_osu_asks_for_by_index_are_read_back(string name, SampleSet set, string sound, int index)
    {
        Assert.Equal((set, sound, index), HitsoundRules.ParseSampleName(name));
        Assert.Equal(name, HitsoundRules.BeatmapSampleName(set, sound, index), ignoreCase: true);
    }

    [Theory]
    [InlineData("soft-hitclap1")] // index 1 has no number
    [InlineData("soft-hitclap0")]
    [InlineData("soft-hitclap03")]
    [InlineData("none-hitclap")]
    [InlineData("1-hitclap")]
    [InlineData("kick")]
    [InlineData("soft-")]
    [InlineData("soft-hit-clap2")]
    public void Other_names_are_only_playable_by_file_name(string name) => Assert.Null(HitsoundRules.ParseSampleName(name));
}

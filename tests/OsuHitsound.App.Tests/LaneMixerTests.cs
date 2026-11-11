using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class LaneMixerTests
{
    private static readonly Lane Clap = new(LaneSource.BeatmapFile, "soft-hitclap2.wav");
    private static readonly Lane Finish = new(LaneSource.BeatmapFile, "soft-hitfinish2.wav");

    [Fact]
    public void Each_lane_is_read_and_decoded_once_across_mixes()
    {
        int reads = 0;
        var folder = new MapFolder([Clap.Name], _ => { reads++; return BlankSample.Bytes; });
        var mixer = new LaneMixer(new LaneAudio(folder));

        mixer.Mix(new MixFile("soft-hitnormal5.wav", [(Clap, 100)]));
        mixer.Mix(new MixFile("soft-hitnormal6.wav", [(Clap, 50)]));

        Assert.Equal(1, reads);
        Assert.Same(mixer.Decoded(Clap), mixer.Decoded(Clap));
    }

    [Fact]
    public void A_missing_lane_decodes_to_nothing_and_its_mix_fails()
    {
        var mixer = new LaneMixer(new LaneAudio(new MapFolder([])));

        Assert.Null(mixer.Decoded(Finish));
        Assert.Throws<IOException>(() => mixer.Mix(new MixFile("soft-hitnormal5.wav", [(Finish, 100)])));
    }
}

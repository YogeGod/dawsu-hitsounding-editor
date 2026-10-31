using System.Text;
using OsuHitsound.App;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class MixerAndLaneTests
{
    [Fact]
    public void Mixing_sums_layers_at_their_gains_into_a_16_bit_wav()
    {
        var a = new float[SampleMixer.Rate * 2]; // 1 s stereo
        var b = new float[100];
        a[0] = 0.5f;
        b[0] = 0.5f;
        a[2] = 0.9f;
        b[2] = 0.9f; // clips

        var wav = SampleMixer.Mix([(a, 1f), (b, 0.5f)]);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(44 + a.Length * 2, wav.Length);
        Assert.Equal((short)Math.Round(0.75 * short.MaxValue), BitConverter.ToInt16(wav, 44));
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(wav, 48));
    }

    [Fact]
    public void A_mix_is_never_shorter_than_25_ms()
    {
        var wav = SampleMixer.Mix([(new float[10], 1f)]);
        Assert.True((wav.Length - 44) / 4 >= SampleMixer.Rate / 40);
    }

    [Fact]
    public void Mono_samples_become_stereo_at_44_1_khz()
    {
        var mono = new float[] { 0, 1, 0, -1 };
        var stereo = SampleMixer.ToStereo(mono, channels: 1, rate: 22050);
        Assert.Equal(16, stereo.Length); // 4 frames at half the rate → 8 frames × 2 channels
        Assert.Equal(stereo[2], stereo[3]); // both channels the same
        Assert.Equal(1f, stereo[4]);
    }

    private static ArrangeModel Model()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(
            "osu file format v14\r\n\r\n[General]\r\nSampleSet: Soft\r\n\r\n[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n256,192,1000,1,8,0:0:0:0:\r\n"));
        var folder = new MapFolder(["soft-hitclap2.wav", "boom.wav"]);
        return new ArrangeModel(new EditSession(file, folder, Decompiler.Decompile(file, folder)), file, new HashSet<Lane>());
    }

    [Fact]
    public void Lanes_can_be_renamed_moved_added_hidden_and_filtered()
    {
        var model = Model();
        var skin = model.LaneRows.Where(r => r.Group == LaneGroup.Skin).Select(r => r.Lane).ToList();

        model.RenameLane(skin[0], "Big clap");
        Assert.Equal("Big clap", model.NameOf(skin[0]));

        model.MoveLane(skin[1], -1);
        Assert.Equal(skin[1], model.LaneRows.First(r => r.Group == LaneGroup.Skin).Lane);

        var boom = new Lane(LaneSource.BeatmapFile, "boom.wav");
        model.AddLane(boom);
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == boom);

        model.HideLane(boom);
        Assert.DoesNotContain(model.Rows.OfType<LaneRow>(), r => r.Lane == boom);

        model.SetFilter("clap2");
        Assert.Equal(["soft-hitclap2.wav"], model.Rows.OfType<LaneRow>().Select(r => r.Lane.Name));
        Assert.Equal(Enumerable.Range(1, model.LaneRows.Count), model.LaneRows.Select(r => r.Number));
    }

    [Fact]
    public void The_lane_layout_round_trips_through_a_project()
    {
        var model = Model();
        var lane = model.LaneRows[1].Lane;
        model.RenameLane(lane, "renamed");
        model.MoveLane(lane, -1);
        var (names, order, added, hidden) = model.LaneLayout();

        var fresh = Model();
        fresh.ApplyLaneLayout(new ProjectData { LaneNames = names, LaneOrder = order, AddedLanes = added, HiddenLanes = hidden }, _ => true);

        Assert.Equal("renamed", fresh.NameOf(lane));
        Assert.Equal(model.LaneRows.Select(r => r.Lane), fresh.LaneRows.Select(r => r.Lane));
    }
}

using System.Text;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class MixPlannerTests
{
    private static readonly List<(Lane Lane, int Volume)> Clap = MixPlanner.Normalize([(new Lane(LaneSource.Skin, "drum-hitclap"), 3)]);

    [Fact]
    public void Identical_layers_share_a_file_and_volumes_below_8_percent_play_at_8()
    {
        var planner = new MixPlanner(new LaneAudio(new MapFolder(["soft-hitclap2.wav"])), null, "a.osu", [Clap]);
        var a = planner.MixFor(Clap);
        var b = planner.MixFor(MixPlanner.Normalize([(new Lane(LaneSource.Skin, "drum-hitclap"), 5)]));

        Assert.Same(a, b);
        Assert.Equal("soft-hitnormal3.wav", a.FileName);
        Assert.Equal(8, a.Layers.Single().Volume);
        Assert.Single(planner.ToWrite);
    }

    [Fact]
    public void Other_difficulties_reserve_their_exact_indexes_without_pushing_the_start_above_them()
    {
        byte[] Uses(int index) => Encoding.UTF8.GetBytes($"osu file format v14\r\n\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:{index}:0:\r\n");
        var files = new Dictionary<string, byte[]> { ["a.osu"] = Uses(9), ["b.osu"] = Uses(4) };
        var folder = new MapFolder(files.Keys, name => files[name]);

        var planner = new MixPlanner(new LaneAudio(folder), null, "other.osu", [Clap]);
        var names = Enumerable.Range(8, 8).Select(volume => planner.MixFor([(Clap[0].Lane, volume)]).FileName).ToList();
        Assert.Equal([2, 3, 5, 6, 7, 8, 10, 11], names.Select(HitsoundDiff.IndexOf).Select(i => i!.Value));
        Assert.Equal("soft-hitnormal2.wav", new MixPlanner(new LaneAudio(folder), null, "a.osu", [Clap]).MixFor(Clap).FileName);
    }

    [Fact]
    public void New_batches_continue_after_previous_generated_mix_and_skip_local_collisions()
    {
        var other = Encoding.UTF8.GetBytes("osu file format v14\r\n[TimingPoints]\r\n0,500,4,2,27,100,1,0\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:999:0:\r\n");
        var folder = new MapFolder(["soft-hitnormal25.wav", "drum-hitclap26.ogg", "other.osu"], name => name == "other.osu" ? other : [1]);
        var previous = new GenerateManifest([new GeneratedMix("soft-hitnormal25.wav", "old", GenerateManifest.Hash([1]))]);
        var planner = new MixPlanner(new LaneAudio(folder), previous, "generated.osu", [Clap]);
        Assert.Equal("soft-hitnormal28.wav", planner.MixFor(Clap).FileName);
        Assert.Equal(28, planner.LastIndex);
    }

    [Fact]
    public void A_first_batch_does_not_jump_above_an_unrelated_999_index()
    {
        var planner = new MixPlanner(new LaneAudio(new MapFolder(["drum-hitclap999.wav"])), null, "a.osu", [Clap]);
        Assert.Equal("soft-hitnormal2.wav", planner.MixFor(Clap).FileName);
    }

    [Fact]
    public void Last_generated_index_is_remembered_when_the_old_mix_has_been_removed()
    {
        var previous = new GenerateManifest([]) { LastIndex = 25 };
        var planner = new MixPlanner(new LaneAudio(new MapFolder([])), previous, "a.osu", [Clap]);
        Assert.Equal("soft-hitnormal26.wav", planner.MixFor(Clap).FileName);
    }

    [Fact]
    public void Direct_storyboard_and_hit_sample_filenames_reserve_their_indexes()
    {
        var story = Encoding.UTF8.GetBytes("[Variables]\r\n$s=soft-hitnormal4.wav\r\n[Events]\r\nSample,1000,0,\"$s\",100\r\n");
        var other = Encoding.UTF8.GetBytes("osu file format v14\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:0:0:soft-hitnormal2.wav\r\n");
        var files = new Dictionary<string, byte[]> { ["shared.osb"] = story, ["other.osu"] = other, ["soft-hitnormal4.wav"] = [1] };
        var previous = new GenerateManifest([new GeneratedMix("soft-hitnormal4.wav", "old", GenerateManifest.Hash([1]))]);
        var folder = new MapFolder(files.Keys, name => files[name]);
        var planner = new MixPlanner(new LaneAudio(folder), previous, "generated.osu", [Clap]);
        Assert.DoesNotContain("soft-hitnormal4.wav", planner.Removes);
        var fresh = new MixPlanner(new LaneAudio(folder), null, "generated.osu", [Clap]);
        Assert.Equal("soft-hitnormal3.wav", fresh.MixFor(Clap).FileName);
    }

    [Fact]
    public void A_shared_generated_diff_recovers_its_previous_index_without_local_history()
    {
        var diff = Encoding.UTF8.GetBytes("osu file format v14\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:25:100:\r\n");
        var folder = new MapFolder(["generated.osu", "soft-hitnormal25.wav", "drum-hitclap999.wav"], _ => diff);
        Assert.Equal("soft-hitnormal26.wav", new MixPlanner(new LaneAudio(folder), null, "generated.osu", [Clap]).MixFor(Clap).FileName);
    }

    [Fact]
    public void A_shared_diff_marker_preserves_the_last_generated_index_even_with_an_unrelated_high_index()
    {
        var diff = Encoding.UTF8.GetBytes("osu file format v14\r\n[Events]\r\n//dawsu-generated-index:25\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:999:100:\r\n");
        var folder = new MapFolder(["generated.osu", "soft-hitnormal999.wav"], _ => diff);
        Assert.Equal("soft-hitnormal26.wav", new MixPlanner(new LaneAudio(folder), null, "generated.osu", [Clap]).MixFor(Clap).FileName);
        Assert.Equal("soft-hitnormal26.wav", new MixPlanner(new LaneAudio(folder), new GenerateManifest([]) { LastIndex = 10 }, "generated.osu", [Clap]).MixFor(Clap).FileName);
    }

    [Fact]
    public void Reuse_checks_source_contents_including_skin_samples()
    {
        var lane = Clap[0].Lane;
        var oldBytes = new byte[] { 10 };
        var sourceHash = GenerateManifest.Hash(Encoding.UTF8.GetBytes("mix-v2;" + GenerateManifest.Hash(oldBytes)));
        var previous = new GenerateManifest([new GeneratedMix("soft-hitnormal25.wav", MixFile.SignatureOf(Clap), GenerateManifest.Hash([1])) { InputHash = sourceHash }]);
        var folder = new MapFolder(["soft-hitnormal25.wav"], _ => [1]);
        var unchanged = new MixPlanner(new LaneAudio(folder, new Dictionary<string, byte[]> { [lane.Name] = oldBytes }), previous, "generated.osu", [Clap]);
        Assert.Equal("soft-hitnormal25.wav", unchanged.MixFor(Clap).FileName);
        var changed = new MixPlanner(new LaneAudio(folder, new Dictionary<string, byte[]> { [lane.Name] = [11] }), previous, "generated.osu", [Clap]);
        Assert.Equal("soft-hitnormal26.wav", changed.MixFor(Clap).FileName);
        Assert.Empty(changed.Reused);
    }

    [Fact]
    public void Reuse_checks_mapset_sample_contents_and_legacy_manifests_are_not_trusted()
    {
        var lane = new Lane(LaneSource.BeatmapFile, "kick.wav");
        var layers = MixPlanner.Normalize([(lane, 100)]);
        var bytes = new Dictionary<string, byte[]> { ["kick.wav"] = [7], ["soft-hitnormal25.wav"] = [1] };
        var sourceHash = GenerateManifest.Hash(Encoding.UTF8.GetBytes("mix-v2;" + GenerateManifest.Hash(bytes["kick.wav"])));
        var old = new GeneratedMix("soft-hitnormal25.wav", MixFile.SignatureOf(layers), GenerateManifest.Hash([1]));
        var folder = new MapFolder(bytes.Keys, name => bytes[name]);
        var previous = new GenerateManifest([old with { InputHash = sourceHash }]);
        Assert.Equal("soft-hitnormal25.wav", new MixPlanner(new LaneAudio(folder), previous, "generated.osu", [layers]).MixFor(layers).FileName);
        bytes["kick.wav"] = [8];
        Assert.Equal("soft-hitnormal26.wav", new MixPlanner(new LaneAudio(folder), previous, "generated.osu", [layers]).MixFor(layers).FileName);
        Assert.Equal("soft-hitnormal26.wav", new MixPlanner(new LaneAudio(folder), new GenerateManifest([old]), "generated.osu", [layers]).MixFor(layers).FileName);
    }
}

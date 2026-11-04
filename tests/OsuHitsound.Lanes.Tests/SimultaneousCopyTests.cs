using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Tests;

public class SimultaneousCopyTests
{
    private static OsuFile Map(string objects) => OsuFile.Parse(Encoding.UTF8.GetBytes(
        "osu file format v14\n[General]\nSampleSet: Soft\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n" + objects));
    private static OsuFile Source => Map("256,192,1000,1,8,0:0:0:30:\n256,192,1000,1,2,0:0:0:70:\n");

    [Fact]
    public void Keeping_target_indexes_reports_an_unrepresentable_simultaneous_copy_instead_of_dropping_layers()
    {
        var folder = new MapFolder([]);
        var target = Map("256,192,1000,1,0,0:0:0:100:\n");
        byte[] before = target.ToBytes();
        var error = Assert.Throws<InvalidOperationException>(() => HitsoundCopier.Plan(Source, folder, target, folder,
            new() { CopySampleSets = false }));
        Assert.Contains("Copy sample sets", error.Message);
        Assert.Equal(before, target.ToBytes());
    }

    [Theory]
    [InlineData(CopyMode.OverwriteEverything, true, 60, 100)]
    [InlineData(CopyMode.OverwriteOnlyDefined, true, 60, 100)]
    [InlineData(CopyMode.OverwriteEverything, false, 60, 60)]
    [InlineData(CopyMode.OverwriteOnlyDefined, true, 5, 5)]
    public void Simultaneous_hits_keep_every_layer_and_the_requested_volume_policy(CopyMode mode, bool copyVolumes, int targetVolume, int expectedVolume)
    {
        var target = Map($"256,192,1000,1,0,0:0:0:{targetVolume}:\n");
        var folder = new MapFolder([]);
        var plan = HitsoundCopier.Plan(Source, folder, target, folder, new HitsoundCopyOptions { Mode = mode, CopyVolumes = copyVolumes });
        var mix = Assert.Single(plan.Mixes);
        Assert.Equal(4, mix.Layers.Count); // two normals and the two different additions
        Assert.Equal(copyVolumes ? new[] { 30, 30, 70, 70 } : new[] { 100, 100, 100, 100 }, mix.Layers.Select(l => l.Volume).Order());
        var withMix = folder.With([mix.FileName]);
        var result = Compiler.Apply(target, withMix, plan.Wanted);
        Assert.Empty(result.Conflicts);
        var after = Assert.Single(Decompiler.Decompile(target, withMix).Moments);
        Assert.Equal(expectedVolume, after.Volume);
        Assert.Equal(mix.FileName, Assert.Single(after.Hits).Lane.Name);
    }

    [Fact]
    public void Nearby_but_distinct_source_times_retain_the_existing_last_match_policy()
    {
        var source = Map("256,192,1000,1,8,0:0:0:30:\n256,192,1001,1,2,0:0:0:70:\n");
        var target = Map("256,192,1000,1,0,0:0:0:100:\n");
        var folder = new MapFolder([]);
        var plan = HitsoundCopier.Plan(source, folder, target, folder, new());
        Assert.Empty(plan.Mixes);
        Assert.Contains(Assert.Single(plan.Wanted).Value.Lanes, l => l.Name == "soft-hitwhistle");
        Assert.DoesNotContain(Assert.Single(plan.Wanted).Value.Lanes, l => l.Name == "soft-hitclap");
    }

    [Theory]
    [InlineData(true, 1500)]
    [InlineData(false, 1250)]
    public void Simultaneous_unmatched_hits_also_survive_copy_to_ticks_and_slides(bool ticks, int time)
    {
        var source = Map($"256,192,{time},1,8,0:0:0:30:\n256,192,{time},1,2,0:0:0:70:\n");
        var target = Map("256,192,1000,2,0,B|356:192,1,560\n");
        var folder = new MapFolder([]);
        var plan = HitsoundCopier.Plan(source, folder, target, folder, new()
        {
            Mode = CopyMode.OverwriteOnlyDefined, CopyToSliderTicks = ticks, CopyToSliderSlides = !ticks,
        }, new CustomSampleIndexes(folder, 100));
        var mix = Assert.Single(plan.Mixes);
        Assert.Equal(4, mix.Layers.Count);
        Assert.Equal(new[] { 30, 30, 70, 70 }, mix.Layers.Select(l => l.Volume).Order());
        Assert.Equal(100, Assert.Single(plan.GreenLines).Volume);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_renderer_or_failed_render_leaves_the_target_and_sample_files_untouched(bool provideRenderer)
    {
        string root = Directory.CreateTempSubdirectory("hse-copy-regression-").FullName;
        try
        {
            string path = Path.Combine(root, "target.osu");
            byte[] bytes = Map("256,192,1000,1,0,0:0:0:100:\n").ToBytes();
            File.WriteAllBytes(path, bytes);
            var result = Assert.Single(HitsoundCopier.CopyTo(Source, new MapFolder([]), [path], new(), Path.Combine(root, "backups"),
                provideRenderer ? _ => null : null));
            Assert.NotNull(result.Error);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }
}

using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

/// <summary>Copy hitsounds, checked the way Mapping Tools' own Hitsound Copier tests check it (v2.0.8).</summary>
public class HitsoundCopierTests
{
    private static readonly MapFolder NoFiles = new([]);

    private static OsuFile Map(string timing, params string[] objects) => OsuFile.Parse(Encoding.UTF8.GetBytes(
        "osu file format v14\r\n\r\n[General]\r\nSampleSet: Normal\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
        $"[TimingPoints]\r\n{timing.Replace("\n", "\r\n")}\r\n\r\n[HitObjects]\r\n{string.Join("\r\n", objects)}\r\n"));

    private static OsuFile Circles(params (int Time, int Sound)[] circles) =>
        Map("0,500,4,1,0,100,1,0", [.. circles.Select(c => $"256,192,{c.Time},1,{c.Sound},0:0:0:0:")]);

    // Plans the copy, writes it with the editor's compiler and reads the target back.
    private static (HitsoundCopyPlan Plan, DecompiledMap After) Copy(OsuFile source, OsuFile target, HitsoundCopyOptions options)
    {
        var plan = HitsoundCopier.Plan(source, NoFiles, target, NoFiles, options);
        var copy = OsuFile.Parse(target.ToBytes());
        Compiler.Apply(copy, NoFiles, plan.Wanted);
        return (plan, Decompiler.Decompile(copy, NoFiles));
    }

    private static string[] Sounds(Moment m) => [.. m.Hits.Select(h => h.Lane.Name).Order()];

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void A_hit_is_copied_only_within_the_leniency(double leniency, bool copied)
    {
        // MT: source whistle at 1000, target clap at 1005.
        var (plan, after) = Copy(Circles((1000, 2)), Circles((1005, 8)), new HitsoundCopyOptions { Mode = CopyMode.OverwriteOnlyDefined, Leniency = leniency });

        Assert.Equal(copied ? 1 : 0, plan.Matched);
        Assert.Contains(copied ? "normal-hitwhistle" : "normal-hitclap", Sounds(after.Moments[0]));
    }

    [Fact]
    public void Overwrite_everything_makes_the_target_play_what_the_source_plays()
    {
        var source = Circles((1000, 2), (1500, 8), (2000, 4), (2500, 0));
        var target = Circles((1000, 8), (1500, 0), (2000, 2), (2500, 14));

        var (_, after) = Copy(source, target, new HitsoundCopyOptions());

        var expected = Decompiler.Decompile(source, NoFiles).Moments.Select(Sounds).ToList();
        Assert.Equal(expected, after.Moments.Select(Sounds).ToList());
    }

    [Fact]
    public void Overwrite_everything_clears_target_hits_the_source_has_nothing_for()
    {
        var (_, after) = Copy(Circles((1000, 2)), Circles((1000, 0), (3000, 14)), new HitsoundCopyOptions());

        Assert.Equal(["normal-hitnormal"], Sounds(after.Moments[1]));
    }

    [Fact]
    public void Overwrite_only_defined_leaves_target_hits_the_source_has_nothing_for()
    {
        var (_, after) = Copy(Circles((1000, 2)), Circles((1000, 0), (3000, 14)), new HitsoundCopyOptions { Mode = CopyMode.OverwriteOnlyDefined });

        Assert.Equal(["normal-hitclap", "normal-hitfinish", "normal-hitnormal", "normal-hitwhistle"], Sounds(after.Moments[1]));
    }

    [Theory]
    [InlineData(true, 5)]
    [InlineData(false, 100)]
    public void A_five_percent_target_volume_stays_only_when_asked(bool preserve, int volume)
    {
        // MT: the source is all 100 %; the target has a 5 % green line over its circle. Only volumes are copied.
        var source = Circles((1000, 2));
        var target = Map("0,500,4,1,0,100,1,0\n500,-100,4,1,0,5,0,0", "256,192,1000,1,0,0:0:0:0:");

        var (_, after) = Copy(source, target, new HitsoundCopyOptions { CopyHitsounds = false, CopyVolumes = true, AlwaysPreserve5Volume = preserve });

        Assert.Equal(volume, after.Moments[0].Volume);
    }

    [Fact]
    public void Without_sample_sets_the_sounds_keep_the_targets_sample_set()
    {
        // A drum whistle in the source; the target's green lines are normal.
        var source = Map("0,500,4,3,0,100,1,0", "256,192,1000,1,2,0:0:0:0:");
        var target = Circles((1000, 0));

        var (_, after) = Copy(source, target, new HitsoundCopyOptions { CopySampleSets = false });

        Assert.Equal(["normal-hitnormal", "normal-hitwhistle"], Sounds(after.Moments[0]));
    }

    [Fact]
    public void Volumes_are_copied_with_the_hits()
    {
        var source = Map("0,500,4,1,0,60,1,0", "256,192,1000,1,2,0:0:0:0:");

        var (_, after) = Copy(source, Circles((1000, 0)), new HitsoundCopyOptions());

        Assert.Equal(60, after.Moments[0].Volume);
    }

    [Fact]
    public void Copying_into_a_mapset_writes_each_target_with_a_backup()
    {
        var dir = Directory.CreateTempSubdirectory("hse-copy-").FullName;
        try
        {
            var target = Path.Combine(dir, "Band - Song (Me) [Insane].osu");
            File.WriteAllBytes(target, Circles((1000, 0), (1500, 0)).ToBytes());
            var source = Circles((1000, 2), (1500, 8));

            var result = Assert.Single(HitsoundCopier.CopyTo(source, NoFiles, [target], new HitsoundCopyOptions(), Path.Combine(dir, "backups")));

            Assert.Null(result.Error);
            Assert.Equal(2, result.Matched);
            Assert.True(File.Exists(result.Backup));
            var after = Decompiler.Decompile(target);
            Assert.Contains("normal-hitwhistle", Sounds(after.Moments[0]));
            Assert.Contains("normal-hitclap", Sounds(after.Moments[1]));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_negative_or_endless_leniency_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HitsoundCopier.Plan(Circles(), NoFiles, Circles(), NoFiles, new HitsoundCopyOptions { Leniency = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => HitsoundCopier.Plan(Circles(), NoFiles, Circles(), NoFiles, new HitsoundCopyOptions { Leniency = double.NaN }));
    }
}

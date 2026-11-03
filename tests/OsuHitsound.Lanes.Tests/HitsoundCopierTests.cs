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
        plan.ApplyExtras(copy);
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

    // One-beat (140 px) and half-beat (70 px) sliders at 1000 ms a beat.
    private static OsuFile Sliders(params string[] sliders) => Map("0,1000,4,1,0,100,1,0", sliders);

    private static Moment Tail(DecompiledMap map, int sliderStart) => map.Moments.Single(m => m.Position == HitPosition.SliderTail && (int)m.Object.Time == sliderStart);

    [Fact]
    public void Muting_sets_eligible_slider_ends_to_the_muted_sample_set_at_five_percent()
    {
        // MT: two one-beat sliders, every snap muted, no minimum length, the muted set Drum.
        var target = Sliders("256,192,100,2,0,L|396:192,1,140,0|0,0:0|0:0,0:0:0:0:", "256,192,2100,2,0,L|396:192,1,140,0|0,0:0|0:0,0:0:0:0:");
        var options = new HitsoundCopyOptions { CopyHitsounds = false, MuteSliderEnds = true, BeatDivisors = [1], MutedDivisors = [1], MinLength = 0, MutedSampleSet = SampleSet.Drum };

        var (plan, after) = Copy(Circles(), target, options);

        Assert.Equal(2, plan.Muted);
        foreach (int start in new[] { 100, 2100 })
        {
            Assert.Equal(["drum-hitnormal"], Sounds(Tail(after, start)));
            Assert.Equal(5, Tail(after, start).Volume);
        }
    }

    [Fact]
    public void With_the_default_filters_a_whole_beat_end_keeps_its_sound_and_a_half_beat_end_is_muted()
    {
        var target = Sliders("256,192,1000,2,0,L|396:192,1,140,0|0,0:0|0:0,0:0:0:0:", "256,192,3000,2,0,L|326:192,1,70,0|0,0:0|0:0,0:0:0:0:");

        var (plan, after) = Copy(Circles(), target, new HitsoundCopyOptions { Mode = CopyMode.OverwriteOnlyDefined, MuteSliderEnds = true });

        Assert.Equal(1, plan.Muted);
        Assert.Equal(100, Tail(after, 1000).Volume); // ends on beat 2
        Assert.Equal(5, Tail(after, 3000).Volume);   // ends half a beat after beat 3
    }

    [Fact]
    public void An_end_with_an_addition_or_with_a_copied_hit_is_not_muted()
    {
        var target = Sliders("256,192,3000,2,0,L|326:192,1,70,0|8,0:0|0:0,0:0:0:0:", "256,192,5000,2,0,L|326:192,1,70,0|0,0:0|0:0,0:0:0:0:");
        var source = Circles((5500, 2)); // a whistle on the second slider's end

        var (plan, after) = Copy(source, target, new HitsoundCopyOptions { Mode = CopyMode.OverwriteOnlyDefined, MuteSliderEnds = true });

        Assert.Equal(0, plan.Muted);
        Assert.Contains("normal-hitclap", Sounds(Tail(after, 3000)));
        Assert.Contains("normal-hitwhistle", Sounds(Tail(after, 5000)));
    }

    private static string[] Body(DecompiledMap map, PassiveKind kind) =>
        [.. map.Passive.Where(p => p.Kind == kind).Select(p => p.Lane.Name)];

    [Fact]
    public void A_matched_slider_takes_the_source_sliders_body_sounds()
    {
        // The source slider's body: a whistle loop, in Soft.
        var source = Sliders("256,192,1000,2,2,L|396:192,1,140,0|0,0:0|0:0,2:0:0:0:");
        var target = Sliders("256,192,1000,2,0,L|396:192,1,140,0|0,0:0|0:0,0:0:0:0:");

        var (_, after) = Copy(source, target, new HitsoundCopyOptions());

        Assert.Equal(["soft-sliderslide"], Body(after, PassiveKind.SliderSlide));
        Assert.Single(Body(after, PassiveKind.SliderWhistle));
    }

    [Fact]
    public void A_slider_without_edge_fields_keeps_its_node_sounds_when_its_body_changes()
    {
        // The target slider has no edge fields: its own clap plays on both nodes. The source has claps on the nodes too,
        // and a whistle loop on its body.
        var source = Sliders("256,192,1000,2,2,L|396:192,1,140,8|8,0:0|0:0,0:0:0:0:");
        var target = Sliders("256,192,1000,2,8,L|396:192,1,140");

        var (_, after) = Copy(source, target, new HitsoundCopyOptions());

        Assert.Single(Body(after, PassiveKind.SliderWhistle));
        Assert.All(after.Moments, m => Assert.Contains("normal-hitclap", Sounds(m)));
        Assert.All(after.Moments, m => Assert.DoesNotContain("normal-hitwhistle", Sounds(m)));
    }

    [Fact]
    public void Overwrite_everything_clears_the_body_of_a_slider_the_source_has_nothing_for()
    {
        var target = Sliders("256,192,5000,2,2,L|396:192,1,140,0|0,0:0|0:0,0:0:0:0:");

        var (_, everything) = Copy(Circles(), target, new HitsoundCopyOptions());
        var (_, defined) = Copy(Circles(), target, new HitsoundCopyOptions { Mode = CopyMode.OverwriteOnlyDefined });

        Assert.Empty(Body(everything, PassiveKind.SliderWhistle));
        Assert.Single(Body(defined, PassiveKind.SliderWhistle));
    }

    // A map with storyboard sample lines in [Events], and circles.
    private static OsuFile WithSamples(string[] samples, params (int Time, string Sample)[] circles) => OsuFile.Parse(Encoding.UTF8.GetBytes(
        "osu file format v14\r\n\r\n[General]\r\nSampleSet: Normal\r\n\r\n[Events]\r\n//Storyboard Sound Samples\r\n" +
        string.Concat(samples.Select(s => s + "\r\n")) + "\r\n[TimingPoints]\r\n0,500,4,1,0,100,1,0\r\n\r\n[HitObjects]\r\n" +
        string.Concat(circles.Select(c => $"256,192,{c.Time},1,{c.Sample}\r\n"))));

    private static List<string> SampleLines(OsuFile file) =>
        [.. file.Lines.Where(l => l.Section == "Events" && l.Text.StartsWith("Sample,", StringComparison.Ordinal)).Select(l => l.Text)];

    private static OsuFile CopySamples(OsuFile source, OsuFile target, HitsoundCopyOptions options, MapFolder? folder = null)
    {
        var plan = HitsoundCopier.Plan(source, folder ?? NoFiles, target, folder ?? NoFiles, options with { CopyStoryboardedSamples = true });
        var copy = OsuFile.Parse(target.ToBytes());
        Compiler.Apply(copy, folder ?? NoFiles, plan.Wanted);
        plan.ApplyExtras(copy);
        return OsuFile.Parse(copy.ToBytes());
    }

    [Fact]
    public void A_storyboard_sample_is_copied_at_its_time()
    {
        var after = CopySamples(WithSamples(["Sample,100,3,\"sample.wav\",80"]), WithSamples([]), new HitsoundCopyOptions { CopyHitsounds = false, IgnoreHitsoundSatisfiedSamples = false });

        Assert.Equal(["Sample,100,3,\"sample.wav\",80"], SampleLines(after));
    }

    [Fact]
    public void A_storyboard_sample_at_a_hit_is_skipped_when_asked()
    {
        var options = new HitsoundCopyOptions { CopyHitsounds = false, IgnoreHitsoundSatisfiedSamples = false, IgnoreWheneverHitsound = true };

        var after = CopySamples(WithSamples(["Sample,1000,3,\"sample.wav\",80"]), WithSamples([], (1000, "2,0:0:0:0:")), options);

        Assert.Empty(SampleLines(after));
    }

    [Fact]
    public void A_repeated_storyboard_sample_is_added_once()
    {
        var source = WithSamples(["Sample,100,3,\"sample.wav\",80", "Sample,100,3,\"sample.wav\",80"]);

        var after = CopySamples(source, WithSamples([]), new HitsoundCopyOptions { CopyHitsounds = false, IgnoreHitsoundSatisfiedSamples = false });

        Assert.Single(SampleLines(after));
    }

    [Fact]
    public void A_storyboard_sample_a_hitsound_already_plays_is_skipped()
    {
        // The target's circle plays normal-hitclap.wav (index 1) where the source has it as a storyboard sample. (Only
        // defined: Overwrite everything would give the circle the source's index 0, and then the file isn't played.)
        var folder = new MapFolder(["normal-hitclap.wav"]);
        var target = WithSamples([], (1000, "8,0:0:1:0:"));
        var source = WithSamples(["Sample,1000,3,\"normal-hitclap.ogg\",100"]); // another extension: the same sample

        var after = CopySamples(source, target, new HitsoundCopyOptions { Mode = CopyMode.OverwriteOnlyDefined, CopyHitsounds = false }, folder);

        Assert.Empty(SampleLines(after));
    }

    [Theory]
    [InlineData(CopyMode.OverwriteEverything, 1)]
    [InlineData(CopyMode.OverwriteOnlyDefined, 2)]
    public void Overwrite_everything_replaces_the_targets_storyboard_samples(CopyMode mode, int count)
    {
        var target = WithSamples(["Sample,500,3,\"old.wav\",100"]);

        var after = CopySamples(WithSamples(["Sample,100,3,\"new.wav\",100"]), target, new HitsoundCopyOptions { Mode = mode, CopyHitsounds = false, IgnoreHitsoundSatisfiedSamples = false });

        Assert.Equal(count, SampleLines(after).Count);
        Assert.Equal("Sample,100,3,\"new.wav\",100", SampleLines(after)[0]); // in time order
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

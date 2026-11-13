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

    private static readonly HitsoundCopyOptions OntoSliders = new() { Mode = CopyMode.OverwriteOnlyDefined, CopyBodyHitsounds = false, CopyToSliderTicks = true, CopyToSliderSlides = true };

    private static (HitsoundCopyPlan Plan, OsuFile After) CopyOnto(OsuFile source, OsuFile target, HitsoundCopyOptions options, MapFolder? folder = null)
    {
        var plan = HitsoundCopier.Plan(source, NoFiles, target, folder ?? NoFiles, options, new CustomSampleIndexes(folder ?? NoFiles, options.StartIndex));
        var copy = OsuFile.Parse(target.ToBytes());
        Compiler.Apply(copy, folder ?? NoFiles, plan.Wanted);
        plan.ApplyExtras(copy);
        return (plan, OsuFile.Parse(copy.ToBytes()));
    }

    [Fact]
    public void A_hit_on_a_slider_body_goes_onto_its_slide_without_a_revert_line()
    {
        // MT: a hit at 500 in the source; the target has a one-beat slider from 0 to 1000 and no hit point near 500.
        var (plan, after) = CopyOnto(Circles((500, 0)), Sliders("256,192,0,2,0,L|396:192,1,140,0|0,0:0|0:0,0:0:0:0:"), OntoSliders with { CopyToSliderTicks = false });

        Assert.Equal(["normal-sliderslide100.wav"], plan.Mixes.Select(m => m.FileName));
        var line = Assert.Single(after.TimingPoints, p => p.Time == 500);
        Assert.Equal(100, line.SampleIndex);
        Assert.DoesNotContain(after.TimingPoints, p => p.Time == 505);
    }

    [Fact]
    public void A_hit_near_a_slider_tick_goes_onto_the_tick_and_the_old_settings_come_back_after_lookup()
    {
        // A two-beat slider (0 to 2000) has a tick at 1000; the source hit is at 1003, within the 5 ms leniency.
        var target = Sliders("256,192,0,2,0,L|396:192,1,280,0|0,0:0|0:0,0:0:0:0:");

        var (plan, after) = CopyOnto(Circles((1003, 8)), target, OntoSliders);

        Assert.Equal(["normal-slidertick100.wav"], plan.Mixes.Select(m => m.FileName));
        double tick = Assert.Single(Decompiler.Decompile(target, NoFiles).Passive, p => p.Kind == PassiveKind.SliderTick).Time;
        var timing = new TimingPointIndex(after.TimingPoints);
        Assert.Equal(100, timing.SamplePointAt(tick)!.SampleIndex);
        Assert.Equal(0, timing.PointAt(Math.Floor(tick + HitsoundRules.ControlPointLeniency) + 1)!.SampleIndex);
        Assert.Equal(SampleSet.None, after.HitObjects[0].HitSample!.Value.NormalSet); // auto: the green lines decide
    }

    [Fact]
    public void An_original_green_line_inside_the_lookup_window_does_not_hide_the_copied_tick()
    {
        var target = Map("0,1000,4,1,0,25,1,0\n1003,-50,4,1,2,40,0,0", "256,192,0,2,0,L|396:192,1,280,0|0,0:0|0:0,0:0:0:0:");
        var (plan, after) = CopyOnto(Circles((1000, 8)), target, OntoSliders with { CopyVolumes = false });
        var decoded = Decompiler.Decompile(after, new MapFolder(plan.Mixes.Select(m => m.FileName)));

        Assert.Equal("normal-slidertick100.wav", Assert.Single(decoded.Passive, p => p.Kind == PassiveKind.SliderTick).Lane.Name);
        var restored = new TimingPointIndex(after.TimingPoints).PointAt(1010)!;
        Assert.Equal(2, restored.SampleIndex);
        Assert.Equal(40, restored.Volume);
        Assert.Equal(-50, restored.BeatLength);
    }

    [Theory]
    [InlineData(1, 200)]
    [InlineData(1.4, 280)]
    public void Copied_ticks_resolve_the_generated_sample_at_integer_and_fractional_times(double multiplier, int length)
    {
        var target = Sliders($"256,192,0,2,0,L|396:192,1,{length},0|0,0:0|0:0,0:0:0:0:");
        target = OsuFile.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(target.ToBytes()).Replace("SliderMultiplier:1.4", $"SliderMultiplier:{multiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)}")));
        var (plan, after) = CopyOnto(Circles((1003, 8)), target, OntoSliders);
        var decoded = Decompiler.Decompile(after, new MapFolder(plan.Mixes.Select(m => m.FileName)));

        Assert.Equal("normal-slidertick100.wav", Assert.Single(decoded.Passive, p => p.Kind == PassiveKind.SliderTick).Lane.Name);
    }

    [Fact]
    public void Copying_a_tick_keeps_the_sliders_bank_and_earlier_sounds()
    {
        var target = Sliders("256,192,0,2,0,L|396:192,1,420,0|0,0:0|0:0,2:0:0:0:");
        var (plan, after) = CopyOnto(Circles((2000, 8)), target, OntoSliders);
        var decoded = Decompiler.Decompile(after, new MapFolder(plan.Mixes.Select(m => m.FileName)));

        Assert.Equal(SampleSet.Soft, after.HitObjects[0].HitSample!.Value.NormalSet);
        Assert.Equal("soft-slidertick", decoded.Passive.First(p => p.Kind == PassiveKind.SliderTick).Lane.Name);
        Assert.Equal("soft-sliderslide", decoded.Passive.First(p => p.Kind == PassiveKind.SliderSlide).Lane.Name);
        Assert.Equal("soft-slidertick100.wav", decoded.Passive.Last(p => p.Kind == PassiveKind.SliderTick).Lane.Name);
    }

    [Theory]
    [InlineData(false, 25, 80, 25)]
    [InlineData(true, 5, 80, 5)]
    [InlineData(true, 25, 80, 80)]
    public void Slider_mix_timing_respects_volume_options(bool copyVolumes, int targetVolume, int sourceVolume, int expected)
    {
        var source = Map($"0,1000,4,1,0,{sourceVolume},1,0", "256,192,500,1,8,0:0:0:0:");
        var target = Map($"0,1000,4,1,0,{targetVolume},1,0", "256,192,0,2,0,L|396:192,1,140,0|0,0:0|0:0,0:0:0:0:");
        var (_, after) = CopyOnto(source, target, OntoSliders with { CopyToSliderTicks = false, CopyVolumes = copyVolumes });

        Assert.Equal(expected, new TimingPointIndex(after.TimingPoints).PointAt(500)!.Volume);
        if (!copyVolumes) Assert.Equal(targetVolume, Decompiler.Decompile(after, NoFiles).Moments.Last().Volume);
    }

    [Fact]
    public void Slider_mixes_do_not_change_indexes_when_sample_set_copying_is_disabled()
    {
        var target = Sliders("256,192,0,2,0,L|396:192,1,280,0|0,0:0|0:0,0:0:0:0:");
        var (plan, after) = CopyOnto(Circles((1000, 8)), target, OntoSliders with { CopySampleSets = false });

        Assert.Empty(plan.Mixes);
        Assert.Equal(target.ToBytes(), after.ToBytes());
    }

    [Fact]
    public void New_samples_skip_indexes_the_mapset_already_uses_and_equal_mixes_share_one()
    {
        var folder = new MapFolder(["normal-hitnormal100.wav"]);
        var target = Sliders("256,192,0,2,0,L|396:192,1,140,0|0,0:0|0:0,0:0:0:0:", "256,192,2000,2,0,L|396:192,1,140,0|0,0:0|0:0,0:0:0:0:");

        var (plan, _) = CopyOnto(Circles((500, 2), (2500, 2)), target, OntoSliders with { CopyToSliderTicks = false }, folder);

        Assert.Equal(["normal-sliderslide101.wav"], plan.Mixes.Select(m => m.FileName));
        Assert.Equal(2, plan.GreenLines.Count);
    }

    [Fact]
    public void Copying_onto_sliders_writes_the_new_sample_into_the_mapset()
    {
        var dir = Directory.CreateTempSubdirectory("hse-copy-").FullName;
        try
        {
            var target = Path.Combine(dir, "Band - Song (Me) [Insane].osu");
            File.WriteAllBytes(target, Sliders("256,192,0,2,0,L|396:192,1,140,0|0,0:0|0:0,0:0:0:0:").ToBytes());

            var result = Assert.Single(HitsoundCopier.CopyTo(Circles((500, 0)), NoFiles, [target], OntoSliders with { CopyToSliderTicks = false },
                Path.Combine(dir, "backups"), render: _ => [1, 2, 3]));

            Assert.Equal(1, result.Generated);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(dir, "normal-sliderslide100.wav")));
            Assert.Contains(OsuFile.Read(target).TimingPoints, p => p.Time == 500 && p.SampleIndex == 100);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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

    // ---- Plan every target, then write all or nothing (issue #28) ----

    private static readonly byte[] Slide = [1, 2, 3];

    // Two slider difficulties in one mapset; copying a circle onto their slides makes one new slide sample.
    private static (string Dir, string A, string B) TwoSliderTargets()
    {
        var dir = Directory.CreateTempSubdirectory("hse-copy-").FullName;
        var a = Path.Combine(dir, "Band - Song (Me) [Insane].osu");
        var b = Path.Combine(dir, "Band - Song (Me) [Hard].osu");
        File.WriteAllBytes(a, Sliders("256,192,0,2,0,L|396:192,1,140,0|0,0:0|0:0,0:0:0:0:").ToBytes());
        File.WriteAllBytes(b, Sliders("256,192,0,2,0,L|396:192,1,140,0|0,0:0|0:0,0:0:0:0:", "256,192,2000,1,0,0:0:0:0:").ToBytes());
        return (dir, a, b);
    }

    private static CopyBatch PlanOntoSlides(string a, string b) =>
        HitsoundCopier.PlanCopy(Circles((500, 0)), NoFiles, [a, b], OntoSliders with { CopyToSliderTicks = false }, canRender: true);

    [Fact]
    public void Planning_a_copy_writes_nothing_and_summarises_each_target()
    {
        var (dir, a, b) = TwoSliderTargets();
        try
        {
            var beforeA = File.ReadAllBytes(a);
            var batch = PlanOntoSlides(a, b);

            Assert.Equal(beforeA, File.ReadAllBytes(a));
            Assert.False(File.Exists(Path.Combine(dir, "normal-sliderslide100.wav")));
            Assert.False(Directory.Exists(Path.Combine(dir, "backups")));
            Assert.All(batch.Targets, t => Assert.True(t.Clean));
            Assert.All(batch.Targets, t => Assert.Contains("green line", t.Changes!.Counts()));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_failed_write_puts_back_the_targets_already_written_and_removes_the_new_samples()
    {
        var (dir, a, b) = TwoSliderTargets();
        try
        {
            var beforeA = File.ReadAllBytes(a);
            var batch = PlanOntoSlides(a, b);
            File.AppendAllText(b, "// edited in osu! meanwhile\r\n"); // B can't be written now

            var results = HitsoundCopier.WriteCopy(batch, [a, b], Path.Combine(dir, "backups"), _ => Slide);

            Assert.Equal(beforeA, File.ReadAllBytes(a));                                  // A was written, then put back
            Assert.False(File.Exists(Path.Combine(dir, "normal-sliderslide100.wav")));    // its new sample removed
            Assert.Contains("Put back", results[0].Error);
            Assert.Contains("changed on disk", results[1].Error);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Ticked_targets_share_one_new_sample_and_unticked_ones_are_left_alone()
    {
        var (dir, a, b) = TwoSliderTargets();
        try
        {
            var beforeB = File.ReadAllBytes(b);
            var batch = PlanOntoSlides(a, b);

            var only = HitsoundCopier.WriteCopy(batch, [a], Path.Combine(dir, "backups"), _ => Slide);
            Assert.Null(only[0].Error);
            Assert.Equal("Skipped (not ticked).", only[1].Error);
            Assert.Equal(beforeB, File.ReadAllBytes(b));
            Assert.Equal(Slide, File.ReadAllBytes(Path.Combine(dir, "normal-sliderslide100.wav")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Both_targets_written_share_the_sample_one_of_them_made()
    {
        var (dir, a, b) = TwoSliderTargets();
        try
        {
            var results = HitsoundCopier.WriteCopy(PlanOntoSlides(a, b), [a, b], Path.Combine(dir, "backups"), _ => Slide);

            Assert.All(results, r => Assert.Null(r.Error));
            Assert.Equal([1, 0], results.Select(r => r.Generated));
            Assert.All([a, b], p => Assert.Contains(OsuFile.Read(p).TimingPoints, t => t.Time == 500 && t.SampleIndex == 100));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // A tick switch at 1005 ms (index 101, drum, 80 %) that reverts 1 ms later, as Plan makes for slider ticks.
    private static OsuFile TickSwitchOnto(string timing)
    {
        var file = Map(timing, "256,192,0,1,0,0:0:0:0:");
        var plan = new HitsoundCopyPlan(new Dictionary<MomentKey, WantedSound>(), 0, 0)
        {
            GreenLines = [(1005, SampleSet.Drum, 101, 80, true)],
        };
        plan.ApplyExtras(file);
        return file;
    }

    [Fact]
    public void A_tick_switch_after_a_line_with_no_sample_set_restores_the_map_default_not_soft()
    {
        // An old-format red line with no sampleSet field plays the map default (Normal here). Restored as 0 it would play Soft.
        var file = TickSwitchOnto("0,500,4");

        var restore = file.TimingPoints.Single(p => p.Time == 1006);
        Assert.Equal(SampleSet.Normal, restore.SampleSet);
    }

    [Fact]
    public void Tick_switch_green_lines_drop_omit_first_bar_line_but_keep_kiai()
    {
        // Effects 9 = kiai (1) + omit first bar line (8). Only red lines may carry the 8.
        var file = TickSwitchOnto("0,500,4,1,0,100,1,9");

        var added = file.TimingPoints.Where(p => p.Time is 1005 or 1006).ToList();
        Assert.Equal(2, added.Count);
        Assert.All(added, p => Assert.Equal(1, p.Effects));
    }

    // ---- Never create silent hitnormals (issue #28) ----

    // A mapset where custom index 101's hitnormal is silent (the check comes from the app, which can decode audio).
    private static readonly MapFolder SilentHitnormal = new(["normal-hitnormal101.wav"]);
    private static bool IsSilent(Lane lane) => lane.Name == "normal-hitnormal101.wav";

    private static HitsoundCopyPlan PlanSilent(OsuFile source, OsuFile target, bool never) =>
        HitsoundCopier.Plan(source, SilentHitnormal, target, SilentHitnormal,
            new HitsoundCopyOptions { NeverSilentHitnormals = never, IsSilent = IsSilent }, new CustomSampleIndexes(SilentHitnormal, 100));

    [Fact]
    public void A_matched_hit_never_takes_a_silent_custom_hitnormal_when_asked()
    {
        // The source plays its silent index-101 hitnormal at 1000; the target's circle there is a plain hitnormal.
        var source = Map("0,500,4,1,101,100,1,0", "256,192,1000,1,0,0:0:0:0:");
        var target = Circles((1000, 0));

        Assert.Single(PlanSilent(source, target, never: false).Wanted); // today: the silent sample is copied

        var plan = PlanSilent(source, target, never: true);
        Assert.Empty(plan.Wanted);           // the target keeps its own sound
        Assert.Equal(1, plan.KeptFromSilent);
    }

    [Fact]
    public void An_unmatched_hit_keeps_its_own_sound_instead_of_a_silent_hitnormal()
    {
        // Overwrite everything: the target's 2000 has no source hit, so it would get the source's hitnormal there,
        // which is the silent index 101.
        var source = Map("0,500,4,1,101,100,1,0", "256,192,1000,1,2,0:0:0:0:");
        var target = Circles((1000, 2), (2000, 8));

        var before = PlanSilent(source, target, never: false);
        Assert.Contains(before.Wanted.Values, w => w.Lanes.Any(IsSilent));

        var plan = PlanSilent(source, target, never: true);
        Assert.DoesNotContain(plan.Wanted.Values, w => w.Lanes.Any(IsSilent));
        Assert.True(plan.KeptFromSilent >= 1);
    }

    [Fact]
    public void Audible_custom_samples_are_still_copied_with_the_option_on()
    {
        var folder = new MapFolder(["normal-hitnormal102.wav"]);
        var source = Map("0,500,4,1,102,100,1,0", "256,192,1000,1,0,0:0:0:0:");
        var plan = HitsoundCopier.Plan(source, folder, Circles((1000, 0)), folder,
            new HitsoundCopyOptions { NeverSilentHitnormals = true, IsSilent = IsSilent }, new CustomSampleIndexes(folder, 100));

        Assert.Single(plan.Wanted);
        Assert.Equal(0, plan.KeptFromSilent);
    }

    [Fact]
    public void A_negative_or_endless_leniency_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HitsoundCopier.Plan(Circles(), NoFiles, Circles(), NoFiles, new HitsoundCopyOptions { Leniency = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => HitsoundCopier.Plan(Circles(), NoFiles, Circles(), NoFiles, new HitsoundCopyOptions { Leniency = double.NaN }));
    }
}

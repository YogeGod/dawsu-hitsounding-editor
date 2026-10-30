using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class DecompilerTests
{
    /// <summary>A minimal map: [General] SampleSet, SliderMultiplier 1.4, the given timing points and objects.</summary>
    private static DecompiledMap Decompile(string timingPoints, string hitObjects, string defaultSet = "Normal", string[]? files = null,
        string extra = "", int version = 14, double tickRate = 1)
    {
        var text = $"osu file format v{version}\r\n\r\n[General]\r\nSampleSet: {defaultSet}\r\nMode: 0\r\n\r\n" +
                   $"[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:{tickRate}\r\n\r\n{extra}" +
                   $"[TimingPoints]\r\n{timingPoints.Replace("\n", "\r\n")}\r\n\r\n[HitObjects]\r\n{hitObjects.Replace("\n", "\r\n")}\r\n";
        return Decompiler.Decompile(OsuFile.Parse(Encoding.UTF8.GetBytes(text)), new MapFolder(files ?? []));
    }

    private static string[] LaneNames(Moment m) => [.. m.Hits.Select(h => h.Lane.Name)];

    [Fact]
    public void Object_inherits_set_index_and_volume_from_the_timing_point()
    {
        var m = Decompile("0,500,4,2,0,70,1,0", "256,192,1000,1,8,0:0:0:0:").Moments.Single();

        Assert.Equal(["soft-hitnormal", "soft-hitclap"], LaneNames(m));
        Assert.All(m.Hits, h => Assert.Equal(LaneSource.Skin, h.Lane.Source));
        Assert.Equal(70, m.Volume);
    }

    [Theory]
    [InlineData("Drum")]
    [InlineData("Normal")]
    public void Timing_point_sample_set_0_plays_soft_whatever_the_maps_default(string defaultSet)
    {
        var m = Decompile("0,500,4,0,0,100,1,0", "256,192,1000,1,0,0:0:0:0:", defaultSet: defaultSet).Moments.Single();
        Assert.Equal(["soft-hitnormal"], LaneNames(m));
    }

    [Fact]
    public void Timing_point_without_a_sample_set_field_uses_the_maps_default_set()
    {
        var m = Decompile("0,500", "256,192,1000,1,0,0:0:0:0:", defaultSet: "Soft").Moments.Single();
        Assert.Equal(["soft-hitnormal"], LaneNames(m));
    }

    [Fact]
    public void Additions_follow_the_normal_set_unless_they_have_their_own()
    {
        var map = Decompile("0,500,4,2,0,100,1,0", "256,192,1000,1,2,3:0:0:0:\n256,192,1500,1,2,3:1:0:0:");
        Assert.Equal(["drum-hitnormal", "drum-hitwhistle"], LaneNames(map.Moments[0]));
        Assert.Equal(["drum-hitnormal", "normal-hitwhistle"], LaneNames(map.Moments[1]));
    }

    [Fact]
    public void Hitnormal_is_layered_when_additions_are_set_without_the_normal_bit()
    {
        var map = Decompile("0,500,4,2,0,100,1,0", "256,192,1000,1,8,0:0:0:0:\n256,192,1500,1,9,0:0:0:0:\n256,192,2000,1,0,0:0:0:0:");
        Assert.True(map.Moments[0].Hits[0].Layered);
        Assert.False(map.Moments[1].Hits[0].Layered);
        Assert.False(map.Moments[2].Hits[0].Layered);
    }

    [Fact]
    public void Circle_index_and_volume_override_the_timing_point()
    {
        var m = Decompile("0,500,4,2,3,70,1,0", "256,192,1000,1,0,0:0:5:40:", files: ["soft-hitnormal5.wav"]).Moments.Single();
        Assert.Equal(new Lane(LaneSource.BeatmapFile, "soft-hitnormal5.wav"), m.Hits[0].Lane);
        Assert.Equal(40, m.Volume);
    }

    [Fact]
    public void Custom_index_files_are_tried_as_wav_then_mp3_then_ogg()
    {
        var m = Decompile("0,500,4,2,2,100,1,0", "256,192,1000,1,0,0:0:0:0:", files: ["soft-hitnormal2.ogg", "soft-hitnormal2.MP3"]).Moments.Single();
        Assert.Equal("soft-hitnormal2.MP3", m.Hits[0].Lane.Name);
    }

    [Fact]
    public void Index_1_has_no_number_and_index_0_is_the_skin()
    {
        var map = Decompile("0,500,4,2,1,100,1,0\n1400,-100,4,2,0,100,0,0", "256,192,1000,1,0,0:0:0:0:\n256,192,1500,1,0,0:0:0:0:",
            files: ["soft-hitnormal.wav"]);
        Assert.Equal(new Lane(LaneSource.BeatmapFile, "soft-hitnormal.wav"), map.Moments[0].Hits[0].Lane);
        Assert.Equal(new Lane(LaneSource.Skin, "soft-hitnormal"), map.Moments[1].Hits[0].Lane);
    }

    [Fact]
    public void Missing_custom_file_falls_back_to_the_skin_not_the_unnumbered_map_file()
    {
        var m = Decompile("0,500,4,2,3,100,1,0", "256,192,1000,1,0,0:0:0:0:", files: ["soft-hitnormal.wav"]).Moments.Single();
        Assert.Equal(new Lane(LaneSource.Skin, "soft-hitnormal"), m.Hits[0].Lane);
    }

    [Fact]
    public void Custom_filename_plays_only_that_file()
    {
        var m = Decompile("0,500,4,2,0,100,1,0", "256,192,1000,1,10,0:0:0:0:kick.wav", files: ["Kick.wav"]).Moments.Single();
        Assert.Equal([new Hit(new Lane(LaneSource.BeatmapFile, "Kick.wav"))], m.Hits);
        Assert.Equal(LaneGroup.OwnFiles, m.Hits[0].Lane.Group);
    }

    [Fact]
    public void Missing_custom_filename_is_a_lane_marked_missing()
    {
        var m = Decompile("0,500,4,2,0,100,1,0", "256,192,1000,1,0,0:0:0:0:gone.wav").Moments.Single();
        Assert.Equal(new Lane(LaneSource.BeatmapFile, "gone.wav", Missing: true), m.Hits[0].Lane);
    }

    [Theory]
    [InlineData(@"..\..\Windows\win.ini")]
    [InlineData(@"\\attacker\share\a.wav")]
    public void Filenames_outside_the_mapset_folder_are_never_looked_up(string filename)
    {
        // The folder "contains" the target under that exact name; it must still not be found.
        var map = Decompile("0,500,4,2,0,100,1,0", $"256,192,1000,1,0,0:0:0:0:{filename}", files: [filename]);
        Assert.True(map.Moments.Single().Hits[0].Lane.Missing);
        Assert.Contains(map.Warnings, w => w.Contains("outside the mapset folder"));
    }

    [Fact]
    public void Drive_paths_cannot_be_written_in_a_filename_because_the_field_ends_at_a_colon()
    {
        // "C:\Windows\..." is read as the filename "C", like osu! does: a missing file inside the folder.
        var m = Decompile("0,500,4,2,0,100,1,0", @"256,192,1000,1,0,0:0:0:0:C:\Windows\Media\ding.wav").Moments.Single();
        Assert.Equal(new Lane(LaneSource.BeatmapFile, "C", Missing: true), m.Hits[0].Lane);
    }

    [Theory]
    [InlineData(1004, 2)] // green line 1 ms after the object's +5 ms lookahead edge: inside
    [InlineData(1005, 2)]
    [InlineData(1006, 1)]
    public void Objects_read_the_timing_point_5ms_ahead(int greenLineTime, int expectedIndex)
    {
        var m = Decompile($"0,500,4,2,1,100,1,0\n{greenLineTime},-100,4,2,2,100,0,0", "256,192,1000,1,0,0:0:0:0:",
            files: ["soft-hitnormal.wav", "soft-hitnormal2.wav"]).Moments.Single();
        Assert.Equal(expectedIndex == 2 ? "soft-hitnormal2.wav" : "soft-hitnormal.wav", m.Hits[0].Lane.Name);
    }

    [Fact]
    public void Among_timing_points_at_the_same_time_the_later_line_wins()
    {
        var m = Decompile("0,500,4,2,0,100,1,0\n1000,-100,4,3,0,100,0,0\n1000,500,4,1,0,100,1,0", "256,192,1000,1,0,0:0:0:0:").Moments.Single();
        Assert.Equal(["normal-hitnormal"], LaneNames(m));
    }

    [Fact]
    public void Before_the_first_timing_point_the_first_point_applies()
    {
        var m = Decompile("2000,500,4,3,0,60,1,0", "256,192,0,1,0,0:0:0:0:").Moments.Single();
        Assert.Equal(["drum-hitnormal"], LaneNames(m));
        Assert.Equal(60, m.Volume);
    }

    [Fact]
    public void Spinner_sounds_at_its_end_with_the_timing_point_there()
    {
        var m = Decompile("0,500,4,2,0,100,1,0\n3000,-100,4,3,0,100,0,0", "256,192,1000,12,4,3000,0:0:0:0:").Moments.Single();
        Assert.Equal((3000.0, HitPosition.SpinnerEnd), (m.Time, m.Position));
        Assert.Equal(["drum-hitnormal", "drum-hitfinish"], LaneNames(m));
    }

    [Fact]
    public void Slider_nodes_are_timed_and_each_reads_its_own_timing_point()
    {
        // 120 BPM, SV 1, multiplier 1.4: 140 px per beat. Length 140, 2 slides: nodes at 1000, 1500, 2000.
        var map = Decompile("0,500,4,2,0,100,1,0\n1500,-100,4,3,0,60,0,0", "100,100,1000,2,0,L|240:100,2,140,2|0|8,0:0|0:0|0:0,0:0:0:0:");

        Assert.Equal([1000.0, 1500.0, 2000.0], map.Moments.Select(m => Math.Round(m.Time, 6)));
        Assert.Equal([HitPosition.SliderHead, HitPosition.SliderRepeat, HitPosition.SliderTail], map.Moments.Select(m => m.Position));
        Assert.Equal(["soft-hitnormal", "soft-hitwhistle"], LaneNames(map.Moments[0]));
        Assert.Equal(["drum-hitnormal"], LaneNames(map.Moments[1]));
        Assert.Equal(60, map.Moments[1].Volume);
        Assert.Equal(["drum-hitnormal", "drum-hitclap"], LaneNames(map.Moments[2]));
    }

    [Fact]
    public void Slider_ignores_its_own_index_and_volume()
    {
        var map = Decompile("0,500,4,2,4,55,1,0", "100,100,1000,2,0,L|240:100,1,140,0|0,,3:0:9:99:", files: ["drum-hitnormal4.wav", "drum-hitnormal9.wav"]);
        Assert.All(map.Moments, m => Assert.Equal(("drum-hitnormal4.wav", 55), (m.Hits[0].Lane.Name, m.Volume)));
    }

    [Fact]
    public void Slider_edge_set_0_inherits_the_timing_point_but_an_empty_entry_keeps_the_slider_banks()
    {
        var map = Decompile("0,500,4,2,0,100,1,0", "100,100,1000,2,0,L|240:100,2,140,0|0|0,0:0||0:0,3:0:0:0:");
        Assert.Equal(["soft-hitnormal", "drum-hitnormal", "soft-hitnormal"], map.Moments.Select(m => m.Hits[0].Lane.Name));
    }

    [Fact]
    public void Slider_body_is_read_once_at_start_plus_6ms()
    {
        // The green line at 1006 is too late for the body (start + 6 = 1006 counts, 1007 doesn't).
        var map = Decompile("0,500,4,2,0,100,1,0\n1007,-100,4,3,0,30,0,0", "100,100,1000,2,2,L|240:100,1,140");

        var slide = map.Passive.Single(p => p.Kind == PassiveKind.SliderSlide);
        Assert.Equal(("soft-sliderslide", 100, 1000.0, 1500.0), (slide.Lane.Name, slide.Volume, slide.Time, Math.Round(slide.EndTime, 6)));
        Assert.Equal("soft-sliderwhistle", map.Passive.Single(p => p.Kind == PassiveKind.SliderWhistle).Lane.Name);
    }

    [Theory]
    [InlineData(1, new[] { 1500.0 })]
    [InlineData(2, new[] { 1250.0, 1500.0, 1750.0 })]
    public void Slider_ticks_follow_the_tick_rate(double tickRate, double[] expected)
    {
        // Two beats long (280 px at 140 px per beat).
        var map = Decompile("0,500,4,2,0,100,1,0", "100,100,1000,2,0,L|380:100,1,280", tickRate: tickRate);
        var ticks = map.Passive.Where(p => p.Kind == PassiveKind.SliderTick).Select(p => Math.Round(p.Time, 6));
        Assert.Equal(expected, ticks);
    }

    [Fact]
    public void Repeat_spans_have_ticks_mirrored_and_nan_green_lines_turn_ticks_off()
    {
        var map = Decompile("0,500,4,2,0,100,1,0", "100,100,1000,2,0,L|380:100,2,280", tickRate: 2);
        Assert.Equal([1250.0, 1500.0, 1750.0, 2250.0, 2500.0, 2750.0], map.Passive.Where(p => p.Kind == PassiveKind.SliderTick).Select(p => Math.Round(p.Time, 6)));

        var off = Decompile("0,500,4,2,0,100,1,0\n900,NaN,4,2,0,100,0,0", "100,100,1000,2,0,L|380:100,1,280", tickRate: 2);
        Assert.DoesNotContain(off.Passive, p => p.Kind == PassiveKind.SliderTick);
    }

    [Fact]
    public void Green_line_sv_applies_even_when_a_red_line_at_the_same_time_comes_later_in_the_file()
    {
        // SV 2 (beatLength -50): 280 px per beat at 120 BPM, so a 280 px slider at 1000 ends at 1500 (SV 1 would end at 2000).
        var map = Decompile("0,-50,4,2,0,100,0,0\n0,500,4,2,0,100,1,0", "100,100,1000,2,0,L|380:100,1,280");
        Assert.Equal(1500.0, Math.Round(map.Moments[1].Time, 6));
    }

    [Fact]
    public void A_slider_that_would_have_too_many_ticks_shows_none()
    {
        var map = Decompile("0,500,4,2,0,100,1,0", "100,100,1000,2,0,L|380:100,9001,100000", tickRate: 8);
        Assert.DoesNotContain(map.Passive, p => p.Kind == PassiveKind.SliderTick);
        Assert.Contains(map.Warnings, w => w.Contains("ticks are not shown"));
    }

    [Fact]
    public void Object_volume_above_100_is_clamped_and_a_numeric_default_set_outside_the_enum_means_normal()
    {
        var m = Decompile("0,500", "256,192,1000,1,0,0:0:0:250:", defaultSet: "7").Moments.Single();
        Assert.Equal(100, m.Volume);
        Assert.Equal(["normal-hitnormal"], LaneNames(m));
    }

    [Fact]
    public void Storyboard_samples_from_events_and_osb_are_read_only_lanes()
    {
        var osb = "[Variables]\r\n$fx = sfx/boom\r\n\r\n[Events]\r\nSample,2000,0,\"$fx.wav\",70\r\n";
        var text = "osu file format v14\r\n\r\n[Events]\r\nSample,1000,3,\"Crash.ogg\"\r\n5,1500,0,\"..\\outside.wav\",50\r\n";
        var map = Decompiler.Decompile(OsuFile.Parse(Encoding.UTF8.GetBytes(text)),
            new MapFolder(["crash.ogg", "sfx/boom.wav", "map.osb"], _ => Encoding.UTF8.GetBytes(osb)));

        var samples = map.Passive.Where(p => p.Kind == PassiveKind.StoryboardSample).ToList();
        Assert.Equal([(1000.0, "crash.ogg", false, 100), (1500.0, "../outside.wav", true, 50), (2000.0, "sfx/boom.wav", false, 70)],
            samples.Select(s => (s.Time, s.Lane.Name, s.Lane.Missing, s.Volume)));
        Assert.All(samples, s => Assert.Equal(LaneSource.StoryboardFile, s.Lane.Source));
    }

    [Fact]
    public void Old_format_maps_are_shown_24ms_later()
    {
        var m = Decompile("0,500,4,2,0,100,1,0", "256,192,1000,1,0", version: 4).Moments.Single();
        Assert.Equal(1024, m.Time);
    }

    [Fact]
    public void Lanes_are_grouped_for_the_ui()
    {
        var map = Decompile("0,500,4,2,2,100,1,0", "256,192,1000,1,8,0:3:0:0:\n256,192,1500,1,0,0:0:0:0:kick.wav",
            files: ["soft-hitnormal2.wav", "kick.wav"]);
        Assert.Equal([(LaneGroup.SoftSet, "soft-hitnormal2.wav"), (LaneGroup.OwnFiles, "kick.wav"), (LaneGroup.Skin, "drum-hitclap")],
            map.Lanes.Select(l => (l.Group, l.Name)));
    }

    [Fact]
    public void Non_standard_modes_and_broken_lines_are_warnings_not_errors()
    {
        var text = "osu file format v14\r\n\r\n[General]\r\nMode: 3\r\n\r\n[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n" +
                   "[HitObjects]\r\n256,192,1000,128,0,1500:0:0:0:0:\r\n256,192,abc,1,0\r\n256,192,2000,1,0\r\n";
        var map = Decompiler.Decompile(OsuFile.Parse(Encoding.UTF8.GetBytes(text)), new MapFolder([]));

        Assert.Single(map.Moments);
        Assert.Equal(3, map.Warnings.Count);
    }
}

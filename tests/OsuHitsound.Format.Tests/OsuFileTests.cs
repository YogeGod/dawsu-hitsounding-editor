using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Format.Tests;

public class OsuFileTests
{
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static string Text(OsuFile f) => Encoding.UTF8.GetString(f.ToBytes());

    [Theory]
    [InlineData("")]
    [InlineData("osu file format v14\r\n\r\n[General]\r\nMode: 0\r\n")]
    [InlineData("osu file format v14\n[General]\nMode: 0")]                // LF, no final newline
    [InlineData("a\r\nb\nc\rd\n\re")]                                     // mixed and odd endings
    [InlineData("﻿osu file format v9\r\n[HitObjects]\r\n1,2,3,1,0\r\n")] // BOM
    public void Unedited_file_writes_back_identical_bytes(string content)
    {
        var bytes = Bytes(content);
        Assert.Equal(bytes, OsuFile.Parse(bytes).ToBytes());
    }

    [Fact]
    public void Invalid_utf8_survives_when_another_line_is_edited()
    {
        byte[] latin1Title = [.. Bytes("Title:Caf"), 0xE9, .. Bytes("\r\n")];
        byte[] bytes = [.. Bytes("[Metadata]\r\n"), .. latin1Title, .. Bytes("[HitObjects]\r\n64,192,1000,1,0,0:0:0:0:\r\n")];

        var file = OsuFile.Parse(bytes);
        file.HitObjects.Single().HitSound = HitSound.Clap;

        byte[] expected = [.. Bytes("[Metadata]\r\n"), .. latin1Title, .. Bytes("[HitObjects]\r\n64,192,1000,1,8,0:0:0:0:\r\n")];
        Assert.Equal(expected, file.ToBytes());
    }

    [Fact]
    public void Editing_one_field_changes_only_that_field()
    {
        var file = OsuFile.Parse(Bytes("﻿[HitObjects]\n64,192,1000,1,0,0:0:0:0:\n128,192,1500,1,0\n"));
        file.HitObjects.First().HitSound = HitSound.Whistle | HitSound.Finish;

        Assert.Equal("﻿[HitObjects]\n64,192,1000,1,6,0:0:0:0:\n128,192,1500,1,0\n", Text(file));
        Assert.Equal(1, file.Lines.Count(l => l.IsEdited));
    }

    [Fact]
    public void Setting_the_same_value_in_a_different_spelling_changes_nothing()
    {
        var file = OsuFile.Parse(Bytes("[TimingPoints]\r\n0,500.0,4,2,1,100.0,1,0\r\n[HitObjects]\r\n64,192,1000,1,0,1:0\r\n"));
        var tp = file.TimingPoints.Single();
        var ho = file.HitObjects.Single();

        tp.Volume = 100;
        tp.SampleSet = SampleSet.Soft;
        ho.HitSample = new HitSample(SampleSet.Normal, SampleSet.None, 0, 0, null);

        Assert.False(file.IsEdited);
    }

    [Fact]
    public void A_changed_hit_sample_is_written_in_canonical_form()
    {
        var file = OsuFile.Parse(Bytes("[HitObjects]\r\n64,192,1000,1,0,1:0\r\n"));
        file.HitObjects.Single().HitSample = new HitSample(SampleSet.Soft, SampleSet.Drum, 3, 70, null);
        Assert.Equal("[HitObjects]\r\n64,192,1000,1,0,2:3:3:70:\r\n", Text(file));
    }

    [Fact]
    public void A_missing_hit_sample_is_appended_only_when_it_differs_from_the_default()
    {
        var file = OsuFile.Parse(Bytes("[HitObjects]\r\n64,192,1000,1,0\r\n"));
        var ho = file.HitObjects.Single();

        ho.HitSample = new HitSample();
        Assert.False(file.IsEdited);

        ho.HitSample = new HitSample(SampleSet.None, SampleSet.None, 2, 0, null);
        Assert.Equal("[HitObjects]\r\n64,192,1000,1,0,0:0:2:0:\r\n", Text(file));
    }

    [Fact]
    public void Slider_fields_parse()
    {
        var ho = OsuFile.Parse(Bytes("[HitObjects]\r\n64,192,1000,6,2,B|200:100|300:192,2,250.5,2|0|8,1:2|0:0|3:0,0:0:0:0:\r\n")).HitObjects.Single();

        Assert.Equal(HitObjectKind.Slider, ho.Kind);
        Assert.Equal(2, ho.Slides);
        Assert.Equal(250.5, ho.Length);
        Assert.Equal([HitSound.Whistle, HitSound.None, HitSound.Clap], ho.EdgeSounds);
        Assert.Equal([(SampleSet.Normal, SampleSet.Soft), (SampleSet.None, SampleSet.None), (SampleSet.Drum, SampleSet.None)], ho.EdgeSets);
        Assert.Equal(new HitSample(), ho.HitSample);
        Assert.Equal([(200.0, 100.0), (300.0, 192.0)], ho.CurvePoints);
    }

    [Fact]
    public void Slider_nodes_fall_back_to_the_sliders_own_sounds_and_sets()
    {
        // Two edgeSounds for four nodes; the second edgeSets entry is empty, the fourth missing.
        var ho = OsuFile.Parse(Bytes("[HitObjects]\r\n64,192,1000,6,8,B|200:100,3,100,2|0,1:2||0:0,3:1:0:0:\r\n")).HitObjects.Single();

        Assert.Equal(4, ho.NodeCount);
        Assert.Equal([HitSound.Whistle, HitSound.None, HitSound.Clap, HitSound.Clap], ho.NodeSounds);
        Assert.Equal(
            [(SampleSet.Normal, SampleSet.Soft), (SampleSet.Drum, SampleSet.Normal), (SampleSet.None, SampleSet.None), (SampleSet.Drum, SampleSet.Normal)],
            ho.NodeSets);
    }

    [Fact]
    public void Slider_node_count_is_clamped_like_osu()
    {
        var ho = OsuFile.Parse(Bytes("[HitObjects]\r\n64,192,1000,2,0,L|200:100,0,100\r\n")).HitObjects.Single();
        Assert.Equal(2, ho.NodeCount);
        Assert.Equal([HitSound.None, HitSound.None], ho.NodeSounds);
    }

    [Fact]
    public void Old_short_timing_points_report_missing_fields_as_null()
    {
        var tp = OsuFile.Parse(Bytes("[TimingPoints]\r\n1000,500\r\n")).TimingPoints.Single();
        Assert.Null(tp.SampleSet);
        Assert.Null(tp.Volume);
        Assert.True(tp.Uninherited);
    }

    [Fact]
    public void Setting_a_field_on_a_short_timing_point_fills_the_missing_fields_with_defaults()
    {
        var file = OsuFile.Parse(Bytes("[TimingPoints]\r\n1000,500\r\n"));
        file.TimingPoints.Single().Volume = 60;
        Assert.Equal("[TimingPoints]\r\n1000,500,4,0,0,60\r\n", Text(file));
    }

    [Fact]
    public void Setting_hit_sample_on_a_slider_without_edge_fields_adds_one_entry_per_node()
    {
        var file = OsuFile.Parse(Bytes("[HitObjects]\r\n64,192,1000,2,0,B|200:100,2,140\r\n"));
        file.HitObjects.Single().HitSample = new HitSample(SampleSet.Soft, SampleSet.None, 2, 0, null);
        Assert.Equal("[HitObjects]\r\n64,192,1000,2,0,B|200:100,2,140,0|0|0,0:0|0:0|0:0,2:0:2:0:\r\n", Text(file));
    }

    [Fact]
    public void Editing_a_line_that_is_not_utf8_is_refused()
    {
        byte[] bytes = [.. Bytes("[HitObjects]\r\n64,192,1000,1,0,0:0:0:0:caf"), 0xE9, .. Bytes(".wav\r\n")];
        var ho = OsuFile.Parse(bytes).HitObjects.Single();
        Assert.Throws<InvalidOperationException>(() => ho.HitSound = HitSound.Clap);
    }

    [Theory]
    [InlineData("9999999999")]
    [InlineData("NaN")]
    public void Integers_out_of_range_are_format_errors(string hitSound)
    {
        var ho = OsuFile.Parse(Bytes($"[HitObjects]\r\n64,192,1000,1,{hitSound}\r\n")).HitObjects.Single();
        Assert.Throws<FormatException>(() => ho.HitSound);
    }

    [Fact]
    public void Comments_and_blank_lines_are_not_data()
    {
        var file = OsuFile.Parse(Bytes("[TimingPoints]\r\n// note, with comma\r\n\r\n0,500,4,1,0,100,1,0\r\n"));
        Assert.Single(file.TimingPoints);
    }
}

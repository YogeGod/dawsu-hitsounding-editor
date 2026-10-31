using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

/// <summary>Editor-only time signatures (docs/spec-wave2.md, part 4).</summary>
public class TimeSignatureTests
{
    // 120 BPM 4/4 from 0: a bar is 2000 ms, so bar 3 starts at 4000.
    private const string Text = "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\nSampleSet: Soft\r\n\r\n" +
                                "[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
                                "[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n256,192,1000,1,8,0:0:0:0:\r\n";

    private static (ArrangeModel Model, OsuFile File) Load(string? timing = null)
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(timing is null ? Text : Text.Replace("0,500,4,2,0,100,1,0", timing)));
        var session = new EditSession(file, new MapFolder([]), Decompiler.Decompile(file, new MapFolder([])));
        return (new ArrangeModel(session, file, new HashSet<Lane>()), file);
    }

    [Fact]
    public void Without_changes_bars_follow_the_red_lines()
    {
        var (m, _) = Load();
        Assert.Equal((4, 4), m.SignatureAt(4500));
        Assert.Equal((3, 1, 1), m.BarBeatTick(4000));
    }

    [Fact]
    public void A_change_draws_its_bars_from_its_bar_on_and_counts_beats_in_its_unit()
    {
        var (m, _) = Load();
        m.SetTimeSignatures([new TimeSignature(3, 7, 8)]);

        Assert.Equal((4, 4), m.SignatureAt(3999));
        Assert.Equal((7, 8), m.SignatureAt(4000));
        var bars = m.GridLines(3000, 8000).Where(l => l.IsBar).Select(l => l.Time).ToList();
        Assert.Equal([4000.0, 5750, 7500], bars); // 7 eighths = 1750 ms
        Assert.Equal((3, 2, 1), m.BarBeatTick(4250)); // beats are eighths in 7/8
        Assert.Equal((4, 1, 1), m.BarBeatTick(5750));
    }

    [Fact]
    public void A_change_lasts_until_the_next_one()
    {
        var (m, _) = Load();
        m.SetTimeSignatures([new TimeSignature(2, 3, 4), new TimeSignature(4, 4, 4)]);
        // Bar 2 at 2000, bars of 1500 ms: bar 3 at 3500, bar 4 at 5000, then 2000 ms bars again.
        Assert.Equal([2000.0, 3500, 5000, 7000], m.GridLines(1500, 7000).Where(l => l.IsBar).Select(l => l.Time));
    }

    [Fact]
    public void Snapping_keeps_the_maps_beat_whatever_the_signature()
    {
        var (m, _) = Load();
        m.SetTimeSignatures([new TimeSignature(3, 7, 8)]);
        Assert.Equal(4125, m.Snap(4130, 4)); // 1/4 of the map's 500 ms beat, from the bar start
    }

    [Fact]
    public void After_an_odd_length_bar_snapping_stays_on_the_maps_beats()
    {
        var (m, _) = Load();
        m.SetTimeSignatures([new TimeSignature(3, 7, 8), new TimeSignature(4, 4, 4)]); // bar 4 starts half a beat off, at 5750
        Assert.Equal(6000, m.Snap(6000, 1));
        Assert.Equal(6000, m.Snap(6000, 3), 6);
        Assert.Contains(m.SubLines(5500, 6600, 2), l => l.Time == 6250); // the 1/2 line of the map's beat, not of the bar
        Assert.Equal(5750, m.GridLines(5700, 5800).Single(l => l.IsBar).Time);
    }

    [Fact]
    public void A_bar_1_time_signature_keeps_the_first_red_line_on_a_downbeat()
    {
        var (m, _) = Load("1000,500,4,2,0,100,1,0");
        m.SetTimeSignatures([new TimeSignature(1, 7, 8)]);
        var (_, beat, tick) = m.BarBeatTick(1000);
        Assert.Equal((1, 1), (beat, tick));
    }

    [Fact]
    public void In_3_2_the_maps_in_between_beats_still_get_a_line()
    {
        var (m, _) = Load();
        m.SetTimeSignatures([new TimeSignature(1, 3, 2)]); // beats of 1000 ms; the map's beat is 500
        Assert.Contains(m.SubLines(0, 2000, 1), l => l.Time == 500);
    }

    [Fact]
    public void Time_signatures_never_touch_the_map()
    {
        var (m, file) = Load();
        m.SetTimeSignatures([new TimeSignature(3, 7, 8)]);
        Assert.Equal(Encoding.UTF8.GetBytes(Text), file.ToBytes());
    }
}

/// <summary>View → Colour by Volume (docs/spec-wave2.md, part 5).</summary>
public class VolumeColourTests
{
    private static double Light(Avalonia.Media.Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    [Fact]
    public void Louder_notes_are_brighter_on_one_ramp_for_every_lane()
    {
        var levels = new[] { 0, 10, 50, 90, 100 }.Select(v => Light(ArrangeView.VolumeColor(v))).ToList();
        Assert.Equal(levels.Order(), levels);
        Assert.True(levels[^1] - levels[0] > 150);
        Assert.Equal(ArrangeView.VolumeColor(100), ArrangeView.VolumeColor(150)); // clamped
    }
}

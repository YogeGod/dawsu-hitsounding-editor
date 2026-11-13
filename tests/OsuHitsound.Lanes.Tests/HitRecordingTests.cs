using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class HitRecordingTests
{
    private static EditSession Load(string hitObjects)
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(
            "osu file format v14\r\n\r\n[General]\r\nSampleSet: Soft\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
            $"[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n{hitObjects.Replace("\n", "\r\n")}\r\n"));
        var folder = new MapFolder([]);
        return new EditSession(file, folder, Decompiler.Decompile(file, folder));
    }

    private static Lane Skin(string name) => new(LaneSource.Skin, name);

    // The quarter-beat grid of the 120 BPM map above.
    private static double Grid(double ms) => Math.Round(ms / 125) * 125;

    [Fact]
    public void Taps_snap_to_the_grid_and_land_on_objects_there()
    {
        var s = Load("256,192,1000,1,0,0:0:0:0:");
        var clap = Skin("soft-hitclap");
        var recorder = new HitRecorder(900);
        recorder.Tap(1012, clap); // snaps to 1000, where the circle is
        recorder.Tap(1260, clap); // snaps to 1250, no object
        var hits = HitRecording.Apply(s, recorder.Finish(1500), Grid);

        Assert.Equal(2, hits.Count);
        Assert.Contains(s.State.Hits, h => h.Lane == clap && h.Time == 1000 && h.Anchor is not null);
        Assert.Contains(s.State.Hits, h => h.Lane == clap && h.Time == 1250 && h.Anchor is null);
    }

    [Fact]
    public void A_take_replaces_the_tapped_lanes_in_its_range_and_keeps_the_rest()
    {
        var s = Load("256,192,1000,1,8,0:0:0:0:\n256,192,2000,1,8,0:0:0:0:\n256,192,3000,1,8,0:0:0:0:");
        var clap = Skin("soft-hitclap");
        Assert.Equal(3, s.State.Hits.Count(h => h.Lane == clap));
        var recorder = new HitRecorder(1500);
        recorder.Tap(2240, clap);
        HitRecording.Apply(s, recorder.Finish(2600), Grid);

        var claps = s.State.Hits.Where(h => h.Lane == clap).Select(h => h.Time).Order().ToList();
        Assert.Equal([1000, 2250, 3000], claps); // the clap at 2000 is replaced; before and after stay
        Assert.Equal(3, s.State.Hits.Count(h => h.Lane.Name == "soft-hitnormal")); // a lane not tapped is untouched
    }

    [Fact]
    public void A_whole_take_is_one_undo_step()
    {
        var s = Load("256,192,1000,1,8,0:0:0:0:");
        var before = s.State;
        var recorder = new HitRecorder(0);
        recorder.Tap(500, Skin("soft-hitwhistle"));
        recorder.Tap(1000, Skin("soft-hitfinish"));
        HitRecording.Apply(s, recorder.Finish(2000), Grid);
        Assert.Equal("Record", Assert.Single(s.Steps));

        s.Undo();
        Assert.Equal(before.Hits, s.State.Hits);
    }

    [Fact]
    public void Nothing_tapped_changes_nothing()
    {
        var s = Load("256,192,1000,1,8,0:0:0:0:");
        Assert.Empty(HitRecording.Apply(s, new HitRecorder(0).Finish(5000), Grid));
        Assert.Empty(s.Steps);
    }

    [Fact]
    public void Taps_in_the_count_in_are_ignored_but_a_slightly_early_first_beat_counts()
    {
        var recorder = new HitRecorder(2000);
        recorder.Tap(1500, Skin("soft-hitclap"));
        recorder.Tap(1970, Skin("soft-hitclap"));
        var tap = Assert.Single(recorder.Finish(3000).Taps);
        Assert.Equal(1970, tap.Time);
    }

    [Fact]
    public void Double_taps_on_one_grid_line_make_one_hit()
    {
        var s = Load("256,192,1000,1,0,0:0:0:0:");
        var recorder = new HitRecorder(0);
        recorder.Tap(1240, Skin("soft-hitclap"));
        recorder.Tap(1255, Skin("soft-hitclap"));
        Assert.Single(HitRecording.Apply(s, recorder.Finish(2000), Grid));
    }
}

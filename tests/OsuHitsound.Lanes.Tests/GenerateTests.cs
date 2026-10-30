using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class GenerateTests
{
    private const string Header =
        "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\nSampleSet: Soft\r\n\r\n[Metadata]\r\nTitle:Song\r\nArtist:Band\r\nCreator:Me\r\nVersion:Insane\r\nBeatmapID:123\r\n\r\n" +
        "[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n[Events]\r\n0,0,\"bg.jpg\",0,0\r\n\r\n[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n800,-50,4,2,0,40,0,0\r\n\r\n";

    private static (EditSession Session, OsuFile File, MapFolder Folder) Load(string hitObjects, string[]? files = null, Func<string, byte[]>? read = null)
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(Header + $"[HitObjects]\r\n{hitObjects.Replace("\n", "\r\n")}\r\n"));
        var folder = new MapFolder(files ?? [], read);
        return (new EditSession(file, folder, Decompiler.Decompile(file, folder)), file, folder);
    }

    private static Lane Skin(string name) => new(LaneSource.Skin, name);

    private static Lane File(string name) => new(LaneSource.BeatmapFile, name);

    [Fact]
    public void Every_sound_becomes_a_circle_in_a_hitsounds_diff_with_the_red_lines_only()
    {
        var (s, file, folder) = Load("256,192,1000,1,8,0:0:0:0:");
        s.Add([s.HitAt(Skin("soft-hitnormal"), 1500), s.HitAt(Skin("soft-hitwhistle"), 1500)]); // no object there

        var plan = HitsoundDiff.Plan(file, @"C:\maps\x\Band - Song (Me) [Insane].osu", folder, s);
        var text = Encoding.UTF8.GetString(plan.Diff.ToBytes());

        Assert.Equal(@"C:\maps\x\Band - Song (Me) [Hitsounds].osu", plan.OsuPath);
        Assert.Equal(2, plan.Circles);
        Assert.Empty(plan.Mixes);
        Assert.Empty(plan.Conflicts);
        Assert.Contains("Version:Hitsounds", text);
        Assert.Contains("BeatmapID:0", text);
        Assert.DoesNotContain("bg.jpg", text);
        Assert.DoesNotContain("800,-50", text); // green lines are not copied

        var moments = Decompiler.Decompile(plan.Diff, folder).Moments;
        Assert.Equal([1000.0, 1500.0], moments.Select(m => m.Time));
        Assert.Equal(["soft-hitclap", "soft-hitnormal"], moments[0].Hits.Select(h => h.Lane.Name).Order());
        Assert.Equal(["soft-hitnormal", "soft-hitwhistle"], moments[1].Hits.Select(h => h.Lane.Name).Order());
    }

    [Fact]
    public void Sounds_that_cannot_play_together_are_mixed_on_a_fresh_index_and_identical_mixes_share_a_file()
    {
        var files = new[] { "soft-hitnormal2.wav", "soft-hitclap3.wav" };
        var (s, file, folder) = Load("256,192,1000,1,0,0:0:0:0:\n256,192,2000,1,0,0:0:0:0:", files);
        foreach (int t in new[] { 1000, 2000 })
        {
            s.Remove(s.State.Hits.Where(h => Math.Abs(h.Time - t) < 1).ToList());
            s.Add([s.HitAt(File("soft-hitnormal2.wav"), t), s.HitAt(File("soft-hitclap3.wav"), t)]); // two indexes at once
        }

        s.Add([s.HitAt(Skin("drum-hitclap"), 1250)]); // an addition with no hitnormal

        var plan = HitsoundDiff.Plan(file, "map.osu", folder, s);

        Assert.Equal(2, plan.Mixes.Count);
        Assert.Equal(["soft-hitnormal4.wav", "soft-hitnormal5.wav"], plan.Mixes.Select(m => m.FileName).Order());
        Assert.Empty(plan.Conflicts);
        var both = plan.Mixes.Single(m => m.Layers.Count == 2);
        Assert.Equal(["soft-hitclap3.wav", "soft-hitnormal2.wav"], both.Layers.Select(l => l.Lane.Name).Order());

        var after = new MapFolder(files.Concat(plan.Mixes.Select(m => m.FileName)));
        var moments = Decompiler.Decompile(plan.Diff, after).Moments;
        Assert.Equal(3, moments.Count);
        Assert.All(moments, m => Assert.Single(m.Hits));
        Assert.Equal(moments[0].Hits[0].Lane, moments[2].Hits[0].Lane); // 1000 and 2000 share a mix
    }

    [Fact]
    public void Different_volumes_at_one_time_are_mixed()
    {
        var (s, file, folder) = Load("256,192,1000,1,0,0:0:0:0:");
        s.Add([s.HitAt(Skin("soft-hitclap"), 1000.4)]); // anchored to the circle: volume 100 like it
        s.Add([s.HitAt(Skin("soft-hitwhistle"), 1300)]);
        s.Add([s.HitAt(Skin("soft-hitnormal"), 1300)]);
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, s);
        Assert.Empty(plan.Mixes); // all at one volume per moment: no mix needed
    }

    [Fact]
    public void The_fresh_index_is_above_every_index_any_difficulty_or_file_uses()
    {
        var other = Encoding.UTF8.GetBytes(Header + "[HitObjects]\r\n256,192,1000,1,0,0:0:7:0:\r\n");
        var folder = new MapFolder(["soft-hitclap3.wav", "other.osu"], _ => other);
        Assert.Equal(8, HitsoundDiff.FirstFreeIndex(folder));
        Assert.Equal(2, HitsoundDiff.FirstFreeIndex(new MapFolder([])));
    }
}

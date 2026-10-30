using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class CompilerTests
{
    private const string Red = "0,500,4,2,0,100,1,0"; // 120 BPM, soft, index 0, volume 100

    private static OsuFile Map(string timingPoints, string hitObjects, int version = 14) => OsuFile.Parse(Encoding.UTF8.GetBytes(
        $"osu file format v{version}\r\n\r\n[General]\r\nSampleSet: Soft\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
        $"[TimingPoints]\r\n{timingPoints.Replace("\n", "\r\n")}\r\n\r\n[HitObjects]\r\n{hitObjects.Replace("\n", "\r\n")}\r\n"));

    private static Lane Skin(string name) => new(LaneSource.Skin, name);

    private static Lane File(string name) => new(LaneSource.BeatmapFile, name);

    /// <summary>Applies wishes keyed by moment number (in time order) and returns the result and the re-decompiled map.</summary>
    private static (CompileResult Result, DecompiledMap After, OsuFile File) Apply(OsuFile file, MapFolder folder, params (int Moment, Lane[] Lanes, int Volume)[] wishes)
    {
        var before = Decompiler.Decompile(file, folder);
        var wanted = wishes.ToDictionary(w => Compiler.KeyOf(file, before.Moments[w.Moment]), w => new WantedSound(w.Lanes, w.Volume));
        var result = Compiler.Apply(file, folder, wanted);
        return (result, Decompiler.Decompile(file, folder), file);
    }

    private static string[] Names(Moment m) => [.. m.Hits.Select(h => h.Lane.Name).Order()];

    private static string Text(OsuFile file) => Encoding.UTF8.GetString(file.ToBytes());

    [Fact]
    public void Nothing_wanted_changes_nothing()
    {
        var file = Map(Red, "256,192,1000,1,8,0:0:0:0:\n100,100,1500,2,0,L|240:100,2,140,2|0|8,0:0|0:0|0:0,0:0:0:0:");
        var (result, _, _) = Apply(file, new MapFolder([]));
        Assert.Empty(result.Conflicts);
        Assert.False(file.IsEdited);
    }

    [Fact]
    public void Adding_an_addition_sets_only_the_sound_flag()
    {
        var file = Map(Red, "256,192,1000,1,0,0:0:0:0:");
        var (result, after, _) = Apply(file, new MapFolder([]), (0, [Skin("soft-hitnormal"), Skin("soft-hitclap")], 100));

        Assert.Empty(result.Conflicts);
        Assert.Contains("256,192,1000,1,8,0:0:0:0:", Text(file));
        Assert.Equal(["soft-hitclap", "soft-hitnormal"], Names(after.Moments[0]));
    }

    [Fact]
    public void Addition_set_is_written_only_when_it_differs()
    {
        var file = Map(Red, "256,192,1000,1,0,0:0:0:0:");
        var (result, after, _) = Apply(file, new MapFolder([]), (0, [Skin("soft-hitnormal"), Skin("drum-hitwhistle")], 100));

        Assert.Empty(result.Conflicts);
        Assert.Contains("256,192,1000,1,2,0:3:0:0:", Text(file));
        Assert.Equal(["drum-hitwhistle", "soft-hitnormal"], Names(after.Moments[0]));
    }

    [Fact]
    public void Circles_take_a_custom_index_and_volume_on_themselves()
    {
        var folder = new MapFolder(["soft-hitnormal2.wav", "soft-hitclap2.wav"]);
        var file = Map(Red, "256,192,1000,1,0,0:0:0:0:\n256,192,1500,1,0,0:0:0:0:");
        var (result, after, _) = Apply(file, folder, (0, [File("soft-hitnormal2.wav"), File("soft-hitclap2.wav")], 60));

        Assert.Empty(result.Conflicts);
        Assert.Contains("256,192,1000,1,8,0:0:2:60:", Text(file));
        Assert.Equal(["soft-hitclap2.wav", "soft-hitnormal2.wav"], Names(after.Moments[0]));
        Assert.Equal(60, after.Moments[0].Volume);
        Assert.Equal(["soft-hitnormal"], Names(after.Moments[1])); // untouched
        Assert.Equal(0, result.TimingPointsAdded);
    }

    [Fact]
    public void A_skin_sound_next_to_a_custom_file_needs_an_index_without_that_skin_sounds_file()
    {
        // Index 2 has a hitnormal but no clap file, so a skin clap can share index 2.
        var folder = new MapFolder(["soft-hitnormal2.wav"]);
        var file = Map(Red, "256,192,1000,1,0,0:0:0:0:");
        var (result, after, _) = Apply(file, folder, (0, [File("soft-hitnormal2.wav"), Skin("soft-hitclap")], 100));

        Assert.Empty(result.Conflicts);
        Assert.Equal(["soft-hitclap", "soft-hitnormal2.wav"], Names(after.Moments[0]));
    }

    [Fact]
    public void Slider_nodes_get_a_green_line_and_the_next_node_gets_the_old_settings_back()
    {
        var folder = new MapFolder(["soft-hitnormal3.wav"]);
        // Nodes at 1000, 1500, 2000 (120 BPM, 140 px per beat).
        var file = Map(Red, "100,100,1000,2,0,L|240:100,2,140,0|0|0,0:0|0:0|0:0,0:0:0:0:");
        var (result, after, _) = Apply(file, folder, (1, [File("soft-hitnormal3.wav")], 70));

        Assert.Empty(result.Conflicts);
        Assert.Equal(2, result.TimingPointsAdded);
        Assert.Contains("1500,-100,4,2,3,70,0,0", Text(file));
        Assert.Contains("2000,-100,4,2,0,100,0,0", Text(file));
        Assert.Equal(["soft-hitnormal"], Names(after.Moments[0]));
        Assert.Equal(["soft-hitnormal3.wav"], Names(after.Moments[1]));
        Assert.Equal(70, after.Moments[1].Volume);
        Assert.Equal(["soft-hitnormal"], Names(after.Moments[2]));
        Assert.Equal(100, after.Moments[2].Volume);
    }

    [Fact]
    public void A_green_line_keeps_the_slider_velocity_and_kiai_in_force()
    {
        var file = Map(Red + "\n0,-50,4,2,0,100,0,1", "100,100,1000,2,0,L|240:100,1,280");
        var (result, _, _) = Apply(file, new MapFolder([]), (0, [Skin("soft-hitnormal")], 40));

        Assert.Empty(result.Conflicts);
        Assert.Contains("1000,-50,4,2,0,40,0,1", Text(file));
    }

    [Fact]
    public void Slider_banks_go_into_edge_sets()
    {
        var file = Map(Red, "100,100,1000,2,0,L|240:100,2,140");
        var (result, after, _) = Apply(file, new MapFolder([]), (2, [Skin("drum-hitnormal"), Skin("drum-hitfinish")], 100));

        Assert.Empty(result.Conflicts);
        Assert.Equal(["drum-hitfinish", "drum-hitnormal"], Names(after.Moments[2]));
        Assert.Equal(["soft-hitnormal"], Names(after.Moments[0]));
        Assert.Contains("100,100,1000,2,0,L|240:100,2,140,0|0|4,0:0|0:0|3:0", Text(file));
    }

    [Theory]
    [InlineData("no hitnormal")]
    [InlineData("two indices")]
    [InlineData("two addition sets")]
    [InlineData("own file")]
    public void Impossible_moments_are_conflicts_and_keep_their_old_sounds(string kind)
    {
        var folder = new MapFolder(["soft-hitnormal2.wav", "soft-hitclap3.wav", "boom.wav"]);
        Lane[] lanes = kind switch
        {
            "no hitnormal" => [Skin("soft-hitclap")],
            "two indices" => [File("soft-hitnormal2.wav"), File("soft-hitclap3.wav")],
            "two addition sets" => [Skin("soft-hitnormal"), Skin("soft-hitclap"), Skin("drum-hitwhistle")],
            _ => [Skin("soft-hitnormal"), File("boom.wav")],
        };
        var file = Map(Red, "256,192,1000,1,0,0:0:0:0:");
        var (result, after, _) = Apply(file, folder, (0, lanes, 100));

        Assert.Single(result.Conflicts);
        Assert.False(file.IsEdited);
        Assert.Equal(["soft-hitnormal"], Names(after.Moments[0]));
    }

    [Fact]
    public void A_green_line_that_would_change_a_sound_less_than_5ms_earlier_is_a_conflict()
    {
        var folder = new MapFolder(["soft-hitnormal2.wav"]);
        // A circle stacked at 1000 reads the point at 1005, like the slider head at 1000: no line can change one only.
        var file = Map(Red, "256,192,1000,1,0,0:0:0:0:\n100,100,1000,2,0,L|240:100,1,140");
        var (result, after, _) = Apply(file, folder, (1, [File("soft-hitnormal2.wav")], 100));

        Assert.Single(result.Conflicts);
        Assert.Contains("5 ms", result.Conflicts[0].Reason);
        Assert.Equal(["soft-hitnormal"], Names(after.Moments[0]));
    }

    [Fact]
    public void The_filename_field_is_never_written()
    {
        var file = Map(Red, "256,192,1000,1,0,0:0:0:0:boom.wav");
        var (result, _, _) = Apply(file, new MapFolder(["boom.wav"]), (0, [Skin("soft-hitnormal")], 100));

        Assert.Single(result.Conflicts);
        Assert.False(file.IsEdited);
    }

    [Fact]
    public void Every_corpus_map_compiles_unchanged_and_survives_an_edit_on_every_tenth_moment()
    {
        var corpus = Path.Combine(RealMapTests.RepoRoot(), "corpus");
        Assert.True(Directory.Exists(corpus), "corpus/ is missing. Run tools/build-corpus.ps1.");

        var failures = new List<string>();
        foreach (var path in Directory.EnumerateFiles(corpus, "*.osu"))
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            var file = OsuFile.Parse(bytes);
            var folder = new MapFolder([]);
            var map = Decompiler.Decompile(file, folder);
            if (map.Settings.Mode != 0) continue;

            var none = Compiler.Apply(file, folder, new Dictionary<MomentKey, WantedSound>());
            if (file.IsEdited || none.Conflicts.Count > 0)
            {
                failures.Add($"{Path.GetFileName(path)}: changed with nothing wanted ({none.Conflicts.FirstOrDefault()?.Reason})");
                continue;
            }

            // Add a skin clap in the moment's addition set to every tenth moment: never a "please report" conflict.
            var wanted = new Dictionary<MomentKey, WantedSound>();
            for (int i = 0; i < map.Moments.Count; i += 10)
            {
                var m = map.Moments[i];
                var normal = m.Hits[0].Lane;
                if (normal.Source != LaneSource.Skin) continue;
                var set = normal.Name.Split('-')[0];
                wanted[Compiler.KeyOf(file, m)] = new WantedSound([normal, new Lane(LaneSource.Skin, set + "-hitclap")], m.Volume);
            }

            var result = Compiler.Apply(file, folder, wanted);
            if (result.Conflicts.FirstOrDefault(c => c.Reason.Contains("report")) is { } bug)
                failures.Add($"{Path.GetFileName(path)} at {bug.Time}: {bug.Reason}");
        }

        Assert.True(failures.Count == 0, $"{failures.Count} maps failed:\n" + string.Join('\n', failures.Take(20)));
    }

    [Fact]
    public void A_volume_outside_0_to_100_is_a_conflict_not_a_bug_report()
    {
        var file = Map(Red, "256,192,1000,1,0,0:0:0:0:");
        var (result, _, _) = Apply(file, new MapFolder([]), (0, [Skin("soft-hitnormal")], 150));

        Assert.Single(result.Conflicts);
        Assert.DoesNotContain("report", result.Conflicts[0].Reason);
        Assert.False(file.IsEdited);
    }
}

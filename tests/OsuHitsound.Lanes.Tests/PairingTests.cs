using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Tests;

public class PairingTests
{
    private static Lane File(string name) => new(LaneSource.BeatmapFile, name);
    private static PairingPlanner Planner(params string[] names) => new(new MapFolder(names, Encoding.UTF8.GetBytes));

    [Fact]
    public void Index_pairing_copies_audio_without_fusing_distinct_roles()
    {
        var p = Planner("normal-hitnormal.wav", "soft-hitclap2.ogg");
        var wanted = p.For([(File("normal-hitnormal.wav"), 40), (File("soft-hitclap2.ogg"), 40)]);
        Assert.Equal(40, wanted.Volume);
        Assert.Equal(2, wanted.Lanes.Count);
        Assert.Single(wanted.Lanes.Select(l => l.Sample!.Value.Index).Distinct());
        Assert.All(p.Files, f => Assert.NotNull(f.CopyBytes));
        Assert.Equal(Encoding.UTF8.GetBytes("normal-hitnormal.wav"), p.Files.Single(f => f.FileName.StartsWith("normal-hitnormal")).CopyBytes);
        Assert.EndsWith(".ogg", p.Files.Single(f => f.FileName.StartsWith("soft-hitclap")).FileName);
    }

    [Fact]
    public void Cross_bank_finishes_fuse_into_finish_while_kick_stays_separate()
    {
        var p = Planner("drum-hitnormal.wav", "soft-hitfinish.wav", "normal-hitfinish2.wav");
        var wanted = p.For([(File("drum-hitnormal.wav"), 80), (File("soft-hitfinish.wav"), 40), (File("normal-hitfinish2.wav"), 70)]);
        Assert.Equal(2, wanted.Lanes.Count);
        var finish = Assert.Single(p.Files, f => f.FileName.Contains("hitfinish"));
        Assert.Equal(2, finish.Layers.Count);
        Assert.DoesNotContain(finish.Layers, l => l.Lane.Sample!.Value.Sound == "hitnormal");
        var kick = Assert.Single(p.Files, f => f.FileName.Contains("hitnormal"));
        Assert.Single(kick.Layers);
        Assert.Equal(80, kick.Layers[0].Volume);
        Assert.Equal(100, wanted.Volume);
    }

    [Fact]
    public void Cross_bank_distinct_additions_are_copied_to_one_addition_bank()
    {
        var p = Planner("normal-hitnormal.wav", "soft-hitclap2.wav", "drum-hitwhistle3.wav");
        var wanted = p.For([(File("normal-hitnormal.wav"), 100), (File("soft-hitclap2.wav"), 100), (File("drum-hitwhistle3.wav"), 100)]);
        Assert.Equal(3, wanted.Lanes.Count);
        Assert.Single(wanted.Lanes.Where(l => l.Sample!.Value.Sound != "hitnormal").Select(l => l.Sample!.Value.Set).Distinct());
        Assert.All(p.Files, f => Assert.NotNull(f.CopyBytes));
    }

    [Fact]
    public void Unchanged_direct_sounds_keep_low_volume_and_do_not_create_files()
    {
        var p = Planner("soft-hitnormal.wav");
        var wanted = p.For([(File("soft-hitnormal.wav"), 5)]);
        Assert.Equal(5, wanted.Volume);
        Assert.Equal("soft-hitnormal.wav", wanted.Lanes[0].Name);
        Assert.Empty(p.Files);
    }

    [Fact]
    public void Repeat_reuses_but_changed_balance_allocates_a_distinct_pair()
    {
        var p = Planner("normal-hitnormal.wav", "soft-hitclap2.wav");
        var first = p.For([(File("normal-hitnormal.wav"), 100), (File("soft-hitclap2.wav"), 40)]);
        Assert.Equal(first, p.For([(File("normal-hitnormal.wav"), 100), (File("soft-hitclap2.wav"), 40)]));
        var other = p.For([(File("normal-hitnormal.wav"), 100), (File("soft-hitclap2.wav"), 60)]);
        Assert.NotEqual(first.Lanes[0].Name, other.Lanes[0].Name);
    }

    [Fact]
    public void Overall_volume_changes_reuse_audio_without_reusing_the_wrong_object_volume()
    {
        var p = Planner("normal-hitnormal.wav", "soft-hitclap2.wav");
        var loud = p.For([(File("normal-hitnormal.wav"), 100), (File("soft-hitclap2.wav"), 100)]);
        var quiet = p.For([(File("normal-hitnormal.wav"), 40), (File("soft-hitclap2.wav"), 40)]);
        Assert.Equal(loud.Lanes, quiet.Lanes);
        Assert.Equal(100, loud.Volume);
        Assert.Equal(40, quiet.Volume);
        Assert.Equal(2, p.Files.Count);
    }

    [Fact]
    public void Matching_override_identity_preserves_balance_but_ignores_overall_gain()
    {
        var p = Planner("normal-hitnormal.wav", "soft-hitclap2.wav");
        p.For([(File("normal-hitnormal.wav"), 100), (File("soft-hitclap2.wav"), 40)], 1000);
        p.For([(File("normal-hitnormal.wav"), 50), (File("soft-hitclap2.wav"), 20)], 2000);
        p.For([(File("normal-hitnormal.wav"), 100), (File("soft-hitclap2.wav"), 60)], 3000);
        Assert.Equal(p.Combinations[0].Key, p.Combinations[1].Key);
        Assert.NotEqual(p.Combinations[0].Key, p.Combinations[2].Key);
    }

    [Fact]
    public void Missing_required_input_stops_pairing_instead_of_dropping_it()
    {
        var p = Planner("normal-hitnormal.wav");
        Assert.Throws<IOException>(() => p.For([(File("normal-hitnormal.wav"), 100), (File("soft-hitclap2.wav"), 100)]));
        Assert.Empty(p.Files);
    }

    [Fact]
    public void Green_line_preference_uses_object_override_inside_slider_without_changing_passive_audio()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Soft\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n256,192,1000,2,0,B|536:192,1,560,0|0,0:0|0:0,0:0:0:0:\n256,192,1500,1,0,0:0:0:0:\n"));
        var folder = new MapFolder([]);
        var before = Decompiler.Decompile(file, folder);
        var circle = before.Moments.Single(m => m.Object.Kind == HitObjectKind.Circle);
        var result = Compiler.Apply(file, folder, new Dictionary<MomentKey, WantedSound> { [Compiler.KeyOf(file, circle)] = Compiler.AsWanted(circle) with { Volume = 40 } }, ExportStyle.GreenLines);
        Assert.Empty(result.Conflicts);
        Assert.Contains(result.Notes, n => n.Contains("preserve an overlapping slider"));
        Assert.Equal(before.Passive, Decompiler.Decompile(file, folder).Passive);
        Assert.Single(file.TimingPoints);
        Assert.Equal(40, file.HitObjects[1].HitSample!.Value.Volume);
    }

    [Fact]
    public void Timestamp_override_does_not_change_other_occurrences()
    {
        var names = new[] { "normal-hitnormal.wav", "soft-hitfinish.wav", "drum-hitfinish2.wav" };
        var folder = new MapFolder(names, Encoding.UTF8.GetBytes);
        var settings = new ExportMergeSettings { PairSamples = true, Priorities = ExportMergeSettings.Slots.OrderBy(s => s.StartsWith("drum-", StringComparison.Ordinal) ? 0 : 1).ToList() };
        var layers = names.Select(n => (File(n), 100)).ToList();
        var probe = new PairingPlanner(folder, settings: settings);
        probe.For(layers, 1000);
        var combination = probe.Combinations.Single();
        settings.OccurrenceRules[combination.OccurrenceKey] = "soft-hitfinish";
        var planner = new PairingPlanner(folder, settings: settings);
        var one = planner.For(layers, 1000);
        var two = planner.For(layers, 2000);
        Assert.Equal(SampleSet.Soft, one.Lanes.Single(l => l.Sample?.Sound == "hitfinish").Sample!.Value.Set);
        Assert.Equal(SampleSet.Drum, two.Lanes.Single(l => l.Sample?.Sound == "hitfinish").Sample!.Value.Set);
        Assert.All(one.Lanes.Concat(two.Lanes).Where(l => l.Sample?.Sound == "hitnormal"), l => Assert.Equal(SampleSet.Normal, l.Sample!.Value.Set));
    }

    [Theory]
    [InlineData(ExportStyle.GreenLines)]
    [InlineData(ExportStyle.PerObject)]
    public void Export_styles_round_trip_circle_volume_without_touching_later_object(ExportStyle style)
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Soft\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n256,192,1000,1,0,0:0:0:0:\n256,192,2000,1,0,0:0:0:0:\n"));
        var folder = new MapFolder([]);
        var map = Decompiler.Decompile(file, folder);
        var first = map.Moments[0];
        var wanted = new Dictionary<MomentKey, WantedSound> { [new(0, 0)] = Compiler.AsWanted(first) with { Volume = 40 } };
        var result = Compiler.Apply(file, folder, wanted, style);
        Assert.Empty(result.Conflicts);
        Assert.Equal(new[] { 40, 100 }, Decompiler.Decompile(file, folder).Moments.Select(m => m.Volume));
        Assert.Equal(style == ExportStyle.GreenLines ? 0 : 40, file.HitObjects[0].HitSample!.Value.Volume);
        Assert.Equal(style == ExportStyle.GreenLines ? 3 : 1, file.TimingPoints.Count);
    }
}

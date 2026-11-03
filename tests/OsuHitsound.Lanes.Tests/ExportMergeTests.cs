using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Tests;

public class ExportMergeTests
{
    private static Lane Lane(string name) => new(LaneSource.Skin, name);
    private static MergeCombination Pair(int volume = 100) => new([(Lane("drum-hitwhistle"), volume), (Lane("soft-hitclap"), volume)], []);

    private static (OsuFile File, MapFolder Folder, EditSession Session) Map()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet: Soft\n[Metadata]\nTitle:Merge\nArtist:Test\nCreator:Test\nVersion:Source\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n256,192,1000,1,0,0:0:0:0:\n256,192,2000,1,0,0:0:0:0:\n"));
        var folder = new MapFolder([]);
        var session = new EditSession(file, folder, Decompiler.Decompile(file, folder));
        foreach (double time in new[] { 1000d, 2000d })
            session.Add([session.HitAt(Lane("drum-hitwhistle"), time), session.HitAt(Lane("soft-hitclap"), time)]);
        return (file, folder, session);
    }

    [Fact]
    public void Complete_combination_identity_ignores_order_volumes_and_durations_but_preserves_multiplicity()
    {
        var a = Pair(); var b = Pair(30) with { Layers = Pair(30).Layers.Reverse().ToList() };
        Assert.Equal(a.Key, b.Key);
        Assert.NotEqual(a.Key, (a with { Layers = [.. a.Layers, (Lane("normal-hitfinish"), 100)] }).Key);
        Assert.NotEqual(a.Key, (a with { Layers = [.. a.Layers, a.Layers[0]] }).Key);
        Assert.NotEqual(a.Key, (a with { Loops = [(a.Layers[0].Lane, 100, 200)] }).Key);
        Assert.Equal((a with { Loops = [(a.Layers[0].Lane, 100, 200)] }).Key, (a with { Loops = [(a.Layers[0].Lane, 20, 400)] }).Key);
        Assert.NotEqual(a.Key, (a with { Fingerprints = new Dictionary<Lane, string> { [a.Layers[0].Lane] = "changed" } }).Key);
    }

    [Fact]
    public void Same_addition_uses_independent_priority_and_preserves_both_layers()
    {
        var combo = new MergeCombination([(Lane("drum-hitwhistle"), 80), (Lane("soft-hitwhistle"), 40)], []);
        var settings = new ExportMergeSettings();
        settings.Priorities.Remove("soft-hitwhistle"); settings.Priorities.Insert(0, "soft-hitwhistle");
        Assert.Equal("soft-hitwhistle", settings.Destination(combo));
        Assert.Equal("soft-hitclap", settings.Destination(Pair()));
        Assert.Equal("soft-hitwhistle", settings.Destination(combo with { Layers = [.. combo.Layers, (new Lane(LaneSource.BeatmapFile, "custom.wav"), 100)] }));
    }

    [Theory]
    [InlineData("normal-hitnormal")]
    [InlineData("normal-hitwhistle")]
    [InlineData("normal-hitfinish")]
    [InlineData("normal-hitclap")]
    [InlineData("soft-hitnormal")]
    [InlineData("soft-hitwhistle")]
    [InlineData("soft-hitfinish")]
    [InlineData("soft-hitclap")]
    [InlineData("drum-hitnormal")]
    [InlineData("drum-hitwhistle")]
    [InlineData("drum-hitfinish")]
    [InlineData("drum-hitclap")]
    public void Generate_encodes_the_selected_slot_with_all_inputs_and_a_silent_normal_when_needed(string slot)
    {
        var (file, folder, session) = Map();
        var initial = HitsoundDiff.Plan(file, "source.osu", folder, session);
        var combination = Assert.Single(initial.MergeCombinations);
        var settings = new ExportMergeSettings { Rules = new() { [combination.Key] = slot } };
        var plan = HitsoundDiff.Plan(file, "source.osu", folder, session, mergeSettings: settings);
        Assert.Empty(plan.Conflicts);
        var mix = Assert.Single(plan.Mixes, m => m.SilenceFor is null);
        Assert.StartsWith(slot, mix.FileName);
        Assert.Equal(3, mix.Layers.Count); // original normal plus both additions
        var after = Decompiler.Decompile(plan.Diff, folder.With(plan.Mixes.Select(m => m.FileName)));
        Assert.All(after.Moments, m => Assert.Contains(m.Hits, h => h.Lane.Name == mix.FileName));
        if (slot.EndsWith("-hitnormal")) Assert.Single(plan.Mixes);
        else
        {
            var blank = Assert.Single(plan.Mixes, m => m.SilenceFor is not null);
            Assert.All(after.Moments, m => Assert.Contains(m.Hits, h => h.Lane.Name == blank.FileName));
        }
    }

    [Fact]
    public void Different_volumes_reuse_destination_but_have_different_audio_mixes()
    {
        var (file, folder, session) = Map();
        var combination = Assert.Single(HitsoundDiff.Plan(file, "source.osu", folder, session).MergeCombinations);
        var settings = new ExportMergeSettings { Rules = new() { [combination.Key] = "drum-hitclap" } };
        session.SetVolume([session.Moments.Single(m => m.Moment.Time == 2000).Key], 40);
        var plan = HitsoundDiff.Plan(file, "source.osu", folder, session, mergeSettings: settings);
        Assert.Single(plan.MergeCombinations);
        Assert.Empty(plan.Conflicts);
        var mixes = plan.Mixes.Where(m => m.SilenceFor is null).ToList();
        Assert.Equal(2, mixes.Count);
        Assert.All(mixes, m => Assert.StartsWith("drum-hitclap", m.FileName));
        Assert.Contains(mixes, m => m.Layers.All(l => l.Volume == 40));
    }

    [Fact]
    public void Mixed_addition_combinations_follow_priority_without_per_combination_rules()
    {
        var (file, folder, session) = Map();
        var settings = new ExportMergeSettings();
        settings.Priorities.Remove("drum-hitwhistle"); settings.Priorities.Insert(0, "drum-hitwhistle");
        var plan = HitsoundDiff.Plan(file, "source.osu", folder, session, mergeSettings: settings);
        Assert.Empty(plan.Conflicts);
        Assert.Empty(settings.Rules);
        Assert.All(plan.MergeCombinations, c => Assert.Equal("drum-hitwhistle", settings.Destination(c)));
        Assert.StartsWith("drum-hitwhistle", Assert.Single(plan.Mixes, m => m.SilenceFor is null).FileName);
    }

    [Fact]
    public void Slider_sounds_are_preserved_separately_even_when_they_overlap_a_regular_mix()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet: Soft\n[Metadata]\nTitle:Merge\nArtist:Test\nCreator:Test\nVersion:Source\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n256,192,1000,2,0,B|356:192,1,560\n"));
        var folder = new MapFolder([]);
        var session = new EditSession(file, folder, Decompiler.Decompile(file, folder));
        session.Add([session.HitAt(Lane("soft-hitclap"), 1000), session.HitAt(Lane("drum-hitwhistle"), 1000)]);
        var settings = new ExportMergeSettings();
        var plan = HitsoundDiff.Plan(file, "source.osu", folder, session, mergeSettings: settings);
        Assert.Empty(plan.Conflicts);
        var combo = Assert.Single(plan.MergeCombinations);
        Assert.Empty(combo.Loops);
        Assert.All(combo.Layers, l => Assert.Contains(l.Lane.Sample?.Sound, new[] { "hitnormal", "hitwhistle", "hitclap" }));
        Assert.Equal(2, plan.Diff.HitObjects.Count(o => o.Time == 1000));
        var regularMix = Assert.Single(plan.Mixes, m => m.DestinationSlot is not null && m.SilenceFor is null);
        Assert.Empty(regularMix.Loops);
        Assert.DoesNotContain(regularMix.Layers, l => l.Lane.Sample?.Sound is "slidertick" or "sliderslide" or "sliderwhistle");
        Assert.Contains(plan.Mixes, m => m.Loops.Count > 0);
        Assert.Contains(plan.Mixes, m => m.Layers.Any(l => l.Lane.Sample?.Sound == "slidertick"));
    }

    [Fact]
    public void Same_addition_is_generated_in_the_highest_priority_slot_without_an_explicit_rule()
    {
        var (file, folder, session) = Map();
        session.Remove(session.State.Hits.Where(h => h.Lane == Lane("soft-hitclap")).ToList());
        session.Add([session.HitAt(Lane("soft-hitwhistle"), 1000), session.HitAt(Lane("soft-hitwhistle"), 2000)]);
        var settings = new ExportMergeSettings();
        settings.Priorities.Remove("soft-hitwhistle"); settings.Priorities.Insert(0, "soft-hitwhistle");
        var plan = HitsoundDiff.Plan(file, "source.osu", folder, session, mergeSettings: settings);
        Assert.Empty(plan.Conflicts);
        var mix = Assert.Single(plan.Mixes, m => m.SilenceFor is null);
        Assert.StartsWith("soft-hitwhistle", mix.FileName);
        Assert.Contains(mix.Layers, l => l.Lane == Lane("soft-hitwhistle"));
        Assert.Contains(mix.Layers, l => l.Lane == Lane("drum-hitwhistle"));
        Assert.Empty(settings.Rules);
    }

    [Fact]
    public void Regeneration_reuses_addition_and_silent_normal_as_a_pair_and_destination_changes_do_not_reuse_wrong_slot()
    {
        string dir = Directory.CreateTempSubdirectory("merge-generate-").FullName;
        try
        {
            var (file, _, session) = Map();
            string sourcePath = Path.Combine(dir, "source.osu");
            File.WriteAllBytes(sourcePath, file.ToBytes());
            var skin = session.State.Hits.Select(h => h.Lane).Distinct().ToDictionary(l => l.Name, _ => new byte[] { 1, 2, 3 });
            var folder = MapFolder.FromDirectory(dir);
            var initial = HitsoundDiff.Plan(file, sourcePath, folder, session, skin: skin);
            var combo = Assert.Single(initial.MergeCombinations);
            var settings = new ExportMergeSettings { Rules = new() { [combo.Key] = "drum-hitclap" } };
            var plan = HitsoundDiff.Plan(file, sourcePath, folder, session, skin: skin, mergeSettings: settings);
            var written = GenerateWriter.Write(plan, _ => [1, 2, 3], Path.Combine(dir, "backups"));
            folder = MapFolder.FromDirectory(dir);
            var again = HitsoundDiff.Plan(file, sourcePath, folder, session, written.Manifest, skin, settings);
            Assert.Empty(again.Mixes);
            Assert.Equal(2, again.Reused.Count);
            Assert.Empty(again.Removes);

            var changedSkin = skin.ToDictionary(p => p.Key, _ => new byte[] { 4, 5, 6 });
            var changedInputs = HitsoundDiff.Plan(file, sourcePath, folder, session, written.Manifest, changedSkin);
            var changedCombo = Assert.Single(changedInputs.MergeCombinations);
            var changedSettings = new ExportMergeSettings { Rules = new() { [changedCombo.Key] = "drum-hitclap" } };
            var changedAudio = HitsoundDiff.Plan(file, sourcePath, folder, session, written.Manifest, changedSkin, changedSettings);
            Assert.Empty(changedAudio.Reused);
            Assert.Equal(2, changedAudio.Mixes.Count);

            settings.Rules[combo.Key] = "normal-hitfinish";
            var changed = HitsoundDiff.Plan(file, sourcePath, folder, session, written.Manifest, skin, settings);
            Assert.Empty(changed.Reused);
            Assert.Equal(2, changed.Mixes.Count);
            Assert.Contains(changed.Mixes, m => m.FileName.StartsWith("normal-hitfinish"));
            Assert.Empty(changed.Conflicts);

            var blank = plan.Mixes.Single(m => m.SilenceFor is not null);
            File.WriteAllBytes(Path.Combine(dir, blank.FileName), [9, 8, 7]);
            settings.Rules[combo.Key] = "drum-hitclap";
            var missingPair = HitsoundDiff.Plan(file, sourcePath, MapFolder.FromDirectory(dir), session, written.Manifest, skin, settings);
            Assert.Empty(missingPair.Reused);
            Assert.Equal(2, missingPair.Mixes.Count);
            Assert.DoesNotContain(blank.FileName, missingPair.Removes); // modified user sample is protected
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Presets_round_trip_explicitly_and_reject_invalid_priorities_and_destinations()
    {
        string dir = Directory.CreateTempSubdirectory("merge-preset-").FullName;
        try
        {
            string path = Path.Combine(dir, "preset.json");
            var settings = new ExportMergeSettings { Rules = new() { [Pair().Key] = "soft-hitwhistle" } };
            settings.Save(path);
            var loaded = ExportMergeSettings.Load(path);
            Assert.Equal("soft-hitwhistle", loaded.Destination(Pair(30)));
            loaded.Priorities[0] = loaded.Priorities[1]; Assert.Throws<FormatException>(loaded.Validate);
            settings.Rules[Pair().Key] = "../bad"; Assert.Throws<FormatException>(() => settings.Save(path));
            File.WriteAllText(path, "{\"Priorities\":null}"); Assert.Throws<FormatException>(() => ExportMergeSettings.Load(path));
        }
        finally { Directory.Delete(dir, true); }
    }
}

using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Tests;

public sealed class NativeCombinationTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("native-combinations-").FullName;
    private const string Header = "osu file format v14\n[General]\nSampleSet:Soft\n[Metadata]\nArtist:Test\nTitle:Native\nCreator:Test\nVersion:Hard\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n";
    public void Dispose() => Directory.Delete(root, true);

    private static ExportMergeSettings Overrides(IReadOnlyList<(Lane Lane, int Volume)> layers, MapFolder folder)
    {
        var combination = new MergeCombination(layers, [])
        {
            Time = 1000, MatchBalance = true,
            Fingerprints = layers.ToDictionary(l => l.Lane, l => GenerateManifest.Hash(folder.ReadBytes(l.Lane.Name))),
        };
        var settings = new ExportMergeSettings { PairSamples = true };
        settings.Rules[combination.Key] = "drum-hitfinish";
        settings.OccurrenceRules[combination.OccurrenceKey] = "normal-hitfinish";
        return settings;
    }

    [Fact]
    public void Every_native_bank_and_addition_flag_combination_wins_over_saved_merge_overrides()
    {
        var banks = new[] { SampleSet.Normal, SampleSet.Soft, SampleSet.Drum };
        string[] roles = ["hitnormal", "hitwhistle", "hitfinish", "hitclap"];
        var names = banks.SelectMany(bank => roles.Select(role => HitsoundRules.SampleName(bank, role) + "2.ogg")).ToArray();
        var folder = new MapFolder(names, Encoding.UTF8.GetBytes);
        foreach (var normal in banks)
            foreach (var addition in banks)
                for (int flags = 0; flags < 8; flags++)
                {
                    var layers = new List<(Lane Lane, int Volume)> { (new(LaneSource.BeatmapFile, HitsoundRules.SampleName(normal, "hitnormal") + "2.ogg"), 40) };
                    for (int i = 0; i < 3; i++)
                        if ((flags & (1 << i)) != 0) layers.Add((new(LaneSource.BeatmapFile, HitsoundRules.SampleName(addition, roles[i + 1]) + "2.ogg"), 40));
                    var planner = new PairingPlanner(folder, settings: Overrides(layers, folder));
                    var wanted = planner.For(layers, 1000);
                    Assert.Equal(layers.Select(l => l.Lane), wanted.Lanes);
                    Assert.Equal(40, wanted.Volume);
                    Assert.Empty(planner.Files);
                    Assert.Empty(planner.Combinations);
                }
    }

    [Theory]
    [InlineData(1, 2, ExportStyle.GreenLines)]
    [InlineData(1, 2, ExportStyle.PerObject)]
    [InlineData(2, 3, ExportStyle.GreenLines)]
    [InlineData(2, 3, ExportStyle.PerObject)]
    [InlineData(3, 1, ExportStyle.GreenLines)]
    [InlineData(3, 1, ExportStyle.PerObject)]
    public void Save_generate_and_copy_keep_native_layers_without_rendering_or_creating_sample_files(int normal, int addition, ExportStyle style)
    {
        string[] names = [HitsoundRules.SampleName((SampleSet)normal, "hitnormal") + "2.wav",
            .. new[] { "hitwhistle", "hitfinish", "hitclap" }.Select(role => HitsoundRules.SampleName((SampleSet)addition, role) + "2.ogg")];
        foreach (string name in names) File.WriteAllBytes(Path.Combine(root, name), Encoding.UTF8.GetBytes(name));
        string path = Path.Combine(root, "source.osu");
        File.WriteAllText(path, Header + $"12,34,1000,1,14,{normal}:{addition}:2:65:\n");
        var document = MapDocument.Load(path);
        document.Session.SetVolume([document.Session.Moments.Single().Key], 60);
        var layers = document.Session.State.Hits.Select(h => (h.Lane, Volume: document.Session.VolumeOf(h))).ToList();
        var settings = Overrides(layers, document.Folder); settings.Style = style;
        string manifests = Path.Combine(root, "manifests"), backups = Path.Combine(root, "backups");
        var (save, _) = document.PlanHsSave(manifests, settings: settings);
        var (generate, _) = document.PlanGenerate(manifests, mergeSettings: settings);
        foreach (var plan in new[] { save.Storage, generate })
        {
            Assert.Empty(plan.Mixes); Assert.Empty(plan.Conflicts);
            var moment = Assert.Single(Decompiler.Decompile(plan.Diff, document.Folder).Moments);
            Assert.Equal(60, moment.Volume);
            Assert.Equal(names.Order(), moment.Hits.Select(h => h.Lane.Name).Order());
            GenerateWriter.Write(plan, _ => throw new Exception("Native combinations must not be rendered"), backups,
                plan == save.Storage ? document.Opened : null);
        }
        var target = OsuFile.Parse(Encoding.UTF8.GetBytes(Header + "12,34,1000,1,0,0:0:0:100:\n"));
        var copy = HitsoundCopier.Plan(save.Storage.Diff, document.Folder, target, document.Folder, new(), mergeSettings: settings);
        Assert.Empty(copy.Mixes);
        Assert.Empty(Compiler.Apply(target, document.Folder, copy.Wanted).Conflicts);
        var copied = Assert.Single(Decompiler.Decompile(target, document.Folder).Moments);
        Assert.Equal(60, copied.Volume);
        Assert.Equal(names.Order(), copied.Hits.Select(h => h.Lane.Name).Order());
        Assert.Equal(names.Order(), Directory.GetFiles(root).Select(Path.GetFileName).Where(n => n!.EndsWith(".wav") || n.EndsWith(".ogg")).Order());
        foreach (string name in names) Assert.Equal(Encoding.UTF8.GetBytes(name), File.ReadAllBytes(Path.Combine(root, name)));
    }
}

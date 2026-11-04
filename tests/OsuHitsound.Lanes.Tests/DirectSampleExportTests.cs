using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Tests;

public sealed class DirectSampleExportTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("direct-sample-export-").FullName;
    private const string Header = "osu file format v14\n[General]\nSampleSet:Soft\n[Metadata]\nTitle:Direct sample\nArtist:Test\nCreator:Test\nVersion:HS\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n";
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData("snare.ogg", false, false)]
    [InlineData("snare.wav", false, false)]
    [InlineData("snare.mp3", false, false)]
    [InlineData("soft-hitclap2.ogg", false, false)]
    [InlineData("soft-hitnormal2.ogg", true, false)]
    [InlineData("snare.ogg", false, true)]
    public void Save_and_generate_keep_a_single_existing_sample_without_new_numbered_files(string name, bool shadowed, bool spinner)
    {
        byte[] audio = [1, 2, 3, 4]; // Planning must not need to decode or render this direct reference.
        File.WriteAllBytes(Path.Combine(root, name), audio);
        if (shadowed) File.WriteAllBytes(Path.Combine(root, Path.ChangeExtension(name, ".wav")), [9, 8, 7]);
        var originalSamples = Directory.GetFiles(root).ToDictionary(p => Path.GetFileName(p)!, File.ReadAllBytes);
        string path = Path.Combine(root, "source.osu");
        string shape = spinner ? "256,192,500,8,0,1000," : "256,192,1000,1,0,";
        File.WriteAllText(path, Header + $"{shape}0:0:0:40:{name}\n");
        var document = MapDocument.Load(path);
        document.Session.SetVolume([document.Session.State.Hits.Single().Anchor!.Value], 60);
        var settings = new ExportMergeSettings { PairSamples = true };
        var (save, _) = document.PlanHsSave(Path.Combine(root, "manifests"), settings: settings);
        Assert.Empty(save.Storage.Conflicts);
        Assert.Empty(save.Storage.Mixes);
        var (generate, _) = document.PlanGenerate(Path.Combine(root, "manifests"), mergeSettings: settings);
        Assert.Empty(generate.Conflicts);
        Assert.Empty(generate.Mixes);
        foreach (var plan in new[] { save.Storage, generate })
        {
            var compiled = Decompiler.Decompile(plan.Diff, document.Folder);
            var moment = Assert.Single(compiled.Moments);
            Assert.Equal(1000, moment.Time);
            Assert.Equal(60, moment.Volume);
            Assert.Equal(name, Assert.Single(moment.Hits).Lane.Name);
            GenerateWriter.Write(plan, _ => throw new Exception("Direct samples must not be rendered"), Path.Combine(root, "backups"),
                plan == save.Storage ? document.Opened : null);
        }
        var outputSamples = Directory.GetFiles(root).Where(p => HitsoundRules.SampleExtensions.Contains(Path.GetExtension(p))).ToList();
        Assert.Equal(originalSamples.Count, outputSamples.Count);
        foreach (var sample in outputSamples) Assert.Equal(originalSamples[Path.GetFileName(sample)], File.ReadAllBytes(sample));
    }

    [Fact]
    public void Assigning_one_ogg_to_a_circle_removes_old_additions_without_creating_samples_or_green_lines()
    {
        File.WriteAllBytes(Path.Combine(root, "snare.ogg"), [1, 2, 3]);
        string path = Path.Combine(root, "source.osu");
        File.WriteAllText(path, Header + "12,34,1000,1,8,0:0:0:100:\n");
        var document = MapDocument.Load(path);
        document.Session.Remove(document.Session.State.Hits.ToList());
        document.Session.Add([document.Session.HitAt(new(LaneSource.BeatmapFile, "snare.ogg"), 1000)]);
        var (plan, _) = document.PlanHsSave(Path.Combine(root, "manifests"), settings: new() { PairSamples = true });
        Assert.Empty(plan.Storage.Conflicts);
        Assert.Empty(plan.Storage.Mixes);
        var obj = Assert.Single(plan.Storage.Diff.HitObjects);
        Assert.Equal("snare.ogg", obj.HitSample!.Value.Filename);
        Assert.Equal(HitSound.None, obj.HitSound);
        Assert.Equal(12, obj.X);
        Assert.Equal(34, obj.Y);
        Assert.Equal(document.File.TimingPoints.Select(p => p.Line.Text), plan.Storage.Diff.TimingPoints.Select(p => p.Line.Text));
    }

    [Fact]
    public void An_ogg_overlap_still_gets_the_pairing_it_needs()
    {
        File.WriteAllBytes(Path.Combine(root, "snare.ogg"), [1, 2, 3]);
        string path = Path.Combine(root, "source.osu");
        File.WriteAllText(path, Header + "256,192,1000,1,0,0:0:0:40:snare.ogg\n");
        var document = MapDocument.Load(path);
        document.Session.Add([document.Session.HitAt(new(LaneSource.Skin, "soft-hitclap"), 1000)]);
        var (plan, _) = document.PlanGenerate(Path.Combine(root, "manifests"),
            new Dictionary<string, byte[]> { ["soft-hitclap"] = BlankSample.Bytes }, new() { PairSamples = true });
        Assert.Empty(plan.Conflicts);
        Assert.NotEmpty(plan.Mixes);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(root, "snare.ogg")));
    }
}

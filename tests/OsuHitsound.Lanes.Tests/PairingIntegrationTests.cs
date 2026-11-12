using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Tests;

public sealed class PairingIntegrationTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("pairing-export-").FullName;
    private const string Header = "osu file format v14\n[General]\nSampleSet:Soft\n[Metadata]\nArtist:Test\nTitle:Pair\nCreator:Test\nVersion:Hard\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n";
    private string Manifests => Path.Combine(root, "manifests");
    private string Backups => Path.Combine(root, "backups");
    private static ExportMergeSettings Settings(ExportStyle style = ExportStyle.GreenLines) => new() { PairSamples = true, Style = style };
    public void Dispose() => Directory.Delete(root, true);
    private MapDocument Fixture()
    {
        foreach (var name in new[] { "drum-hitnormal.wav", "soft-hitfinish.wav", "normal-hitfinish2.ogg" })
            File.WriteAllBytes(Path.Combine(root, name), Encoding.UTF8.GetBytes(name));
        var path = Path.Combine(root, "source.osu");
        File.WriteAllText(path, Header + "10,20,1000,1,0,3:3:1:65:\n");
        var doc = MapDocument.Load(path);
        doc.Session.Add([doc.Session.HitAt(new(LaneSource.BeatmapFile, "soft-hitfinish.wav"), 1000),
            doc.Session.HitAt(new(LaneSource.BeatmapFile, "normal-hitfinish2.ogg"), 1000)]);
        return doc;
    }
    private static byte[] Render(MixFile mix) => mix.SilenceFor is not null ? BlankSample.Bytes : Encoding.UTF8.GetBytes(mix.Signature);

    [Theory]
    [InlineData(ExportStyle.GreenLines)]
    [InlineData(ExportStyle.PerObject)]
    public void Playable_save_fuses_finishes_only_and_restores_original_layers(ExportStyle style)
    {
        var doc = Fixture();
        var originalSamples = Directory.GetFiles(root).Where(p => !p.EndsWith(".osu")).ToDictionary(p => p, File.ReadAllBytes);
        var (plan, _) = doc.PlanHsSave(Manifests, settings: Settings(style));
        Assert.Empty(plan.Storage.Conflicts);
        Assert.Equal(2, plan.Storage.Mixes.Count);
        Assert.Equal(2, Assert.Single(plan.Storage.Mixes, m => m.FileName.Contains("hitfinish")).Layers.Count);
        Assert.Single(Assert.Single(plan.Storage.Mixes, m => m.FileName.Contains("hitnormal")).Layers);
        doc.WriteHsSave(plan, Render, Backups, Manifests);
        var reopened = MapDocument.Load(doc.Path);
        Assert.Equal(10, reopened.File.HitObjects[0].X);
        Assert.Equal(20, reopened.File.HitObjects[0].Y);
        Assert.Equal(2, reopened.Map.Moments.Single().Hits.Count);
        reopened.RestoreHsProject(doc.HsProject);
        Assert.Equal(3, reopened.Session.State.Hits.Count);
        Assert.Contains(reopened.Session.State.Hits, h => h.Lane.Name == "normal-hitfinish2.ogg");
        foreach (var (path, bytes) in originalSamples) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Input_changes_after_planning_stop_before_writing_any_outputs()
    {
        var doc = Fixture();
        var (plan, _) = doc.PlanHsSave(Manifests, settings: Settings());
        File.WriteAllBytes(Path.Combine(root, "soft-hitfinish.wav"), [9, 9]);
        Assert.Throws<IOException>(() => doc.WriteHsSave(plan, Render, Backups, Manifests));
        Assert.Equal(doc.Opened, File.ReadAllBytes(doc.Path));
        Assert.All(plan.Storage.Mixes, m => Assert.False(File.Exists(Path.Combine(root, m.FileName))));
    }

    [Fact]
    public void Unchanged_custom_filename_objects_keep_exact_bytes_without_reading_or_replacing_their_audio()
    {
        string path = Path.Combine(root, "custom.osu");
        File.WriteAllText(path, Header + "10,20,1000,1,0,0:0:0:40:custom.wav\n");
        var doc = MapDocument.Load(path);
        var (plan, _) = doc.PlanHsSave(Manifests, settings: Settings());
        Assert.Empty(plan.Storage.Conflicts);
        Assert.Empty(plan.Storage.Mixes);
        Assert.Equal(doc.Opened, plan.Storage.Diff.ToBytes());
        Assert.False(doc.Compile(doc.Session.State, Settings()).Result.Conflicts.Any());
        doc.WriteHsSave(plan, _ => throw new Exception("No new audio needed"), Backups, Manifests);
        Assert.Equal(doc.Opened, File.ReadAllBytes(path));
    }

    [Fact]
    public void Explicit_keep_original_override_reports_conflict_instead_of_silently_fusing_or_dropping_layers()
    {
        var doc = Fixture();
        var settings = Settings();
        var (initial, _) = doc.PlanHsSave(Manifests, settings: settings);
        var combination = initial.Storage.MergeCombinations.Single();
        settings.OccurrenceKeepOriginal[combination.OccurrenceKey] = true;
        var (blocked, _) = doc.PlanHsSave(Manifests, settings: settings);
        Assert.Contains(blocked.Storage.Conflicts, c => c.Reason.Contains("Original files were requested"));
        Assert.Empty(blocked.Storage.Mixes);
        Assert.Throws<IOException>(() => doc.WriteHsSave(blocked, Render, Backups, Manifests));
        Assert.Equal(doc.Opened, File.ReadAllBytes(doc.Path));
        settings.OccurrenceKeepOriginal[combination.OccurrenceKey] = false;
        var (restored, _) = doc.PlanHsSave(Manifests, settings: settings);
        Assert.Empty(restored.Storage.Conflicts);
        Assert.Equal(2, restored.Storage.Mixes.Count);
    }

    [Fact]
    public void Unchanged_pair_reuses_all_files_and_changed_input_invalidates_reuse()
    {
        var doc = Fixture();
        var (plan, _) = doc.PlanHsSave(Manifests, settings: Settings());
        doc.WriteHsSave(plan, Render, Backups, Manifests);
        var reopened = MapDocument.Load(doc.Path);
        reopened.RestoreHsProject(doc.HsProject);
        var (again, _) = reopened.PlanHsSave(Manifests, settings: Settings());
        Assert.Empty(again.Storage.Mixes);
        Assert.Equal(2, again.Storage.Reused.Count);
        File.WriteAllBytes(Path.Combine(root, "soft-hitfinish.wav"), [4, 5, 6]);
        var (changed, _) = reopened.PlanHsSave(Manifests, settings: Settings());
        Assert.Equal(2, changed.Storage.Mixes.Count);
        Assert.Empty(changed.Storage.Reused);
        Assert.DoesNotContain(changed.Storage.Mixes, m => plan.Storage.Mixes.Any(old => old.FileName == m.FileName));
    }

    [Fact]
    public void Generate_uses_same_finish_only_rule_and_refuses_changed_output()
    {
        var doc = Fixture();
        var (plan, _) = doc.PlanGenerate(Manifests, mergeSettings: Settings());
        Assert.Empty(plan.Conflicts);
        Assert.Equal(2, plan.Mixes.Count);
        Assert.All(plan.Mixes.Where(m => m.FileName.Contains("hitnormal")), m => Assert.DoesNotContain(m.Layers, l => l.Lane.Sample?.Sound == "hitfinish"));
        File.WriteAllText(plan.OsuPath, "external change");
        Assert.Throws<IOException>(() => GenerateWriter.Write(plan, Render, Backups));
        Assert.Equal("external change", File.ReadAllText(plan.OsuPath));
        Assert.All(plan.Mixes, m => Assert.False(File.Exists(Path.Combine(root, m.FileName))));
    }

    [Fact]
    public void Generate_rechecks_reused_files_after_planning()
    {
        var doc = Fixture();
        var (first, _) = doc.PlanGenerate(Manifests, mergeSettings: Settings());
        var written = GenerateWriter.Write(first, Render, Backups);
        written.Manifest.Save(Manifests, first.OsuPath);
        var (again, _) = doc.PlanGenerate(Manifests, mergeSettings: Settings());
        Assert.Empty(again.Mixes);
        Assert.Equal(2, again.Reused.Count);
        var before = File.ReadAllBytes(first.OsuPath);
        File.WriteAllBytes(Path.Combine(root, again.Reused[0].FileName), [9, 9]);
        Assert.Throws<IOException>(() => GenerateWriter.Write(again, Render, Backups));
        Assert.Equal(before, File.ReadAllBytes(first.OsuPath));
    }

    [Fact]
    public void Late_sample_collision_is_not_overwritten()
    {
        var doc = Fixture();
        var (plan, _) = doc.PlanGenerate(Manifests, mergeSettings: Settings());
        var taken = Path.Combine(root, plan.Mixes[0].FileName);
        File.WriteAllBytes(taken, [7, 8, 9]);
        Assert.Throws<IOException>(() => GenerateWriter.Write(plan, Render, Backups));
        Assert.Equal(new byte[] { 7, 8, 9 }, File.ReadAllBytes(taken));
        Assert.False(File.Exists(plan.OsuPath));
    }

    [Fact]
    public void Copy_preserves_distinct_addition_types_and_failure_leaves_no_partial_files()
    {
        foreach (var name in new[] { "normal-hitnormal.wav", "soft-hitclap2.wav", "drum-hitwhistle3.wav" })
            File.WriteAllBytes(Path.Combine(root, name), Encoding.UTF8.GetBytes(name));
        var source = OsuFile.Parse(Encoding.UTF8.GetBytes(Header + "256,192,1000,1,8,1:2:2:60:\n256,192,1000,1,2,1:3:3:60:\n"));
        string target = Path.Combine(root, "target.osu");
        File.WriteAllText(target, Header + "30,40,1000,1,0,1:1:1:80:\n");
        var skin = new Dictionary<string, byte[]> { ["normal-hitnormal"] = [1, 2, 3] };
        var before = File.ReadAllBytes(target);
        var failed = HitsoundCopier.CopyTo(source, MapFolder.FromDirectory(root), [target], new(), Backups,
            _ => throw new IOException("decode failure"), Settings(), skin);
        Assert.NotNull(failed.Single().Error);
        Assert.Equal(before, File.ReadAllBytes(target));
        Assert.Equal(3, Directory.GetFiles(root, "*.wav").Length);
        var copied = HitsoundCopier.CopyTo(source, MapFolder.FromDirectory(root), [target], new(), Backups, Render, Settings(), skin);
        Assert.Null(copied.Single().Error);
        var output = MapDocument.Load(target).Map.Moments.Single();
        Assert.Equal(3, output.Hits.Count);
        Assert.Contains(output.Hits, h => h.Lane.Sample?.Sound == "hitclap");
        Assert.Contains(output.Hits, h => h.Lane.Sample?.Sound == "hitwhistle");
        Assert.Equal(30, output.Object.X);
    }

    // ---- Unused mixes are removed on Save, paired too (issue #38) ----

    // Saves the fixture (two finishes at 1000 ms: a paired mix kit), then reopens it with the second finish removed, so
    // the next save no longer needs the kit. Returns the reopened map and the kit's files.
    private (MapDocument Reopened, IReadOnlyList<string> Kit) SaveKitThenDropTheSecondFinish()
    {
        var doc = Fixture();
        var (plan, _) = doc.PlanHsSave(Manifests, settings: Settings());
        doc.WriteHsSave(plan, Render, Backups, Manifests);
        var kit = plan.Storage.Mixes.Select(m => m.FileName).ToList();
        Assert.All(kit, f => Assert.True(File.Exists(Path.Combine(root, f))));

        var reopened = MapDocument.Load(doc.Path);
        reopened.RestoreHsProject(doc.HsProject);
        reopened.Session.Remove(reopened.Session.State.Hits.Where(h => h.Lane.Name == "normal-hitfinish2.ogg").ToList());
        return (reopened, kit);
    }

    [Fact]
    public void A_paired_save_removes_its_old_mixes_once_nothing_uses_them()
    {
        var (reopened, kit) = SaveKitThenDropTheSecondFinish();

        var (plan, _) = reopened.PlanHsSave(Manifests, settings: Settings());
        Assert.Equal(kit.Order(), plan.Storage.Removes.Order());
        reopened.WriteHsSave(plan, Render, Backups, Manifests);

        Assert.All(kit, f => Assert.False(File.Exists(Path.Combine(root, f))));
    }

    [Fact]
    public void A_mix_another_difficulty_uses_is_kept()
    {
        var (reopened, kit) = SaveKitThenDropTheSecondFinish();
        // Another difficulty plays the kit's index (as Copy hitsounds can make it).
        int index = HitsoundDiff.IndexOf(kit[0])!.Value;
        File.WriteAllText(Path.Combine(root, "other.osu"), Header + $"10,20,1000,1,0,0:0:{index}:0:\n");

        var (plan, _) = reopened.PlanHsSave(Manifests, settings: Settings());

        Assert.Empty(plan.Storage.Removes);
    }

    [Fact]
    public void A_mix_changed_since_dawsu_wrote_it_is_kept()
    {
        var (reopened, kit) = SaveKitThenDropTheSecondFinish();
        File.WriteAllBytes(Path.Combine(root, kit[0]), [7, 7, 7]); // the mapper replaced it

        var (plan, _) = reopened.PlanHsSave(Manifests, settings: Settings());

        Assert.DoesNotContain(kit[0], plan.Storage.Removes);
    }
}

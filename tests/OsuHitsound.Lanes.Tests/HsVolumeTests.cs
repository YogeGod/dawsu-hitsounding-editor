using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Tests;

public class HsVolumeTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hs-volumes-").FullName;
    public void Dispose() => Directory.Delete(root, true);
    private MapDocument Open(string objects = "")
    {
        string path = Path.Combine(root, "source.osu");
        File.WriteAllBytes(path, HsModeTests.Bytes(objects, "HS"));
        var doc = MapDocument.Load(path);
        doc.Session.SetHsMode(true);
        return doc;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Move_and_copy_to_occupied_times_preserve_each_layer_volume_and_history(bool copy, bool sameLane)
    {
        var s = Open().Session;
        var a = s.HitAt(HsModeTests.Normal, 1000);
        var b = s.HitAt(sameLane ? HsModeTests.Normal : HsModeTests.Clap, 2000);
        s.Add([a, b]); s.SetVolume([a.Anchor!.Value], 30); s.SetVolume([b.Anchor!.Value], 80);
        var placed = Assert.Single(copy ? s.Copy([a], 1000) : s.Move([a], 1000, h => h.Lane));
        Assert.Equal(30, s.VolumeOf(placed)); Assert.Equal(80, s.VolumeOf(b));
        Assert.Equal(2, s.State.Hits.Count(h => h.Time == 2000));
        Assert.Equal(copy ? 2 : 1, s.Moments.Count);
        s.Undo(); Assert.Equal(30, s.VolumeOf(a)); Assert.Equal(80, s.VolumeOf(b));
        s.Redo(); Assert.Equal(30, s.VolumeOf(placed)); Assert.Equal(80, s.VolumeOf(b));
        s.SetVolume([b.Anchor.Value], 50);
        Assert.All(s.State.Hits.Where(h => h.Time == 2000), h => Assert.Equal(50, s.VolumeOf(h)));
        Assert.Contains(placed, s.State.Hits); // Volume changes keep note identity and selection stable.
        s.Undo(); Assert.Equal(30, s.VolumeOf(placed)); Assert.Equal(80, s.VolumeOf(b));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Save_reload_and_generate_preserve_mixed_volumes_even_for_the_same_sample(bool sameLane)
    {
        var doc = Open(); var s = doc.Session;
        var a = s.HitAt(HsModeTests.Normal, 1000);
        var b = s.HitAt(sameLane ? HsModeTests.Normal : HsModeTests.Clap, 2000);
        s.Add([a, b]); s.SetVolume([a.Anchor!.Value], 30); s.SetVolume([b.Anchor!.Value], 80);
        s.Move([a], 1000, h => h.Lane);
        Assert.NotEmpty(doc.Compile(s.State).Result.Conflicts);
        var skin = s.State.Hits.Select(h => h.Lane).Distinct().ToDictionary(l => l.Name, _ => new byte[] { 1, 2 });
        string manifests = Path.Combine(root, "manifests");
        var (plan, _) = doc.PlanHsSave(manifests, skin, new ExportMergeSettings());
        Assert.Empty(plan.Storage.Conflicts);
        Assert.Equal(new[] { 30, 80 }, plan.Storage.Mixes.SelectMany(m => m.Layers).Select(l => l.Volume).Order());
        doc.WriteHsSave(plan, _ => [1, 2], Path.Combine(root, "backup"), manifests);
        var reopened = MapDocument.Load(doc.Path); reopened.RestoreHsProject(doc.HsProject);
        Assert.Equal(new[] { 30, 80 }, reopened.Session.State.Hits.Select(reopened.Session.VolumeOf).Order());
        var (again, _) = reopened.PlanHsSave(manifests, skin, new ExportMergeSettings());
        Assert.Empty(again.Storage.Mixes); Assert.NotEmpty(again.Storage.Reused);
        var (generated, _) = reopened.PlanGenerate(manifests, skin, new ExportMergeSettings());
        Assert.Equal(new[] { 30, 80 }, generated.Mixes.SelectMany(m => m.Layers).Select(l => l.Volume).Order());
    }

    [Fact]
    public void Repeated_layers_on_a_protected_object_still_use_HS_save_when_mode_is_off()
    {
        var doc = Open("256,192,2000,1,0,0:0:0:80:\n"); var s = doc.Session;
        var source = s.HitAt(HsModeTests.Normal, 1000); s.Add([source]);
        s.SetVolume([source.Anchor!.Value], 30);
        s.Move([source], 1000, h => h.Lane);
        var target = s.State.Hits.First(h => h.Time == 2000).Anchor!.Value;
        s.SetVolume([target], 50); s.SetHsMode(false);
        Assert.Empty(s.State.HsCircles); Assert.Empty(s.State.LayerVolumes);
        Assert.True(s.HasHsObjects);
        Assert.Throws<InvalidOperationException>(() => doc.Save(Path.Combine(root, "backup")));
        var (plan, _) = doc.PlanHsSave(Path.Combine(root, "manifests"),
            new Dictionary<string, byte[]> { [HsModeTests.Normal.Name] = [1] });
        Assert.Equal(new[] { 50, 50 }, Assert.Single(plan.Storage.Mixes).Layers.Select(l => l.Volume));
    }

    [Fact]
    public void Replacing_a_merged_lane_preserves_volume_and_a_stationary_move_is_not_an_edit()
    {
        var s = Open().Session;
        var a = s.HitAt(HsModeTests.Normal, 1000); var b = s.HitAt(HsModeTests.Clap, 2000);
        s.Add([a, b]); s.SetVolume([a.Anchor!.Value], 30); s.SetVolume([b.Anchor!.Value], 80);
        var moved = Assert.Single(s.Move([a], 1000, h => h.Lane));
        int position = s.Position;
        s.Move([moved], 0, h => h.Lane); Assert.Equal(position, s.Position);
        s.RetargetLane(HsModeTests.Normal, HsModeTests.Clap);
        Assert.Equal(new[] { 30, 80 }, s.State.Hits.Select(s.VolumeOf).Order());
        Assert.Equal(2, s.State.Hits.Count);
        s.Undo(); Assert.Equal(30, s.VolumeOf(moved)); Assert.Equal(80, s.VolumeOf(b));
    }

    [Fact]
    public void An_encodable_pair_with_different_volumes_still_requires_a_mix()
    {
        var doc = Open(); var s = doc.Session;
        var a = s.HitAt(HsModeTests.Normal, 1000); var b = s.HitAt(HsModeTests.Clap, 2000);
        s.Add([a, b]); s.SetVolume([a.Anchor!.Value], 30); s.SetVolume([b.Anchor!.Value], 80);
        s.Move([a], 1000, h => h.Lane);
        var skin = s.State.Hits.ToDictionary(h => h.Lane.Name, _ => new byte[] { 1 });
        var (plan, _) = doc.PlanHsSave(Path.Combine(root, "manifests"), skin);
        Assert.Single(plan.Storage.Mixes);
        Assert.Single(plan.Storage.MergeCombinations);
        Assert.Equal(new[] { 30, 80 }, plan.Storage.Mixes.Single().Layers.Select(l => l.Volume).Order());
    }

    [Fact]
    public void Slider_side_effects_are_carried_into_the_HS_save_plan()
    {
        var doc = Open("256,192,1000,2,0,B|536:192,1,280,0|0,0:0|0:0,0:0:0:0:\n");
        var head = doc.Session.State.Hits.First(h => h.Time == 1000);
        doc.Session.SetVolume([head.Anchor!.Value], 35);
        var (plan, _) = doc.PlanHsSave(Path.Combine(root, "manifests"));
        Assert.Empty(plan.Storage.Conflicts);
        Assert.Contains(plan.Notes, n => n.Contains("2 slider body or tick sounds changed"));
        Assert.All(Decompiler.Decompile(plan.Storage.Diff, new MapFolder([])).Passive, p => Assert.Equal(35, p.Volume));
    }

    [Fact]
    public void Invalid_layer_volume_sidecars_do_not_adopt_protected_objects()
    {
        var doc = Open("256,192,1000,1,0,0:0:0:0:\n");
        doc.RestoreHsProject(new HsProject(true, false, GenerateManifest.Hash(doc.Opened),
            [new HsMoment(0, 0, 1000, [ProjectStore.KeyOf(HsModeTests.Normal)], 100, true) { LayerVolumes = [101] }]));
        Assert.Empty(doc.Session.State.HsCircles);
        Assert.Empty(doc.Session.State.LayerVolumes);
    }
}

using System.Text;
using System.Text.Json;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class DrumRenderTests
{
    private static ArrangeModel Model()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Drum\n[TimingPoints]\n0,500,4,3,0,70,1,0\n[HitObjects]\n256,192,1000,1,8,3:2:0:0:\n"));
        var folder = new MapFolder([]); return new(new EditSession(file, folder, Decompiler.Decompile(file, folder)), file, new HashSet<Lane>());
    }
    private static (EditHit Hit, Lane Wav, DrumRenderSource Source) Convert(ArrangeModel model)
    {
        var hit = model.Session.State.Hits.First();
        var wav = new Lane(LaneSource.BeatmapFile, "render.wav");
        var source = new DrumRenderSource(ProjectStore.KeyOf(hit.Lane), 36, 89);
        model.ApplyDrumRender(new Dictionary<EditHit, Lane> { [hit] = wav }, new() { [ProjectStore.KeyOf(wav)] = source });
        return (hit, wav, source);
    }
    [Fact]
    public void Conversion_preserves_timing_anchor_unrelated_volumes_and_is_one_undo_step()
    {
        var m = Model(); var before = m.Session.State; var volumes = before.Hits.ToDictionary(h => h, h => m.Session.VolumeOf(h)); var (hit, wav, _) = Convert(m);
        var baked = Assert.Single(m.Session.State.Hits, h => h.Lane == wav);
        Assert.Equal(hit.Time, baked.Time); Assert.Equal(hit.Anchor, baked.Anchor); Assert.Equal(100, m.Session.VolumeOf(baked));
        foreach (var other in before.Hits.Where(h => h != hit)) Assert.Equal(volumes[other], m.Session.VolumeOf(other));
        m.Session.Undo(); Assert.Equal(before.Hits, m.Session.State.Hits); Assert.Empty(m.DrumRenders);
        Assert.DoesNotContain(m.LaneRows, r => r.Lane == wav);
        m.Session.Redo(); Assert.Contains(m.Session.State.Hits, h => h.Lane == wav); Assert.Single(m.DrumRenders);
    }
    [Fact]
    public void Repeated_identical_conversion_is_a_noop_and_rerender_retains_original_note_velocity()
    {
        var m = Model(); var (_, wav, source) = Convert(m); var state = m.Session.State;
        var baked = state.Hits.Single(h => h.Lane == wav);
        m.ApplyDrumRender(new Dictionary<EditHit, Lane> { [baked] = wav }, m.DrumRenders.ToDictionary());
        Assert.Same(state, m.Session.State);
        var plan = DrumRenderPlan.Create(state, new Dictionary<Lane, int>(), m.DrumRenders);
        Assert.Equal(new DrumRenderKey(source.Note, source.Velocity), Assert.Single(plan.Keys));
        Assert.DoesNotContain(wav, MainWindow.DrumVstNotes(m).Keys);
        m.Session.Undo(); Assert.Empty(m.DrumRenders);
    }
    [Fact]
    public void Variants_collapse_under_parent_in_both_views_and_follow_parent_mute_solo()
    {
        var m = Model(); m.SetDrumMapMode(true); m.SetDrumMapMode(false);
        var (hit, wav, _) = Convert(m);
        Assert.True(m.IsFamilyParent(hit.Lane)); Assert.True(m.IsFamilyCollapsed(hit.Lane));
        Assert.DoesNotContain(m.Rows.OfType<LaneRow>(), r => r.Lane == wav);
        m.ToggleFamily(hit.Lane); Assert.Contains(m.Rows.OfType<LaneRow>(), r => r.Lane == wav);
        m.SetDrumMapMode(true);
        Assert.Contains(m.Rows.OfType<LaneRow>(), r => r.Lane == wav);
        m.ToggleFamily(hit.Lane); Assert.DoesNotContain(m.Rows.OfType<LaneRow>(), r => r.Lane == wav);
        m.ToggleMute(hit.Lane); Assert.False(m.IsAudible(wav)); m.ToggleMute(hit.Lane);
        m.ToggleSolo(hit.Lane); Assert.True(m.IsAudible(wav));
    }
    [Fact]
    public void Project_roundtrip_restores_grouping_and_rerender_identity()
    {
        var m = Model(); var (hit, wav, source) = Convert(m);
        var data = JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(new ProjectData { DrumRenders = m.DrumRenders, AddedLanes = [ProjectStore.KeyOf(wav)] }))!;
        var reopened = Model(); reopened.ApplyLaneLayout(data, _ => true);
        Assert.Equal(source, reopened.DrumRenders[ProjectStore.KeyOf(wav)]);
        Assert.Equal(reopened.FamilyKey(hit.Lane), reopened.FamilyKey(wav));
    }
    [Fact]
    public void Render_plan_reuses_note_velocity_and_preserves_unmapped_hits()
    {
        var m = Model(); var mapped = m.Session.State.Hits.First().Lane;
        var plan = DrumRenderPlan.Create(m.Session.State, new Dictionary<Lane, int> { [mapped] = 36 });
        Assert.All(plan.Hits.Keys, h => Assert.Equal(mapped, h.Lane)); Assert.Single(plan.Keys);
    }
    [Fact]
    public void Custom_parent_is_persisted_even_after_its_last_hit_is_rendered()
    {
        var m = Model(); var parent = new Lane(LaneSource.BeatmapFile, "custom-kick.wav");
        m.AddLane(parent); m.Session.Add([m.Session.HitAt(parent, 2000)]);
        var hit = m.Session.State.Hits.Single(h => h.Lane == parent); var wav = new Lane(LaneSource.BeatmapFile, "baked.wav");
        m.ApplyDrumRender(new Dictionary<EditHit, Lane> { [hit] = wav }, new() { [ProjectStore.KeyOf(wav)] = new(ProjectStore.KeyOf(parent), 36, 100) });
        Assert.Contains(ProjectStore.KeyOf(parent), m.LaneLayout().Added);
        var reopened = Model(); reopened.ApplyLaneLayout(new ProjectData { AddedLanes = m.LaneLayout().Added, DrumRenders = m.DrumRenders }, _ => true);
        Assert.Contains(reopened.LaneRows, row => row.Lane == parent);
        Assert.Equal(reopened.FamilyKey(parent), reopened.FamilyKey(wav));
    }
    [Fact]
    public void Rerender_preserves_post_render_gain_and_original_velocity()
    {
        var m = Model(); var (_, wav, source) = Convert(m);
        var hit = m.Session.State.Hits.Single(h => h.Lane == wav);
        m.Session.SetVolume([hit.Anchor!.Value], 35);
        int gain = m.Session.VolumeOf(hit);
        var plan = DrumRenderPlan.Create(m.Session.State, new Dictionary<Lane,int>(), m.DrumRenders);
        Assert.Equal(source.Velocity, plan.Hits[hit].Velocity);
        var replacement = new Lane(LaneSource.BeatmapFile, "new-kit.wav");
        m.ApplyDrumRender(new Dictionary<EditHit,Lane> { [hit] = replacement }, new() { [ProjectStore.KeyOf(replacement)] = source });
        Assert.Equal(gain, m.Session.VolumeOf(m.Session.State.Hits.Single(h => h.Lane == replacement)));
    }
    [Fact]
    public void Stale_assignments_are_rejected_without_changes()
    {
        var m = Model(); var state = m.Session.State; var hit = state.Hits.First() with { Time = -123 };
        Assert.Throws<InvalidOperationException>(() => m.Session.ApplyDrumRender(new Dictionary<EditHit, Lane> { [hit] = new(LaneSource.BeatmapFile, "x.wav") }));
        Assert.Same(state, m.Session.State);
    }
    [Fact]
    public void Failed_install_rolls_back_only_its_files_and_cache_rejects_tampering()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string staged = Path.Combine(root, "staged.wav"); File.WriteAllBytes(staged, [1, 2, 3]);
            var key = new DrumRenderKey(36, 90); DrumRenderAsset asset;
            using (var install = new DrumRenderInstall(root)) { asset = install.Install(key, staged, "Skin|drum-hitnormal"); install.Commit(); }
            Assert.True(DrumRenderKey.TryParse(asset.FileName, out var parsed)); Assert.Equal(key, parsed);
            using (var rollback = new DrumRenderInstall(root)) { rollback.Install(key, staged, "Skin|drum-hitnormal"); rollback.Install(new(38, 90), staged); }
            Assert.True(File.Exists(Path.Combine(root, asset.FileName))); Assert.Equal(2, Directory.GetFiles(root).Length);
            var cache = new DrumRenderCache(Path.Combine(root, "cache"), Path.Combine(root, "map.osu")); cache.Remember("kit1", [asset]);
            Assert.NotNull(cache.Find("kit1", key, root)); Assert.Null(cache.Find("kit2", key, root));
            File.WriteAllBytes(Path.Combine(root, asset.FileName), [9]); Assert.Null(cache.Find("kit1", key, root));
            using var collision = new DrumRenderInstall(root); Assert.Throws<IOException>(() => collision.Install(key, staged, "Skin|drum-hitnormal"));
        }
        finally { Directory.Delete(root, true); }
    }
}

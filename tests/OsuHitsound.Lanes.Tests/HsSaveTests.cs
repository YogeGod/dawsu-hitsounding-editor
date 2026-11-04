using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Tests;

public class HsSaveTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hs-save-").FullName;
    public void Dispose() => Directory.Delete(root, true);
    private string Manifests => Path.Combine(root, "manifests");
    private string Backups => Path.Combine(root, "backups");
    private MapDocument Open(string objects = "", string version = "Source")
    {
        string path = Path.Combine(root, "source.osu");
        File.WriteAllBytes(path, HsModeTests.Bytes(objects, version));
        return MapDocument.Load(path);
    }
    private static Dictionary<string, byte[]> Skin(EditSession session) => session.State.Hits.Select(h => h.Lane)
        .Where(l => l.Source == LaneSource.Skin).Distinct().ToDictionary(l => l.Name, _ => new byte[] { 1, 2, 3 });

    [Fact]
    public void An_unedited_named_HS_map_keeps_its_original_objects_and_does_not_mix_encodable_additions()
    {
        var doc = Open("12,34,2000,5,14,0:0:0:60:\n", "HS");
        doc.Session.SetHsMode(true);
        var (plan, _) = doc.PlanHsSave(Manifests, settings: new ExportMergeSettings());
        Assert.Empty(plan.Storage.Mixes);
        Assert.Empty(plan.Storage.MergeCombinations);
        Assert.Equal(doc.Opened, plan.Storage.Diff.ToBytes());
    }

    [Fact]
    public void Normal_save_projection_creates_centred_objects_in_the_current_difficulty_and_preserves_other_sections()
    {
        var doc = Open();
        byte[] original = doc.Opened;
        doc.Session.SetHsMode(true);
        doc.Session.Add([doc.Session.HitAt(HsModeTests.Normal, 1000), doc.Session.HitAt(HsModeTests.Normal, 2000)]);
        var (plan, _) = doc.PlanHsSave(Manifests);
        Assert.Equal(doc.Path, plan.Storage.OsuPath);
        Assert.Empty(plan.Storage.Conflicts);
        Assert.Empty(plan.Storage.Mixes);
        doc.WriteHsSave(plan, _ => throw new Exception("No mix needed"), Backups, Manifests);
        var file = OsuFile.Read(doc.Path);
        Assert.Equal(2, file.HitObjects.Count);
        Assert.All(file.HitObjects, o => { Assert.Equal(256, o.X); Assert.Equal(192, o.Y); });
        Assert.Equal("Source", HitsoundDiff.VersionOf(doc.Path));
        Assert.Equal(OsuFile.Parse(original).TimingPoints.Select(p => p.Line.Text), file.TimingPoints.Select(p => p.Line.Text));
        Assert.Single(Directory.GetFiles(root, "*.osu"));
    }

    [Fact]
    public void Save_reload_keeps_initially_empty_eligibility_logical_layers_volumes_and_owned_circle_deletion()
    {
        var doc = Open();
        doc.Session.SetHsMode(true);
        doc.Session.Add([doc.Session.HitAt(HsModeTests.Clap, 1000), doc.Session.HitAt(new Lane(LaneSource.Skin, "drum-hitwhistle"), 1000)]);
        doc.Session.SetVolume(doc.Session.State.Hits.Select(h => h.Anchor!.Value), 35);
        var skin = Skin(doc.Session);
        var (plan, _) = doc.PlanHsSave(Manifests, skin, new ExportMergeSettings());
        Assert.Empty(plan.Storage.Conflicts);
        Assert.Equal(2, plan.Storage.Mixes.Count); // an addition mix and its silent normal
        doc.WriteHsSave(plan, _ => [1, 2, 3], Backups, Manifests);
        var provenance = doc.HsProject;
        Assert.Single(provenance.Moments);
        var reopened = MapDocument.Load(doc.Path);
        Assert.False(reopened.Session.HsEligible); // no name match, until the saved origin is restored
        reopened.RestoreHsProject(provenance);
        Assert.True(reopened.Session.HsEligible);
        Assert.True(reopened.Session.State.HsMode);
        Assert.Equal(2, reopened.Session.State.Hits.Count);
        Assert.All(reopened.Session.State.Hits, h => { Assert.Equal(LaneSource.Skin, h.Lane.Source); Assert.Equal(35, reopened.Session.VolumeOf(h)); });
        Assert.False(reopened.Session.IsEdited);
        var (again, _) = reopened.PlanHsSave(Manifests, skin, new ExportMergeSettings());
        Assert.Empty(again.Storage.Mixes);
        Assert.Equal(2, again.Storage.Reused.Count);
        reopened.Session.Remove(reopened.Session.State.Hits.ToList());
        var (removed, _) = reopened.PlanHsSave(Manifests, skin);
        Assert.Empty(removed.Storage.Diff.HitObjects);
        reopened.WriteHsSave(removed, _ => throw new Exception("Nothing to render"), Backups, Manifests);
        Assert.Empty(OsuFile.Read(doc.Path).HitObjects);
    }

    [Fact]
    public void Existing_objects_keep_geometry_and_correct_keys_when_new_circles_sort_before_them()
    {
        const string originalObject = "12,34,2000,5,8,0:0:0:60:";
        var doc = Open(originalObject + "\n", "HS");
        doc.Session.SetHsMode(true);
        doc.Session.Add([doc.Session.HitAt(HsModeTests.Normal, 1000)]);
        var (plan, _) = doc.PlanHsSave(Manifests);
        Assert.Empty(plan.Storage.Conflicts);
        Assert.Equal(originalObject, plan.Storage.Diff.HitObjects[1].Line.Text);
        doc.WriteHsSave(plan, _ => throw new Exception(), Backups, Manifests);
        var reopened = MapDocument.Load(doc.Path);
        reopened.RestoreHsProject(doc.HsProject);
        reopened.Session.Remove(reopened.Session.State.Hits.Where(h => h.Time == 1000).ToList());
        var (removed, _) = reopened.PlanHsSave(Manifests);
        Assert.Equal(originalObject, Assert.Single(removed.Storage.Diff.HitObjects).Line.Text);
    }

    [Fact]
    public void External_map_changes_revoke_ownership_and_never_delete_preexisting_objects()
    {
        var doc = Open(); doc.Session.SetHsMode(true);
        doc.Session.Add([doc.Session.HitAt(HsModeTests.Normal, 1000)]);
        var (plan, _) = doc.PlanHsSave(Manifests); doc.WriteHsSave(plan, _ => [], Backups, Manifests);
        var provenance = doc.HsProject;
        File.AppendAllText(doc.Path, "//external change\n");
        var reopened = MapDocument.Load(doc.Path); reopened.RestoreHsProject(provenance);
        Assert.Empty(reopened.Session.State.HsCircles);
        reopened.Session.Remove(reopened.Session.State.Hits.ToList());
        Assert.Single(reopened.Compile(reopened.Session.State).File.HitObjects);
    }

    [Fact]
    public void Save_while_disabled_keeps_an_empty_owned_circle_silent_then_reenabling_removes_it()
    {
        var doc = Open(); doc.Session.SetHsMode(true);
        var hit = doc.Session.HitAt(HsModeTests.Normal, 1000); doc.Session.Add([hit]);
        doc.Session.SetHsMode(false); doc.Session.Remove([hit]);
        var (plan, _) = doc.PlanHsSave(Manifests);
        Assert.Single(plan.Storage.Diff.HitObjects);
        Assert.Equal("soft-hitnormal", Assert.Single(plan.Storage.Mixes).DestinationSlot);
        doc.WriteHsSave(plan, _ => [0, 0], Backups, Manifests);
        var reopened = MapDocument.Load(doc.Path); reopened.RestoreHsProject(doc.HsProject);
        Assert.False(reopened.Session.State.HsMode);
        Assert.Empty(reopened.Session.State.Hits);
        Assert.Single(reopened.Session.Moments);
        reopened.Session.SetHsMode(true);
        Assert.Empty(reopened.Session.Moments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_render_or_external_edit_during_render_leaves_all_generated_outputs_unwritten(bool changeSource)
    {
        var doc = Open(); doc.Session.SetHsMode(true);
        // A single file can now be referenced directly; use an actual overlap to exercise rendering failures.
        doc.Session.Add([doc.Session.HitAt(new Lane(LaneSource.BeatmapFile, "kick.wav"), 1000),
            doc.Session.HitAt(new Lane(LaneSource.BeatmapFile, "snare.wav"), 1000)]);
        File.WriteAllBytes(Path.Combine(root, "kick.wav"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(root, "snare.wav"), [4, 5, 6]);
        var (plan, _) = doc.PlanHsSave(Manifests);
        Assert.Single(plan.Storage.Mixes);
        Assert.Throws<IOException>(() => doc.WriteHsSave(plan, _ =>
        {
            if (changeSource) { File.AppendAllText(doc.Path, "//external\n"); return [1, 2, 3]; }
            throw new IOException("decode failed");
        }, Backups, Manifests));
        foreach (var mix in plan.Storage.Mixes) Assert.False(File.Exists(Path.Combine(root, mix.FileName)));
        Assert.Empty(OsuFile.Read(doc.Path).HitObjects);
    }

    [Fact]
    public void A_changed_edit_state_cannot_be_saved_using_a_stale_plan()
    {
        var doc = Open(); doc.Session.SetHsMode(true);
        doc.Session.Add([doc.Session.HitAt(HsModeTests.Normal, 1000)]);
        var (plan, _) = doc.PlanHsSave(Manifests);
        doc.Session.Add([doc.Session.HitAt(HsModeTests.Normal, 2000)]);
        Assert.Throws<IOException>(() => doc.WriteHsSave(plan, _ => [], Backups, Manifests));
        Assert.Equal(doc.Opened, File.ReadAllBytes(doc.Path));
    }

    [Fact]
    public void A_reused_sample_changed_after_planning_stops_save_before_map_writes()
    {
        var doc = Open(); doc.Session.SetHsMode(true);
        doc.Session.Add([doc.Session.HitAt(HsModeTests.Clap, 1000)]);
        var skin = Skin(doc.Session);
        var (first, _) = doc.PlanHsSave(Manifests, skin);
        doc.WriteHsSave(first, _ => [1, 2, 3], Backups, Manifests);
        var reopened = MapDocument.Load(doc.Path); reopened.RestoreHsProject(doc.HsProject);
        var (again, _) = reopened.PlanHsSave(Manifests, skin);
        var reused = Assert.Single(again.Storage.Reused);
        File.WriteAllBytes(Path.Combine(root, reused.FileName), [9, 9, 9]);
        Assert.Throws<IOException>(() => reopened.WriteHsSave(again, _ => throw new Exception(), Backups, Manifests));
        Assert.Equal(reopened.Opened, File.ReadAllBytes(reopened.Path));
    }

    [Fact]
    public void Malformed_logical_sidecar_layers_do_not_adopt_or_delete_existing_circles()
    {
        var doc = Open("256,192,1000,1,0,0:0:0:0:\n", "HS");
        var project = new HsProject(true, false, GenerateManifest.Hash(doc.Opened),
            [new HsMoment(0, 0, 1000, [null!], 100, true)]);
        doc.RestoreHsProject(project);
        Assert.Empty(doc.Session.State.HsCircles);
        doc.Session.Remove(doc.Session.State.Hits.ToList());
        Assert.Single(doc.Compile(doc.Session.State).File.HitObjects);
    }

    [Fact]
    public void A_mixed_save_on_a_protected_object_restores_input_layers_without_taking_ownership()
    {
        var doc = Open("12,34,1000,1,0,0:0:0:100:\n", "HS");
        doc.Session.SetHsMode(true);
        doc.Session.Add([doc.Session.HitAt(HsModeTests.Clap, 1000), doc.Session.HitAt(new Lane(LaneSource.Skin, "drum-hitwhistle"), 1000)]);
        var (plan, _) = doc.PlanHsSave(Manifests, Skin(doc.Session), new ExportMergeSettings());
        doc.WriteHsSave(plan, _ => [1, 2, 3], Backups, Manifests);
        var reopened = MapDocument.Load(doc.Path); reopened.RestoreHsProject(doc.HsProject);
        Assert.Equal(3, reopened.Session.State.Hits.Count);
        Assert.All(reopened.Session.State.Hits, h => Assert.Equal(LaneSource.Skin, h.Lane.Source));
        Assert.Empty(reopened.Session.State.HsCircles);
        reopened.Session.Remove(reopened.Session.State.Hits.ToList());
        var kept = Assert.Single(reopened.Compile(reopened.Session.State).File.HitObjects);
        Assert.Equal(12, kept.X); Assert.Equal(34, kept.Y);
    }
}

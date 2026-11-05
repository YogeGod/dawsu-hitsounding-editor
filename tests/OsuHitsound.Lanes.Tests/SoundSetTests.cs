using System.Collections.Immutable;
using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Tests;

public sealed class SoundSetTests : IDisposable
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Drum_render_detaches_only_converted_member_and_undo_restores_binding(bool hs)
    {
        var s = Session(hs); var set = Set(); s.SaveSoundSet(set);
        var hits = s.ApplySoundSet(set.Id, 1000); var original = s.State;
        var wav = new Lane(LaneSource.BeatmapFile, "rendered.wav");
        s.ApplyDrumRender(new Dictionary<EditHit, Lane> { [hits[0]] = wav });
        var rendered = Assert.Single(s.State.Hits, h => h.Lane == wav);
        Assert.Equal(Guid.Empty, rendered.SetPlacement); Assert.Equal(100, s.VolumeOf(rendered));
        Assert.Equal(40, s.VolumeOf(s.State.Hits.Single(h => h.SetMember == hits[1].SetMember)));
        s.SaveSoundSet(set with { Name = "Renamed after render" });
        Assert.Contains(s.State.Hits, h => h.Lane == wav); Assert.Equal(2, s.State.Hits.Count);
        s.Undo(); s.Undo(); Assert.Equal(original.Hits, s.State.Hits);
    }

    [Fact]
    public void Independent_volume_edit_on_owned_member_survives_set_rendering()
    {
        var s = Session(); var set = Set(); s.SaveSoundSet(set);
        var hits = s.ApplySoundSet(set.Id, 1000);
        s.SetLayerVolumes(new Dictionary<EditHit, int> { [hits[0]] = 23 });
        s.SaveSoundSet(set with { Name = "Renamed" });
        Assert.Equal(23, s.VolumeOf(s.State.Hits.Single(h => h.SetMember == hits[0].SetMember)));
        Assert.Equal(40, s.VolumeOf(s.State.Hits.Single(h => h.SetMember == hits[1].SetMember)));
    }
    private readonly string root = Directory.CreateTempSubdirectory("sound-sets-").FullName;
    private readonly Dictionary<string, byte[]> skin = new() { ["normal-hitnormal"] = [1, 2, 3], ["soft-hitclap"] = [4, 5, 6], ["soft-hitwhistle"] = [7, 8, 9] };
    private const string MapText = "osu file format v14\n[General]\nSampleSet:Normal\n[Metadata]\nVersion:HS\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n";
    public void Dispose() => Directory.Delete(root, true);
    private EditSession Session(bool hs = false)
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(MapText)); var folder = MapFolder.FromDirectory(root);
        var session = new EditSession(file, folder, Decompiler.Decompile(file, folder));
        if (hs) session.SetHsMode(true);
        return session;
    }
    private SoundSet Set(string name = "Kick + clap", string addition = "soft-hitclap") => SoundSetFiles.Install(root,
        SoundSetFiles.Capture(name, [(new(LaneSource.Skin, "normal-hitnormal"), 80), (new(LaneSource.Skin, addition), 40)], MapFolder.FromDirectory(root), skin));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Applying_a_set_keeps_each_member_and_relative_gain_in_normal_and_HS_modes(bool hs)
    {
        var s = Session(hs); var set = Set(); s.SaveSoundSet(set);
        var hits = s.ApplySoundSet(set.Id, 1000);
        Assert.Equal(2, hits.Count); Assert.All(hits, h => Assert.NotEqual(Guid.Empty, h.SetPlacement));
        Assert.Equal(new[] { 40, 80 }, hits.Select(s.VolumeOf).Order());
        Assert.Equal(hs ? 1 : 0, s.State.HsCircles.Count);
        Assert.Equal(2, s.ApplySoundSet(set.Id, 1000).Count); Assert.Equal(2, s.State.Hits.Count);
    }

    [Fact]
    public void Add_adopts_existing_independent_kick_and_keeps_its_volume_override()
    {
        var s = Session(); var set = Set(); s.SaveSoundSet(set);
        var kick = s.HitAt(new(LaneSource.Skin, "normal-hitnormal"), 1000); s.Add([kick]);
        var applied = s.ApplySoundSet(set.Id, 1000);
        Assert.Equal(2, s.State.Hits.Count);
        var adopted = applied.Single(h => h.Lane.Sample?.Sound == "hitnormal");
        Assert.NotEqual(kick.Lane, adopted.Lane);
        Assert.Equal(100, s.VolumeOf(adopted));
        s.SaveSoundSet(set with { Members = set.Members.Select(m => m with { Volume = 20 }).ToImmutableList() });
        Assert.Equal(100, s.VolumeOf(s.State.Hits.Single(h => h.SetMember == adopted.SetMember)));
        Assert.Equal(20, s.VolumeOf(s.State.Hits.Single(h => h.SetMember != adopted.SetMember)));
    }

    [Fact]
    public void Replace_keeps_unrelated_whistle_and_removes_only_set_sound_types()
    {
        var s = Session(); var set = Set(); s.SaveSoundSet(set);
        var whistle = s.HitAt(new(LaneSource.Skin, "soft-hitwhistle"), 1000);
        s.Add([whistle, s.HitAt(new(LaneSource.Skin, "normal-hitnormal"), 1000)]);
        s.ApplySoundSet(set.Id, 1000, replace: true);
        Assert.Contains(whistle, s.State.Hits); Assert.Equal(3, s.State.Hits.Count);
        Assert.Single(s.State.Hits, h => h.Lane.Sample?.Sound == "hitnormal");
    }

    [Fact]
    public void First_set_keeps_shared_kick_and_second_updates_only_its_owned_sounds()
    {
        var s = Session(); var first = Set(); var second = Set("Kick + whistle", "soft-hitwhistle");
        s.SaveSoundSet(first); s.SaveSoundSet(second);
        var initial = s.ApplySoundSet(first.Id, 1000); var owner = initial[0].SetPlacement;
        s.ApplySoundSet(second.Id, 1000);
        Assert.Equal(3, s.State.Hits.Count);
        Assert.Equal(owner, s.State.Hits.Single(h => h.Lane.Sample?.Sound == "hitnormal").SetPlacement);
        s.SaveSoundSet(second with { Members = second.Members.Select(m => m with { Volume = 10 }).ToImmutableList() });
        Assert.Equal(80, s.VolumeOf(s.State.Hits.Single(h => h.Lane.Sample?.Sound == "hitnormal")));
        Assert.Equal(10, s.VolumeOf(s.State.Hits.Single(h => h.Lane.Sample?.Sound == "hitwhistle")));
    }

    [Fact]
    public void Placement_gain_and_member_override_preserve_balance_across_definition_updates()
    {
        var s = Session(); var set = Set(); s.SaveSoundSet(set); var hits = s.ApplySoundSet(set.Id, 1000); Guid id = hits[0].SetPlacement;
        s.SetSoundSetGain(id, 50); Assert.Equal(new[] { 20, 40 }, s.State.Hits.Select(s.VolumeOf).Order());
        var clap = set.Members.Single(m => m.Slot.EndsWith("hitclap")); s.SetSoundSetMember(id, clap.Id, 60);
        s.SaveSoundSet(set with { Members = set.Members.Select(m => m with { Volume = 100 }).ToImmutableList() });
        Assert.Equal(30, s.VolumeOf(s.State.Hits.Single(h => h.SetMember == clap.Id)));
        Assert.Equal(50, s.VolumeOf(s.State.Hits.Single(h => h.SetMember != clap.Id)));
        s.ResetSoundSet(id); Assert.All(s.State.Hits, h => Assert.Equal(100, s.VolumeOf(h)));
    }

    [Fact]
    public void Local_deletion_stays_removed_until_reset_and_detaching_prevents_updates()
    {
        var s = Session(); var set = Set(); s.SaveSoundSet(set); var hits = s.ApplySoundSet(set.Id, 1000); Guid id = hits[0].SetPlacement;
        s.Remove([hits[1]]); s.SaveSoundSet(set with { Name = "Updated" }); Assert.Single(s.State.Hits);
        s.ResetSoundSet(id); Assert.Equal(2, s.State.Hits.Count);
        s.DetachSoundSet(id); var before = s.State.Hits;
        s.SaveSoundSet(set with { Members = set.Members.Select(m => m with { Volume = 5 }).ToImmutableList() });
        Assert.Equal(before, s.State.Hits); Assert.All(s.State.Hits, h => Assert.Equal(Guid.Empty, h.SetPlacement));
    }

    [Fact]
    public void Set_update_and_all_linked_placements_undo_and_redo_together()
    {
        var s = Session(); var set = Set(); s.SaveSoundSet(set); s.ApplySoundSet(set.Id, 1000); s.ApplySoundSet(set.Id, 2000);
        var before = s.State; int count = s.Position;
        s.SaveSoundSet(set with { Members = set.Members.Select(m => m with { Volume = 25 }).ToImmutableList() });
        Assert.Equal(count + 1, s.Position); Assert.All(s.State.Hits, h => Assert.Equal(25, s.VolumeOf(h)));
        s.Undo(); Assert.Same(before, s.State); s.Redo(); Assert.All(s.State.Hits, h => Assert.Equal(25, s.VolumeOf(h)));
    }

    [Fact]
    public void Whole_placement_move_and_copy_keep_link_gain_and_overrides()
    {
        var s = Session(); var set = Set(); s.SaveSoundSet(set); var hits = s.ApplySoundSet(set.Id, 1000);
        s.SetSoundSetGain(hits[0].SetPlacement, 50);
        var moved = s.Move(s.State.Hits, 500, h => h.Lane);
        Assert.Equal(1500, s.State.SoundPlacements.Single().Time);
        var copy = s.Copy(moved, 1500);
        Assert.Equal(2, s.State.SoundPlacements.Count); Assert.NotEqual(moved[0].SetPlacement, copy[0].SetPlacement);
        s.SaveSoundSet(set with { Members = set.Members.Select(m => m with { Volume = 100 }).ToImmutableList() });
        Assert.All(s.State.Hits, h => Assert.Equal(50, s.VolumeOf(h)));
    }

    [Fact]
    public void Project_binding_roundtrip_retains_links_but_changed_file_hash_revokes_them()
    {
        var s = Session(); var set = Set(); s.SaveSoundSet(set); var hits = s.ApplySoundSet(set.Id, 1000);
        var project = s.CaptureSoundSets("same");
        var other = Session(); other.Restore(hits.Select(h => h with { SetPlacement = Guid.Empty, SetMember = Guid.Empty }));
        other.RestoreSoundSets(project, "same"); Assert.Equal(2, other.State.Hits.Count(h => h.SetPlacement != Guid.Empty));
        var changed = Session(); changed.Restore(hits.Select(h => h with { SetPlacement = Guid.Empty, SetMember = Guid.Empty }));
        changed.RestoreSoundSets(project, "changed"); Assert.Empty(changed.State.SoundPlacements); Assert.Single(changed.State.SoundSets);
        Assert.All(changed.State.Hits, h => Assert.Equal(Guid.Empty, h.SetPlacement));
    }

    [Fact]
    public void Library_file_contains_frozen_audio_and_installs_without_original_sources()
    {
        var set = Set(); string file = Path.Combine(root, "set.dawsuset"); SoundSetFiles.Save(file, set);
        var loaded = SoundSetFiles.Load(file); string target = Directory.CreateDirectory(Path.Combine(root, "other")).FullName;
        var installed = SoundSetFiles.Install(target, loaded);
        Assert.All(installed.Members, m => Assert.Equal(m.Audio, File.ReadAllBytes(Path.Combine(target, m.Lane.Name))));
        Assert.All(loaded.Members, m => Assert.Equal(set.Members.Single(o => o.Id == m.Id).Audio, m.Audio));
    }

    [Fact]
    public void Moving_and_copying_a_shared_placement_to_empty_times_keeps_its_complete_sound()
    {
        var s = Session(); var first = Set(); var second = Set("Kick + whistle", "soft-hitwhistle");
        s.SaveSoundSet(first); s.SaveSoundSet(second); s.ApplySoundSet(first.Id, 1000);
        var partial = s.ApplySoundSet(second.Id, 1000);
        Assert.Single(partial);
        var copy = s.Copy(partial, 2000);
        Assert.Equal(2, copy.Count); Assert.All(copy, h => Assert.Equal(3000, h.Time));
        s.Move(partial, 1000, h => h.Lane);
        Assert.Equal(2, s.State.Hits.Count(h => h.Time == 2000));
        Assert.Equal(2, s.State.Hits.Count(h => h.Time == 1000));
        Assert.Single(s.State.Hits, h => h.Time == 1000 && h.Lane.Sample?.Sound == "hitnormal");
    }

    [Fact]
    public void New_set_members_adopt_existing_sounds_without_stealing_existing_links()
    {
        var s = Session(); var first = Set(); var second = Set("Kick + whistle", "soft-hitwhistle");
        s.SaveSoundSet(first); s.SaveSoundSet(second); s.ApplySoundSet(first.Id, 1000); s.ApplySoundSet(second.Id, 1000);
        var clap = first.Members.Single(m => m.Slot.EndsWith("hitclap"));
        s.SaveSoundSet(second with { Members = second.Members.Add(clap with { Id = Guid.NewGuid() }) });
        Assert.Equal(3, s.State.Hits.Count);
        var firstPlacement = s.State.SoundPlacements.Single(p => p.SetId == first.Id);
        Assert.Equal(firstPlacement.Id, s.State.Hits.Single(h => h.Lane.Sample?.Sound == "hitclap").SetPlacement);
    }

    [Fact]
    public void Deleting_an_entire_placement_does_not_prevent_drawing_it_again()
    {
        var s = Session(); var set = Set(); s.SaveSoundSet(set); var hits = s.ApplySoundSet(set.Id, 1000);
        s.Remove(hits); Assert.Empty(s.State.Hits);
        Assert.Equal(2, s.ApplySoundSet(set.Id, 1000).Count);
    }

    [Fact]
    public void Serialized_project_preserves_immutable_definitions_and_binding_ids()
    {
        var s = Session(true); var set = Set(); s.SaveSoundSet(set); s.ApplySoundSet(set.Id, 1000);
        var data = new ProjectData { SoundSets = s.CaptureSoundSets("hash") };
        ProjectStore.Save(root, Path.Combine(root, "map.osu"), data);
        var restored = ProjectStore.Load(root, Path.Combine(root, "map.osu"))!;
        Assert.Equal(set.Members[0].Audio, restored.SoundSets!.Sets[0].Members[0].Audio);
        Assert.Equal(data.SoundSets!.Bindings, restored.SoundSets.Bindings);
    }

    [Fact]
    public void Reset_returns_a_locally_moved_member_to_its_placement_time()
    {
        var s = Session(); var set = Set(); s.SaveSoundSet(set); var hits = s.ApplySoundSet(set.Id, 1000);
        s.Move([hits[0]], 250, h => h.Lane); s.ResetSoundSet(hits[0].SetPlacement);
        Assert.All(s.State.Hits, h => Assert.Equal(1000, h.Time));
    }

    [Fact]
    public void Captured_audio_does_not_change_when_original_sample_changes()
    {
        string name = "normal-hitnormal.wav"; File.WriteAllBytes(Path.Combine(root, name), [1, 2, 3]);
        var set = SoundSetFiles.Capture("Frozen kick", [(new(LaneSource.BeatmapFile, name), 70)], MapFolder.FromDirectory(root), skin);
        File.WriteAllBytes(Path.Combine(root, name), [9, 9, 9]);
        var installed = SoundSetFiles.Install(root, set);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(root, installed.Members[0].Lane.Name)));
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(Path.Combine(root, name)));
    }

    [Fact]
    public void Hs_save_and_project_restore_keep_set_bindings_and_owned_circles()
    {
        var path = Path.Combine(root, "HS.osu"); File.WriteAllText(path, MapText);
        var doc = MapDocument.Load(path); doc.Session.SetHsMode(true); var set = Set(); doc.Session.SaveSoundSet(set); doc.Session.ApplySoundSet(set.Id, 1000);
        var planned = doc.PlanHsSave(Path.Combine(root, "manifests"), settings: new() { PairSamples = true });
        doc.WriteHsSave(planned.Plan, m => m.SilenceFor is not null ? BlankSample.Bytes : [10, 11, 12], Path.Combine(root, "backups"), Path.Combine(root, "manifests"));
        var project = doc.Session.CaptureSoundSets(doc.HsProject.FileHash);
        var reopened = MapDocument.Load(path); reopened.RestoreHsProject(doc.HsProject); reopened.Session.RestoreSoundSets(project, GenerateManifest.Hash(reopened.Opened));
        Assert.Single(reopened.Session.State.HsCircles);
        Assert.Equal(2, reopened.Session.State.Hits.Count(h => h.SetPlacement != Guid.Empty));
        Assert.Equal(new[] { 40, 80 }, reopened.Session.State.Hits.Select(reopened.Session.VolumeOf).Order());
    }

    [Fact]
    public void Discarded_loose_bindings_are_not_resurrected_by_a_later_set_update()
    {
        var s = Session(); var set = Set(); s.SaveSoundSet(set); s.ApplySoundSet(set.Id, 1000);
        var reopened = Session(); reopened.RestoreSoundSets(s.CaptureSoundSets("same"), "same");
        Assert.Empty(reopened.State.SoundPlacements);
        reopened.SaveSoundSet(set with { Name = "Updated" }); Assert.Empty(reopened.State.Hits);
    }

    [Fact]
    public void Invalid_set_paths_and_missing_member_names_fail_validation()
    {
        var set = Set();
        Assert.Throws<FormatException>(() => (set with { Members = [set.Members[0] with { Lane = new(LaneSource.BeatmapFile, "../outside.wav") }] }).Validate());
        Assert.Throws<FormatException>(() => (set with { Members = [set.Members[0] with { Lane = new(LaneSource.BeatmapFile, null!) }] }).Validate());
    }

    [Fact]
    public void Add_can_adopt_an_identical_custom_named_sample()
    {
        File.WriteAllBytes(Path.Combine(root, "kick.wav"), [1, 2, 3]);
        var s = Session(); var lane = new Lane(LaneSource.BeatmapFile, "kick.wav"); s.Add([s.HitAt(lane, 1000)]);
        var set = SoundSetFiles.Install(root, SoundSetFiles.Capture("Custom kick", [(lane, 80)], MapFolder.FromDirectory(root), skin));
        s.SaveSoundSet(set); s.ApplySoundSet(set.Id, 1000);
        Assert.Single(s.State.Hits); Assert.NotEqual(Guid.Empty, s.State.Hits[0].SetPlacement);
    }

    [Fact]
    public void Very_quiet_set_members_are_baked_without_raising_their_gain_or_unrelated_layers()
    {
        var normal = new Lane(LaneSource.BeatmapFile, "normal-hitnormal.wav"); var clap = new Lane(LaneSource.BeatmapFile, "soft-hitclap2.wav");
        var p = new PairingPlanner(new MapFolder([normal.Name, clap.Name], Encoding.UTF8.GetBytes));
        var wanted = p.For([(normal, 5), (clap, 4)], exactGainLanes: new HashSet<Lane> { clap });
        Assert.Equal(100, wanted.Volume);
        Assert.Contains(p.Files, m => m.Layers.Any(l => l.Lane == clap && l.Volume == 4));
        Assert.Contains(p.Files, m => m.Layers.Any(l => l.Lane == normal && l.Volume == HitsoundRules.StableMinimumVolume));
    }

    [Fact]
    public void Zero_volume_set_uses_zero_gain_audio_even_when_direct_fields_would_inherit_volume()
    {
        var normal = new Lane(LaneSource.BeatmapFile, "normal-hitnormal.wav");
        var p = new PairingPlanner(new MapFolder([normal.Name], Encoding.UTF8.GetBytes));
        var wanted = p.For([(normal, 0)], alreadyEncoded: true, exactGainLanes: new HashSet<Lane> { normal });
        Assert.Equal(100, wanted.Volume); Assert.Null(p.Files.Single().CopyBytes);
        Assert.Equal(0, p.Files.Single().Layers.Single().Volume);
    }

    [Fact]
    public void Changed_snapshot_audio_blocks_save_and_generate_and_reports_a_visible_problem()
    {
        string path = Path.Combine(root, "map.osu"); File.WriteAllText(path, MapText);
        var doc = MapDocument.Load(path); var set = Set(); doc.Session.SaveSoundSet(set); doc.Session.ApplySoundSet(set.Id, 1000);
        File.WriteAllBytes(Path.Combine(root, set.Members[0].Lane.Name), [99, 98]); doc.RescanFolder();
        Assert.Throws<IOException>(() => doc.PlanGenerate(Path.Combine(root, "manifests"), mergeSettings: new() { PairSamples = true }));
        Assert.Throws<IOException>(() => doc.PlanHsSave(Path.Combine(root, "manifests"), settings: new() { PairSamples = true }));
        Assert.Contains(doc.Compile(doc.Session.State, new() { PairSamples = true }).Result.Conflicts, c => c.Reason.Contains("missing or changed"));
        Assert.Equal(doc.Opened, File.ReadAllBytes(path));
    }

    [Fact]
    public void Import_does_not_adopt_unrelated_existing_files_even_when_bytes_match()
    {
        var set = Set(); string target = Directory.CreateDirectory(Path.Combine(root, "other")).FullName;
        foreach (var m in set.Members) File.WriteAllBytes(Path.Combine(target, m.Lane.Name), m.Audio);
        var installed = SoundSetFiles.Install(target, set);
        Assert.All(installed.Members, m => Assert.NotEqual(set.Members.Single(o => o.Id == m.Id).Lane, m.Lane));
    }
}

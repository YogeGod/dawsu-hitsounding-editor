using System.Collections.Immutable;
using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public sealed class DrumMapTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("drum-map-tests-").FullName;
    private static Lane Kick => new(LaneSource.Skin, "drum-hitnormal");
    private static Lane Snare => new(LaneSource.Skin, "soft-hitclap");
    private static DrumMapPreset Preset() => new(Guid.NewGuid(), "Acoustic", [new(Guid.NewGuid(), "Kick", ProjectStore.KeyOf(Kick), ProjectStore.KeyOf(Kick))]);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void User_starter_kits_preserve_exact_names_order_and_unassigned_samples()
    {
        var maps = DrumMapDefaults.Create(); maps.Validate();
        Assert.Equal(new[] { "Yoge Expanded Kit", "Yoge Simple Kit" }, maps.Presets.Select(p => p.Name));
        Assert.Equal(new[] { "Kick", "Snare", "Ghost Notes", "T1", "T2", "T3", "T4", "Tight Tip", "Tight Edge", "Closed Tip", "Closed Edge",
            "Hi-Hat Open 1", "Hi-Hat Open 2", "Hi-Hat Open 3", "Ride Bell", "Bow Ride (Low)", "Bow Ride (High)", "Right Crash", "Left Crash",
            "Ride Crash", "China", "Stack", "Mini Stack Left", "Mini Stack Right", "Splash" }, maps.Presets[0].Rows.Select(r => r.Name));
        Assert.Equal(new[] { "Kick", "Snare", "T1", "T2", "T3", "T4", "Tight Tip", "Tight Edge", "Closed Tip", "Closed Edge",
            "Hi-Hat Open 1", "Hi-Hat Open 2", "Hi-Hat Open 3", "Ride Bell", "Right Crash", "Left Crash", "Ride Crash", "China" }, maps.Presets[1].Rows.Select(r => r.Name));
        Assert.All(maps.Presets.SelectMany(p => p.Rows), r => { Assert.Null(r.LaneKey); Assert.Null(r.SlotKey); });
        Assert.All(maps.Presets, p => Assert.Empty(p.Audio));
    }

    [Fact]
    public void Presets_share_mapset_state_and_reject_stale_writes()
    {
        string mapset = Path.Combine(root, "song");
        var initial = DrumMapStore.Load(root, mapset);
        Assert.Equal("missing", initial.Hash);
        var preset = Preset(); var maps = new DrumMapCollection([preset], preset.Id, true);
        string hash = DrumMapStore.Save(root, mapset, maps, initial.Hash);
        var read = DrumMapStore.Load(root, mapset.ToUpperInvariant());
        Assert.Equal(hash, read.Hash); Assert.Equal("Kick", read.Maps.Presets[0].Rows[0].Name);
        Assert.Throws<IOException>(() => DrumMapStore.Save(root, mapset, DrumMapCollection.Empty, "missing"));
        Assert.Equal(hash, DrumMapStore.Load(root, mapset).Hash);
    }
    [Fact]
    public void Portable_preset_round_trips_audio_and_aliases()
    {
        var preset = Preset(); var row = preset.Rows[0];
        preset = preset with { Rows = preset.Rows.Add(row with { Id = Guid.NewGuid(), Name = "Sub kick" }), Audio = [new(row.LaneKey!, row.SlotKey!, [1, 2, 3])] };
        string path = Path.Combine(root, "kit.dawsudrum"); DrumMapStore.Export(path, preset);
        var restored = DrumMapStore.Import(path);
        Assert.Equal(2, restored.Rows.Count); Assert.Equal(restored.Rows[0].LaneKey, restored.Rows[1].LaneKey);
        Assert.Equal(new byte[] { 1, 2, 3 }, restored.Audio[0].Bytes);
    }
    [Theory]
    [InlineData("BeatmapFile|../outside.wav")]
    [InlineData("BeatmapFile|C:/outside.wav")]
    [InlineData("StoryboardFile|boom.wav")]
    [InlineData("999|soft-hitclap.wav")]
    [InlineData("Skin|soft-sliderslide")]
    public void Invalid_assignments_are_rejected(string key)
    {
        var preset = Preset();
        Assert.Throws<FormatException>(() => (preset with { Rows = [preset.Rows[0] with { LaneKey = key }] }).Validate());
    }
    [Fact]
    public void Duplicate_row_ids_and_oversized_audio_are_rejected()
    {
        var preset = Preset();
        Assert.Throws<FormatException>(() => (preset with { Rows = preset.Rows.Add(preset.Rows[0]) }).Validate());
        Assert.Throws<FormatException>(() => (preset with { Audio = [new(preset.Rows[0].LaneKey!, preset.Rows[0].SlotKey!, new byte[33554433])] }).Validate());
    }
    [Fact]
    public void Mapset_store_rejects_embedded_audio_before_writing()
    {
        var preset = Preset(); preset = preset with { Audio = [new(preset.Rows[0].LaneKey!, preset.Rows[0].SlotKey!, [1])] };
        Assert.Throws<FormatException>(() => DrumMapStore.Save(root, root, new([preset], preset.Id, true), "missing"));
        Assert.False(File.Exists(DrumMapStore.PathFor(root, root)));
    }

    [Fact]
    public void Corrupt_collection_is_never_silently_replaced()
    {
        string path = DrumMapStore.PathFor(root, root); File.WriteAllText(path, "broken");
        Assert.Throws<FormatException>(() => DrumMapStore.Load(root, root));
        Assert.Equal("broken", File.ReadAllText(path));
    }
    private static EditSession Session()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Drum\n[TimingPoints]\n0,500,4,3,0,65,1,0\n[HitObjects]\n256,192,1000,1,8,3:2:0:0:\n256,192,2000,1,0,3:3:0:0:\n"));
        var folder = new MapFolder([]); return new(file, folder, Decompiler.Decompile(file, folder));
    }
    [Fact]
    public void Migration_requires_explicit_overlap_choice()
    {
        var session = Session(); var state = session.State;
        Assert.Single(session.DrumMapOverlaps(new Dictionary<Lane, Lane> { [Kick] = Snare }));
        Assert.Throws<InvalidOperationException>(() => session.MoveDrumMapHits(new Dictionary<Lane, Lane> { [Kick] = Snare }, null, () => {}, () => {}));
        Assert.Same(state, session.State);
    }
    [Theory]
    [InlineData(DrumMapOverlap.KeepBoth, 3)]
    [InlineData(DrumMapOverlap.KeepDestination, 2)]
    [InlineData(DrumMapOverlap.ReplaceDestination, 2)]
    public void Migration_keeps_timing_volume_and_single_step_undo(DrumMapOverlap choice, int count)
    {
        var session = Session(); var state = session.State; bool changed = false;
        session.MoveDrumMapHits(new Dictionary<Lane, Lane> { [Kick] = Snare }, choice, () => changed = false, () => changed = true);
        Assert.True(changed); Assert.Equal(count, session.State.Hits.Count);
        Assert.All(session.State.Hits, h => { Assert.Equal(Snare, h.Lane); Assert.Equal(65, session.VolumeOf(h)); });
        Assert.Equal(new double[] { 1000, 2000 }, session.State.Hits.Select(h => h.Time).Distinct().Order());
        session.Undo(); Assert.False(changed); Assert.Same(state, session.State);
        session.Redo(); Assert.True(changed); Assert.Equal(count, session.State.Hits.Count);
    }
    [Fact]
    public void Migrating_unequal_volumes_preserves_both_layers()
    {
        var session = Session(); var source = session.State.Hits.First(h => h.Lane == Kick);
        session.Restore([session.HitAt(Kick, 3000)], new Dictionary<EditHit, int> { [source] = 23 });
        session.MoveDrumMapHits(new Dictionary<Lane, Lane> { [Kick] = Snare }, DrumMapOverlap.KeepBoth, () => {}, () => {});
        Assert.Contains(session.State.Hits, h => h.Time == 1000 && session.VolumeOf(h) == 23);
        Assert.Contains(session.State.Hits, h => h.Time == 1000 && session.VolumeOf(h) == 65);
    }
}

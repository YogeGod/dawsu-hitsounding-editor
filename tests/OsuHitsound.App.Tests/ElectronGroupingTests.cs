using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class ElectronGroupingTests
{
    private static Lane File(string name) => new(LaneSource.BeatmapFile, name);
    private static Lane Skin => new(LaneSource.Skin, "soft-hitclap");
    private static Lane Base => File("soft-hitclap.wav");
    private static Lane Two => File("soft-hitclap2.wav");
    private static Lane Three => File("soft-hitclap3.wav");
    private static (ArrangeModel Model, MapFolder Folder) Setup()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Soft\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n"));
        var folder = new MapFolder(["soft-hitclap.wav", "soft-hitclap2.wav", "soft-hitclap3.wav", "soft-hitwhistle2.wav", "normal-hitnormal9.wav"]);
        var session = new EditSession(file, folder, Decompiler.Decompile(file, folder));
        var model = new ArrangeModel(session, file, new HashSet<Lane>());
        session.Changed += model.RebuildNotes;
        session.SetHsMode(true);
        return (model, folder);
    }

    [Fact]
    public void Preset_default_backing_keeps_the_requested_extra_slot_and_starts_without_hits()
    {
        var (model, _) = Setup(); var before = model.Session.State.Hits;
        var backing = File("soft-hitclap27.wav"); var slot = File("soft-hitclap4.wav");
        model.AddLane(backing, slot);
        Assert.Equal(slot, model.SlotOf(backing));
        Assert.Equal(4, model.LaneRows.Single(r => r.Lane == backing).Index);
        Assert.Equal("soft-hitclap4.wav", model.NameOf(backing));
        Assert.Equal(before, model.Session.State.Hits);
        var (names, order, added, hidden) = model.LaneLayout();
        var (reopened, _) = Setup();
        reopened.ApplyLaneLayout(new ProjectData { LaneNames = names, LaneOrder = order, AddedLanes = added, HiddenLanes = hidden, LaneSlots = model.SlotLayout() }, _ => true);
        Assert.Equal(slot, reopened.SlotOf(backing));
        Assert.Equal("soft-hitclap5.wav", reopened.NextSlot(backing, new MapFolder([])).Name);
    }

    [Fact]
    public void File_index_one_is_the_only_family_parent_and_skin_zero_remains_a_distinct_child()
    {
        var (model, _) = Setup();
        Assert.True(model.IsFamilyParent(Base)); Assert.False(model.IsFamilyParent(Skin));
        Assert.Equal(2, model.DepthOf(Base));
        Assert.Equal(3, model.DepthOf(Skin)); Assert.Equal(3, model.DepthOf(Two));
        Assert.Equal(1, model.DepthOf(new Lane(LaneSource.Skin, "soft-hitnormal")));
        model.ToggleFamily(Base);
        Assert.Equal(new[] { Base }, model.Rows.OfType<LaneRow>().Where(r => model.FamilyKey(r.Lane) == "Soft:hitclap").Select(r => r.Lane));
        Assert.Equal(new[] { Skin, Two, Three }, model.CollapsedChildren(Base).Select(r => r.Lane));
    }

    [Fact]
    public void Folder_and_family_summaries_reveal_the_source_without_changing_hits_or_volume()
    {
        var (model, _) = Setup(); var s = model.Session;
        var hit = s.HitAt(Two, 1000); s.Add([hit]); s.SetVolume([hit.Anchor!.Value], 35);
        var state = s.State;
        model.ToggleFamily(Base); model.ToggleCollapsed(LaneGroup.SoftSet);
        var view = new ArrangeView(); view.SetModel(model); view.Axis.Left = 0; view.Axis.Zoom = 1;
        var folder = model.Rows.OfType<GroupRow>().Single(r => r.Group == LaneGroup.SoftSet);
        Assert.Equal(Two, view.SummaryLaneUnder(folder, 1000));
        Assert.Contains(model.FolderChildren(LaneGroup.SoftSet), r => r.Lane == Two);
        model.RevealLane(Two);
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == Two);
        Assert.Equal(Two, model.ActiveLane); Assert.Same(state, s.State);
        Assert.Equal(35, s.VolumeOf(hit));
    }

    [Fact]
    public void Filtering_a_closed_folder_shows_parent_context_and_matching_children_without_duplicate_ghosts()
    {
        var (model, _) = Setup(); model.ToggleFamily(Base); model.ToggleCollapsed(LaneGroup.SoftSet);
        model.SetFilter("hitclap2");
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == Base);
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == Two);
        Assert.Empty(model.CollapsedChildren(Base)); Assert.Empty(model.FolderChildren(LaneGroup.SoftSet));
        model.SetFilter(""); Assert.NotEmpty(model.FolderChildren(LaneGroup.SoftSet));
    }

    [Fact]
    public void Dragging_between_types_moves_the_whole_family_and_layers_reorder_within_it()
    {
        var (model, _) = Setup(); var s = model.Session;
        var before = model.LaneRows.Select(r => r.Lane).ToList(); var stateHits = s.State.Hits;
        var whistle = File("soft-hitwhistle2.wav");
        model.MoveLaneTo(Two, whistle, false);
        var order = model.LaneRows.Select(r => r.Lane).ToList();
        Assert.Equal(new[] { Base, Skin, Two, Three }, order.Skip(order.IndexOf(Base)).Take(4));
        Assert.True(order.IndexOf(Three) < order.IndexOf(whistle));
        model.MoveLaneTo(Three, Two, false);
        order = model.LaneRows.Select(r => r.Lane).ToList(); Assert.True(order.IndexOf(Three) < order.IndexOf(Two));
        Assert.Same(stateHits, s.State.Hits); Assert.False(s.IsEdited);
        s.Undo(); s.Undo(); Assert.Equal(before, model.LaneRows.Select(r => r.Lane));
        s.Redo(); s.Redo(); Assert.True(model.LaneRows.ToList().FindIndex(r => r.Lane == Three) < model.LaneRows.ToList().FindIndex(r => r.Lane == Two));
    }

    [Fact]
    public void Family_parents_and_hitnormal_stay_pinned()
    {
        var (model, _) = Setup(); int before = model.Session.StepCount;
        model.MoveLaneTo(Base, Two, true);
        model.MoveLaneTo(Two, Base, false);
        model.MoveLaneTo(Base, new Lane(LaneSource.Skin, "soft-hitnormal"), false);
        Assert.Equal(before, model.Session.StepCount);
    }

    [Fact]
    public void Audio_replacement_keeps_the_editor_slot_and_volume_through_undo_and_redo()
    {
        var (model, _) = Setup(); var s = model.Session;
        var original = s.HitAt(Three, 1000); s.Add([original]); s.SetVolume([original.Anchor!.Value], 30);
        var replacement = File("soft-hitclap10.wav");
        Assert.True(model.ReplaceSample(Three, replacement, preserveSlot: true));
        Assert.Equal(Three, model.SlotOf(replacement)); Assert.Equal(Three.Name, model.NameOf(replacement));
        Assert.Equal(3, model.LaneRows.Single(r => r.Lane == replacement).Index);
        Assert.Equal(30, s.VolumeOf(s.State.Hits.Single()));
        s.Undo(); Assert.Equal(Three, s.State.Hits.Single().Lane);
        s.Redo(); Assert.Equal(replacement, s.State.Hits.Single().Lane); Assert.Equal(Three, model.SlotOf(replacement));
    }

    [Fact]
    public void Slot_metadata_round_trips_and_backing_indexes_do_not_inflate_the_next_family_index()
    {
        var (model, folder) = Setup(); var backing = File("soft-hitclap10.wav");
        model.ReplaceSample(Three, backing, preserveSlot: true);
        var (names, order, added, hidden) = model.LaneLayout();
        var project = new ProjectData { LaneNames = names, LaneOrder = order, AddedLanes = added, HiddenLanes = hidden, LaneSlots = model.SlotLayout() };
        var (fresh, _) = Setup(); fresh.ApplyLaneLayout(project, _ => true);
        Assert.Equal(Three, fresh.SlotOf(backing)); Assert.Equal(Three.Name, fresh.NameOf(backing));
        Assert.Equal("soft-hitclap4.wav", fresh.NextSlot(backing, folder.With([backing.Name])).Name);
    }

    [Fact]
    public void Adding_a_slot_is_undoable_and_reveals_it_in_its_family()
    {
        var (model, _) = Setup(); var backing = File("soft-hitclap10.wav"); var slot = File("soft-hitclap4.wav");
        model.ToggleFamily(Base); model.ToggleCollapsed(LaneGroup.SoftSet);
        model.AddSlotLane(backing, slot);
        Assert.Equal(backing, model.ActiveLane); Assert.Equal(slot, model.SlotOf(backing));
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == backing);
        model.Session.Undo(); Assert.DoesNotContain(model.Rows.OfType<LaneRow>(), r => r.Lane == backing);
        model.Session.Redo(); Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == backing);
        Assert.False(model.Session.IsEdited);
    }

    [Fact]
    public void Reopening_unsaved_replacement_uses_saved_map_audio_without_a_duplicate_slot()
    {
        var (model, _) = Setup(); model.Session.Add([model.Session.HitAt(Three, 1000)]);
        var replacement = File("soft-hitclap10.wav"); model.ReplaceSample(Three, replacement, preserveSlot: true);
        var (names, order, added, hidden) = model.LaneLayout();
        var (fresh, _) = Setup(); fresh.Session.Add([fresh.Session.HitAt(Three, 1000)]);
        fresh.ApplyLaneLayout(new ProjectData { LaneNames = names, LaneOrder = order, AddedLanes = added, HiddenLanes = hidden, LaneSlots = model.SlotLayout(), ActiveLane = ProjectStore.KeyOf(replacement) }, _ => true);
        Assert.Equal(new[] { Three }, fresh.Rows.OfType<LaneRow>().Where(r => fresh.SlotOf(r.Lane) == Three).Select(r => r.Lane));
        Assert.Equal(Three, fresh.Session.State.Hits.Single().Lane);
        Assert.Equal(Three, fresh.ActiveLane);
    }

    [Fact]
    public void Replacing_skin_zero_and_file_one_keeps_two_distinct_slots_and_one_parent()
    {
        var (model, _) = Setup(); var s = model.Session;
        s.Add([s.HitAt(Skin, 1000), s.HitAt(Base, 1000)]);
        var zero = File("soft-hitclap10.wav"); var one = File("soft-hitclap11.wav");
        Assert.True(model.ReplaceSample(Skin, zero, preserveSlot: true));
        Assert.True(model.ReplaceSample(Base, one, preserveSlot: true));
        Assert.Equal(Skin, model.SlotOf(zero)); Assert.Equal(Base, model.SlotOf(one));
        Assert.True(model.IsFamilyParent(one)); Assert.False(model.IsFamilyParent(zero));
        Assert.Equal(2, s.State.Hits.Count); model.ToggleFamily(one);
        Assert.Contains(model.CollapsedChildren(one), r => r.Lane == zero);
        s.Undo(); Assert.True(model.IsFamilyParent(Base)); Assert.Equal(Skin, model.SlotOf(zero));
    }

    [Fact]
    public void Old_interleaved_orders_are_normalized_and_invalid_optional_slot_metadata_is_ignored()
    {
        var (model, _) = Setup(); var whistle = File("soft-hitwhistle2.wav");
        model.ApplyLaneLayout(new ProjectData { LaneOrder = [ProjectStore.KeyOf(Two), ProjectStore.KeyOf(whistle), ProjectStore.KeyOf(Three)],
            LaneSlots = new() { [ProjectStore.KeyOf(Two)] = null!, [ProjectStore.KeyOf(Three)] = "BeatmapFile|../../secret.wav" } }, _ => true);
        var rows = model.LaneRows.Select(r => r.Lane).ToList();
        Assert.Equal(4, rows.Skip(rows.IndexOf(Base)).TakeWhile(l => model.FamilyKey(l) == "Soft:hitclap").Count());
        Assert.Equal(Two, model.SlotOf(Two)); Assert.Equal(Three, model.SlotOf(Three));
    }
}

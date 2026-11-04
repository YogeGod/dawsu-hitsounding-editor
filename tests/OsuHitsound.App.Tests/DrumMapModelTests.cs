using System.Collections.Immutable;
using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class DrumMapModelTests
{
    private static ArrangeModel Model()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Drum\n[TimingPoints]\n0,500,4,3,0,70,1,0\n[HitObjects]\n256,192,1000,1,8,3:2:0:0:\n"));
        var folder = new MapFolder([]); var session = new EditSession(file, folder, Decompiler.Decompile(file, folder));
        return new(session, file, new HashSet<Lane>());
    }
    private static DrumMapPreset Kit(ArrangeModel model)
    {
        var kick = model.LaneRows.First(r => r.Lane.Name == "drum-hitnormal").Lane;
        return new(Guid.NewGuid(), "Kit", [new(Guid.NewGuid(), "Kick", ProjectStore.KeyOf(kick), ProjectStore.KeyOf(kick)), new(Guid.NewGuid(), "Subkick", ProjectStore.KeyOf(kick), ProjectStore.KeyOf(kick))]);
    }
    [Fact]
    public void Undoing_an_extra_lane_hides_it_in_the_flat_view_and_redo_restores_it()
    {
        var model = Model(); model.SetDrumMapMode(true);
        var lane = new Lane(LaneSource.BeatmapFile, "drum-hitnormal2.wav");
        model.AddSlotLane(lane, lane);
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == lane);
        model.Session.Undo();
        Assert.DoesNotContain(model.Rows.OfType<LaneRow>(), r => r.Lane == lane);
        model.Session.Redo();
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == lane);
    }

    [Fact]
    public void Reordering_other_samples_accepts_long_sample_filenames()
    {
        var model = Model(); var sample = new Lane(LaneSource.BeatmapFile, new string('a', 160) + ".wav");
        model.AddLane(sample); model.SetDrumMapMode(true);
        var source = model.Rows.OfType<LaneRow>().Single(r => r.Lane == sample);
        model.MoveDrumRow(source, model.Rows[0], false);
        model.DrumMaps.Validate();
        var row = Assert.IsType<LaneRow>(model.Rows[0]);
        Assert.Equal(sample, row.Lane); Assert.Equal(128, row.InstrumentName!.Length);
    }

    [Fact]
    public void Replacing_a_sample_keeps_all_aliases_linked_and_undoes_with_notes()
    {
        var model = Model(); var kit = Kit(model); model.SetDrumPreset(kit); model.SetDrumMapMode(true);
        var original = ((LaneRow)model.Rows[0]).Lane;
        var replacement = new Lane(LaneSource.BeatmapFile, "kick.wav");
        var before = model.Session.State.Hits;
        int changes = 0; model.DrumMapsChanged += () => changes++;
        Assert.True(model.ReplaceSample(original, replacement, preserveSlot: true));
        Assert.All(model.ActiveDrumMap!.Rows, r => Assert.Equal(ProjectStore.KeyOf(replacement), r.LaneKey));
        Assert.All(model.Rows.OfType<LaneRow>().Take(2), r => Assert.Equal(replacement, r.Lane));
        Assert.Contains(model.Session.State.Hits, h => h.Lane == replacement);
        Assert.DoesNotContain(model.Session.State.Hits, h => h.Lane == original);
        Assert.Equal(original, model.SlotOf(replacement));
        model.Session.Undo();
        Assert.Equal(kit, model.ActiveDrumMap); Assert.Equal(before, model.Session.State.Hits);
        model.Session.Redo();
        Assert.All(model.ActiveDrumMap!.Rows, r => Assert.Equal(ProjectStore.KeyOf(replacement), r.LaneKey));
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Switching_preserves_notes_regular_names_order_and_collapsed_families()
    {
        var model = Model(); var lane = model.LaneRows[0].Lane;
        model.RenameLane(lane, "Regular name"); model.ToggleCollapsed(model.Rows[0].Group);
        var regular = model.Rows.ToList(); var hits = model.Session.State.Hits;
        var kit = Kit(model); model.SetDrumPreset(kit); model.SetDrumMapMode(true);
        Assert.DoesNotContain(model.Rows, r => r is GroupRow);
        Assert.Equal("Kick", ((LaneRow)model.Rows[0]).InstrumentName);
        Assert.Equal("Kick", model.DrumRowName((LaneRow)model.Rows[0]));
        Assert.Equal("Subkick", ((LaneRow)model.Rows[1]).InstrumentName);
        Assert.Same(hits, model.Session.State.Hits);
        model.SetDrumMapMode(false);
        Assert.Equal(regular, model.Rows); Assert.Equal("Regular name", model.NameOf(lane));
    }
    [Fact]
    public void Aliases_do_not_duplicate_edit_selection_or_copy_paste()
    {
        var model = Model(); var kit = Kit(model); model.SetDrumPreset(kit); model.SetDrumMapMode(true);
        var editor = new HitEditor { Model = model };
        var rows = model.Rows.OfType<LaneRow>().Take(2).ToList();
        editor.Select(rows.SelectMany(r => model.Session.State.Hits.Where(h => h.Lane == r.Lane)));
        Assert.Single(editor.Selected); editor.Copy(); editor.Paste(2000);
        Assert.Single(editor.Selected); Assert.Single(model.Session.State.Hits, h => h.Time == 2000);
    }
    [Fact]
    public void Instrument_rows_can_cross_families_without_reordering_sample_view()
    {
        var model = Model(); var original = model.Rows.ToList();
        model.SetDrumMapMode(true);
        var originalEntries = model.ActiveDrumMap!.Rows;
        var source = model.Rows.OfType<LaneRow>().First(r => r.Lane.Name == "drum-hitnormal");
        var target = model.Rows.OfType<LaneRow>().First(r => r.Lane.Name == "soft-hitclap");
        model.MoveDrumRow(source, target, false);
        int at = model.Rows.ToList().FindIndex(r => r is LaneRow l && l.Lane == source.Lane);
        Assert.Equal(target.Lane, ((LaneRow)model.Rows[at + 1]).Lane);
        model.Session.Undo(); Assert.Equal(originalEntries, model.ActiveDrumMap!.Rows);
        model.SetDrumMapMode(false); Assert.Equal(original, model.Rows);
    }
    [Fact]
    public void Missing_assignments_are_explicit_and_never_create_fake_note_lanes()
    {
        var model = Model(); var preset = DrumMapPreset.Empty("Missing") with { Rows = [new(Guid.NewGuid(), "Crash", "BeatmapFile|soft-hitfinish77.wav", "BeatmapFile|soft-hitfinish77.wav")] };
        model.SetDrumPreset(preset); model.SetDrumMapMode(true);
        Assert.IsType<UnassignedDrumRow>(model.Rows[0]);
        Assert.DoesNotContain(model.LaneRows, r => r.Lane.Name == "soft-hitfinish77.wav");
    }
    [Fact]
    public void Filtering_matches_instrument_names_and_other_samples_remain_accessible()
    {
        var model = Model(); var kit = Kit(model); model.SetDrumPreset(kit); model.SetDrumMapMode(true);
        Assert.Equal(model.LaneRows.Count + 1, model.Rows.Count);
        model.SetFilter("Subkick"); var row = Assert.IsType<LaneRow>(Assert.Single(model.Rows)); Assert.Equal("Subkick", row.InstrumentName);
        model.SetFilter(""); Assert.Equal(model.LaneRows.Count + 1, model.Rows.Count);
    }
    [Fact]
    public void Keyboard_lane_move_uses_the_active_alias_row()
    {
        var model = Model(); model.SetDrumPreset(Kit(model)); model.SetDrumMapMode(true);
        var alias = (LaneRow)model.Rows[1]; model.SetActiveRow(alias);
        Assert.Equal(((LaneRow)model.Rows[2]).Lane, HitEditor.ShiftLane(model, alias.Lane, 1));
    }

    [Fact]
    public void Drag_mapping_uses_the_grabbed_alias_instead_of_its_first_mirror()
    {
        var model = Model(); var kit = Kit(model); model.SetDrumPreset(kit); model.SetDrumMapMode(true);
        var lane = ((LaneRow)model.Rows[1]).Lane;
        Assert.Equal(((LaneRow)model.Rows[2]).Lane, model.ShiftDrumLane(lane, 1, 1));
        Assert.Equal(lane, model.ShiftDrumLane(lane, -1, 1));
    }
}

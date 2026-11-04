using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class LongSnapTests
{
    private static (ArrangeModel Model, OsuFile File) Load(string timing = "0,500,4,2,0,100,1,0")
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes($"osu file format v14\n[General]\nSampleSet:Soft\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n{timing}\n[HitObjects]\n"));
        var folder = new MapFolder([]);
        return (new ArrangeModel(new EditSession(file, folder, Decompiler.Decompile(file, folder)), file, new HashSet<Lane>()), file);
    }

    [Theory]
    [InlineData(2, 1000)]
    [InlineData(3, 1500)]
    [InlineData(4, 2000)]
    public void Long_snap_uses_map_beats_and_leaves_input_map_unchanged(int beats, double expected)
    {
        var (model, file) = Load();
        var before = file.ToBytes();
        var spacing = new SnapSpacing(beats, 1);
        Assert.Equal(expected, model.Snap(expected + 30, spacing));
        Assert.Equal(new[] { 0.0, expected, expected * 2 }, model.LongSnapLines(0, expected * 2, spacing));
        Assert.Equal(before, file.ToBytes());
    }

    [Fact]
    public void Three_beat_grid_anchors_to_actual_red_line_even_before_first_red_line()
    {
        var (model, _) = Load("1000,500,4,2,0,100,1,0");
        var snap = new SnapSpacing(3, 1);
        Assert.Equal(1000, model.Snap(900, snap));
        Assert.Equal(-500, model.Snap(-450, snap));
        Assert.Equal(new[] { 1000.0, 2500, 4000 }, model.LongSnapLines(0, 4200, snap));
        Assert.Equal(1000, model.StepsAfter(-450, 1, snap));
    }

    [Fact]
    public void Negative_red_offsets_do_not_create_a_phantom_boundary_at_zero()
    {
        var (model, _) = Load("-1000,500,4,2,0,100,1,0");
        var snap = new SnapSpacing(3, 1);
        Assert.Equal(2000, model.Snap(1800, snap));
        Assert.Equal(500, model.StepsAfter(-1000, 1, snap));
        Assert.Equal(new[] { 500.0, 2000, 3500 }, model.LongSnapLines(0, 4000, snap));
    }

    [Fact]
    public void To_grid_nudges_stop_at_red_boundary_in_both_directions()
    {
        var (model, _) = Load("0,500,4,2,0,100,1,0\n2200,400,4,2,0,100,1,0");
        var editor = new HitEditor { Model = model, Spacing = new(3, 1) };
        model.Session.Add([model.Session.HitAt(new(LaneSource.Skin, "soft-hitclap"), 1500)]);
        editor.Select(model.Session.State.Hits);
        editor.Nudge(1, false, true);
        Assert.Equal(2200, Assert.Single(editor.Selected).Time);
        editor.Nudge(-1, false, true);
        Assert.Equal(1500, Assert.Single(editor.Selected).Time);
        Assert.Equal(2200, model.AdjacentLongSnap(2250, -1, editor.Spacing));
    }

    [Fact]
    public void Green_lines_and_signature_changes_keep_the_same_long_grid()
    {
        var (model, _) = Load("0,500,4,2,0,100,1,0\n2250,-50,4,2,2,50,0,0");
        model.SetTimeSignatures([new(2, 7, 8), new(3, 4, 4)]);
        var snap = new SnapSpacing(3, 1);
        Assert.Equal(new[] { 0.0, 1500, 3000, 4500, 6000 }, model.LongSnapLines(0, 6000, snap));
        Assert.Equal(4500, model.Snap(4420, snap));
        Assert.Equal(4500, model.StepsAfter(3000, 1, snap));
    }

    [Fact]
    public void New_red_line_is_a_grid_boundary_and_uses_new_bpm()
    {
        var (model, _) = Load("0,500,4,2,0,100,1,0\n2200,400,4,2,0,100,1,0");
        var snap = new SnapSpacing(3, 1);
        Assert.Equal(new[] { 0.0, 1500, 2200, 3400, 4600 }, model.LongSnapLines(0, 4600, snap));
        Assert.Equal(2200, model.Snap(1900, snap)); // nearer red line, not old grid at 1500/3000
        Assert.Equal(2200, model.Snap(2300, snap));
        Assert.Equal(2200, model.StepsAfter(1500, 1, snap));
        Assert.Equal(3400, model.StepsAfter(1500, 2, snap));
    }

    [Fact]
    public void Early_red_line_can_be_nearer_than_a_rounded_old_grid_position()
    {
        var (model, _) = Load("0,500,4,2,0,100,1,0\n600,400,4,2,0,100,1,0");
        Assert.Equal(600, model.Snap(400, new SnapSpacing(3, 1)));
    }

    [Fact]
    public void Wheel_cycles_long_steps_without_changing_existing_fraction_order()
    {
        Assert.Equal(new SnapSpacing(2, 1), MainWindow.NextWheelSnap(new(4, 1), 1, false));
        Assert.Equal(new SnapSpacing(1, 1), MainWindow.NextWheelSnap(new(2, 1), 1, false));
        Assert.Equal(new SnapSpacing(2, 1), MainWindow.NextWheelSnap(new(1, 1), -1, false));
        Assert.Equal(new SnapSpacing(3, 1), MainWindow.NextWheelSnap(new(1, 1), -1, true));
        Assert.Equal(new SnapSpacing(1, 3), MainWindow.NextWheelSnap(new(1, 1), 1, true));
        Assert.Equal(new SnapSpacing(1, 4), MainWindow.NextWheelSnap(new(1, 3), 1, false));
        Assert.Equal(new SnapSpacing(4, 1), MainWindow.NextWheelSnap(new(4, 1), -1, false));
        Assert.Equal(new SnapSpacing(1, 16), MainWindow.NextWheelSnap(new(1, 16), 1, false));
    }

    [Fact]
    public void Editing_uses_long_snap_for_paste_quantize_nudge_duplicate_and_relative_drag()
    {
        var (model, _) = Load();
        var editor = new HitEditor { Model = model, Spacing = new(3, 1) };
        var lane = new Lane(LaneSource.Skin, "soft-hitclap");
        model.Session.Add([model.Session.HitAt(lane, 1600)]);
        editor.Select(model.Session.State.Hits);
        Assert.Equal(1600, Assert.Single(editor.Selected).Time);
        Assert.Equal(1, editor.Quantize(100));
        Assert.Equal(1500, Assert.Single(editor.Selected).Time);
        editor.Copy(); editor.Paste(3040);
        Assert.Equal(3000, Assert.Single(editor.Selected).Time);
        editor.Nudge(1, false, false);
        Assert.Equal(4500, Assert.Single(editor.Selected).Time);
        editor.Duplicate();
        Assert.Equal(6000, Assert.Single(editor.Selected).Time);
        editor.SnapMode = SnapMode.GridRelative;
        Assert.Equal(1500, editor.SnapOffset(1600, 1480));
        editor.SnapOn = false;
        Assert.Equal(1733, editor.SnapTime(1733));
    }

    [Fact]
    public void Repeat_and_duplicate_land_on_red_line_then_continue_with_one_undo_step()
    {
        var (model, _) = Load("0,500,4,2,0,100,1,0\n2200,400,4,2,0,100,1,0");
        var editor = new HitEditor { Model = model, Spacing = new(3, 1) };
        model.Session.Add([model.Session.HitAt(new(LaneSource.Skin, "soft-hitclap"), 1500)]);
        editor.Select(model.Session.State.Hits);
        editor.Repeat(3, 1);
        Assert.Equal(new[] { 2200.0, 3400, 4600 }, editor.Selected.Select(h => h.Time).Order());
        model.Session.Undo();
        Assert.Single(model.Session.State.Hits);
        editor.Select(model.Session.State.Hits);
        editor.Duplicate();
        Assert.Equal(2200, Assert.Single(editor.Selected).Time);
    }
}

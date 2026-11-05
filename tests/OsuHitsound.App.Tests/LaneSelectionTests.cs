using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class LaneSelectionTests
{
    private static ArrangeModel Model()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Soft\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n256,192,1000,1,14,0:0:0:100:\n"));
        var folder = new MapFolder([]);
        return new(new EditSession(file, folder, Decompiler.Decompile(file, folder)), file, new HashSet<Lane>());
    }
    private static Lane[] Visible(ArrangeModel model) => model.Rows.OfType<LaneRow>().Select(r => r.Lane).Distinct().ToArray();

    [Fact]
    public void Plain_and_control_selection_do_not_edit_hits_or_undo_history()
    {
        var model = Model(); var lanes = Visible(model); var state = model.Session.State;
        model.SelectLane(lanes[0]); model.SelectLane(lanes[1], toggle: true);
        Assert.Equal(2, model.SelectedLanes.Count);
        model.SelectLane(lanes[0], toggle: true);
        Assert.Equal(lanes[1], Assert.Single(model.SelectedLanes));
        model.SelectLane(lanes[2]);
        Assert.Equal(lanes[2], Assert.Single(model.SelectedLanes));
        Assert.Same(state, model.Session.State);
        Assert.False(model.Session.IsEdited);
    }

    [Fact]
    public void Shift_range_can_grow_shrink_and_reverse_from_its_anchor()
    {
        var model = Model(); var lanes = Visible(model);
        model.SelectLane(lanes[0]); model.SelectLane(lanes[3], extend: true);
        Assert.True(model.SelectedLanes.SetEquals(lanes.Take(4)));
        model.SelectLane(lanes[1], extend: true);
        Assert.True(model.SelectedLanes.SetEquals(lanes.Take(2)));
        model.SelectLane(lanes[3]); model.SelectLane(lanes[0], extend: true);
        Assert.True(model.SelectedLanes.SetEquals(lanes.Take(4)));
    }

    [Fact]
    public void Control_shift_adds_a_range_and_hidden_lanes_are_not_in_new_ranges()
    {
        var model = Model(); var lanes = Visible(model);
        model.SelectLane(lanes[0]); model.SelectLane(lanes[6], toggle: true); model.SelectLane(lanes[4], extend: true, toggle: true);
        Assert.True(model.SelectedLanes.SetEquals(new[] { lanes[0], lanes[4], lanes[5], lanes[6] }));
        model.HideLane(lanes[1]);
        model.SelectLane(lanes[0]); model.SelectLane(lanes[3], extend: true);
        Assert.True(model.SelectedLanes.SetEquals(new[] { lanes[0], lanes[2], lanes[3] }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selected_group_follows_the_clicked_mute_or_solo_state_even_when_mixed(bool solo)
    {
        var model = Model(); var lanes = Visible(model);
        void Toggle(Lane lane) { if (solo) model.ToggleSolo(lane); else model.ToggleMute(lane); }
        bool On(Lane lane) => solo ? model.IsSoloed(lane) : model.IsMuted(lane);
        Toggle(lanes[0]);
        model.SelectLane(lanes[0]); model.SelectLane(lanes[1], toggle: true);
        Toggle(lanes[0]);
        Assert.False(On(lanes[0])); Assert.False(On(lanes[1]));
        Toggle(lanes[1]);
        Assert.True(On(lanes[0])); Assert.True(On(lanes[1])); Assert.False(On(lanes[2]));
        model.SelectLane(lanes[2]); Toggle(lanes[2]);
        Assert.True(On(lanes[0])); Assert.True(On(lanes[1])); Assert.True(On(lanes[2]));
        Toggle(lanes[2]); Assert.False(On(lanes[2])); Assert.True(On(lanes[0]));
    }

    [Fact]
    public void Deselect_ends_group_behavior_without_resetting_mixer_states()
    {
        var model = Model(); var lanes = Visible(model);
        model.SelectLane(lanes[0]); model.SelectLane(lanes[1], toggle: true); model.ToggleSolo(lanes[0]);
        model.DeselectLanes(); model.ToggleSolo(lanes[0]);
        Assert.Empty(model.SelectedLanes); Assert.False(model.IsSoloed(lanes[0])); Assert.True(model.IsSoloed(lanes[1]));
    }

    [Fact]
    public void A_hidden_selected_lane_leaves_the_group_and_save_view_restore_retains_remaining_selection()
    {
        var model = Model(); var lanes = Visible(model);
        model.SelectLane(lanes[0]); model.SelectLane(lanes[1], toggle: true);
        model.HideLane(lanes[1]); Assert.Equal(lanes[0], Assert.Single(model.SelectedLanes));
        var view = model.CaptureViewState(); model.DeselectLanes(); model.RestoreViewState(view);
        Assert.Equal(lanes[0], Assert.Single(model.SelectedLanes));
    }
}

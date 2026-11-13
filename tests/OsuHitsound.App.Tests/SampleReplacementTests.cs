using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class SampleReplacementTests
{
    private static ArrangeModel Model(string objects = "256,192,1000,1,0,0:0:0:0:\n256,192,1000,1,0,0:0:0:0:\n")
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet: Soft\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n" + objects));
        var folder = new MapFolder([]);
        return new(new EditSession(file, folder, Decompiler.Decompile(file, folder)), file, new HashSet<Lane>());
    }

    [Fact]
    public void Replacement_preserves_stacked_anchors_unanchored_hits_and_volumes_through_history()
    {
        var model = Model();
        var session = model.Session;
        var original = session.State.Hits[0].Lane;
        var replacement = new Lane(LaneSource.BeatmapFile, "soft-hitnormal2.wav");
        session.Add([session.HitAt(original, 1800)]);
        session.SetVolume([session.Moments[0].Key], 35);
        var before = session.State;
        model.ToggleSolo(original);
        model.SetActive(original);
        Assert.True(model.ReplaceSample(original, replacement));
        Assert.Equal(before.Hits.Select(h => h with { Lane = replacement }), session.State.Hits);
        Assert.Equal(before.Volumes, session.State.Volumes);
        Assert.True(model.IsAudible(replacement));
        session.Add([session.HitAt(replacement, 2200)]);
        session.Undo(); // undo a later edit before undoing the replacement
        session.Undo();
        Assert.Equal(before.Hits, session.State.Hits);
        Assert.True(model.IsAudible(original));
        Assert.False(model.IsSoloed(replacement));
        Assert.Equal(original, model.ActiveLane);
        session.Redo();
        Assert.Equal(before.Hits.Select(h => h with { Lane = replacement }), session.State.Hits);
        Assert.True(model.IsAudible(replacement));
        Assert.False(model.IsSoloed(original));
    }

    [Fact]
    public void Replacement_restores_both_existing_lanes_preview_state_and_supports_history_jumps()
    {
        var model = Model();
        var original = model.Session.State.Hits[0].Lane;
        var replacement = new Lane(LaneSource.Skin, "drum-hitclap");
        model.ToggleMute(original);
        model.ToggleSolo(original);
        model.ToggleMute(replacement);
        model.RenameLane(replacement, "Existing clap");
        Assert.True(model.ReplaceSample(original, replacement));
        model.Session.GoTo(0);
        Assert.True(model.IsMuted(original));
        Assert.True(model.IsMuted(replacement));
        Assert.True(model.IsSoloed(original));
        Assert.False(model.IsSoloed(replacement));
        Assert.Equal("Existing clap", model.NameOf(replacement));
        model.Session.GoTo(1);
        Assert.True(model.IsSoloed(replacement));
        Assert.False(model.IsSoloed(original));
        model.Session.GoTo(0);
        model.Session.Add([model.Session.HitAt(original, 2500)]);
        Assert.False(model.Session.CanRedo);
        Assert.True(model.IsSoloed(original));
    }

    [Fact]
    public void Read_only_slider_destinations_are_rejected_without_changing_notes_or_history()
    {
        var model = Model("256,192,1000,2,2,B|356:192,1,560\n");
        var original = model.Session.State.Hits.First(h => h.Lane.Sample?.Sound == "hitnormal").Lane;
        var before = model.Session.State;
        foreach (var lane in model.Map.Passive.Select(p => p.Lane).Distinct())
        {
            if (lane.Source == LaneSource.Skin) Assert.False(ArrangeModel.CanChooseChannelSample(lane));
            Assert.False(model.ReplaceSample(original, lane));
        }
        Assert.Same(before, model.Session.State);
        Assert.False(model.Session.CanUndo);
    }

    [Fact]
    public void View_state_survives_reloading_and_empty_lane_replacement_is_undoable()
    {
        var model = Model();
        var lane = new Lane(LaneSource.Skin, "soft-hitclap");
        model.ToggleMute(lane); model.ToggleSolo(lane); model.ToggleSongSolo();
        model.ToggleCollapsed(LaneGroup.SoftSet); model.SetFilter("clap");
        var fresh = Model();
        fresh.RestoreViewState(model.CaptureViewState());
        Assert.True(fresh.IsMuted(lane)); Assert.True(fresh.IsSoloed(lane));
        Assert.True(fresh.SongSoloed); Assert.True(fresh.IsCollapsed(LaneGroup.SoftSet));
        Assert.Equal("clap", fresh.Filter);
        var replacement = new Lane(LaneSource.BeatmapFile, "soft-hitclap2.wav");
        Assert.True(model.ReplaceSample(lane, replacement));
        model.Session.Undo();
        Assert.True(model.IsSoloed(lane)); Assert.False(model.IsSoloed(replacement));
    }
}

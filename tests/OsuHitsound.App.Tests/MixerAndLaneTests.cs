using System.Text;
using OsuHitsound.App;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class MixerAndLaneTests
{
    [Fact]
    public void Mixing_sums_layers_at_their_gains_into_a_16_bit_wav()
    {
        var a = new float[SampleMixer.Rate * 2]; // 1 s stereo
        var b = new float[100];
        a[0] = 0.5f;
        b[0] = 0.5f;
        a[2] = 0.9f;
        b[2] = 0.9f; // clips

        var wav = SampleMixer.Mix([(a, 1f), (b, 0.5f)]);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(44 + a.Length * 2, wav.Length);
        Assert.Equal((short)Math.Round(0.75 * short.MaxValue), BitConverter.ToInt16(wav, 44));
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(wav, 48));
    }

    [Fact]
    public void A_mix_is_never_shorter_than_25_ms()
    {
        var wav = SampleMixer.Mix([(new float[10], 1f)]);
        Assert.True((wav.Length - 44) / 4 >= SampleMixer.Rate / 40);
    }

    [Fact]
    public void Mono_samples_become_stereo_at_44_1_khz()
    {
        var mono = new float[] { 0, 1, 0, -1 };
        var stereo = SampleMixer.ToStereo(mono, channels: 1, rate: 22050);
        Assert.Equal(16, stereo.Length); // 4 frames at half the rate → 8 frames × 2 channels
        Assert.Equal(stereo[2], stereo[3]); // both channels the same
        Assert.Equal(1f, stereo[4]);
    }

    private static ArrangeModel Model()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(
            "osu file format v14\r\n\r\n[General]\r\nSampleSet: Soft\r\n\r\n[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n256,192,1000,1,8,0:0:0:0:\r\n"));
        var folder = new MapFolder(["soft-hitclap2.wav", "boom.wav"]);
        return new ArrangeModel(new EditSession(file, folder, Decompiler.Decompile(file, folder)), file, new HashSet<Lane>());
    }

    [Fact]
    public void Lanes_can_be_renamed_moved_added_hidden_and_filtered()
    {
        var model = Model();
        var skin = model.LaneRows.Where(r => r.Group == LaneGroup.SoftSet && r.Lane.Source == LaneSource.Skin).Select(r => r.Lane).ToList();

        model.RenameLane(skin[0], "Big clap");
        Assert.Equal("Big clap", model.NameOf(skin[0]));

        model.MoveLane(skin[1], -1);
        Assert.Equal(skin[1], model.LaneRows.First(r => r.Group == LaneGroup.SoftSet).Lane);

        var boom = new Lane(LaneSource.BeatmapFile, "boom.wav");
        model.AddLane(boom);
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == boom);

        model.HideLane(boom);
        Assert.DoesNotContain(model.Rows.OfType<LaneRow>(), r => r.Lane == boom);

        model.SetFilter("clap2");
        Assert.Equal(["soft-hitclap2.wav"], model.Rows.OfType<LaneRow>().Select(r => r.Lane.Name));
        Assert.Equal(Enumerable.Range(1, model.LaneRows.Count), model.LaneRows.Select(r => r.Number));
    }

    [Fact]
    public void The_lane_layout_round_trips_through_a_project()
    {
        var model = Model();
        var lane = model.LaneRows[1].Lane;
        model.RenameLane(lane, "renamed");
        model.MoveLane(lane, -1);
        var (names, order, added, hidden) = model.LaneLayout();

        var fresh = Model();
        fresh.ApplyLaneLayout(new ProjectData { LaneNames = names, LaneOrder = order, AddedLanes = added, HiddenLanes = hidden }, _ => true);

        Assert.Equal("renamed", fresh.NameOf(lane));
        Assert.Equal(model.LaneRows.Select(r => r.Lane), fresh.LaneRows.Select(r => r.Lane));
    }

    [Fact]
    public void Row_zoom_keeps_the_point_under_the_mouse_even_past_short_group_rows()
    {
        // A group row (18 px, it doesn't grow), three lanes, another group row, three lanes; rows 22 px → 44 px.
        var model = Model();
        var rows = model.Rows;
        Assert.True(rows.Count >= 4);

        // The mouse 5 px into the fourth row (the content above it: what the old rows add up to), no scroll yet.
        double Height(Row r, double h) => r is GroupRow ? Math.Min(ArrangeView.GroupRowHeight, h) : h;
        double before = rows.Take(3).Sum(r => Height(r, 22)) + 5;
        double scroll = ArrangeView.ScrollKeepingPoint(rows, 22, 44, 0, before);

        // After the zoom the same row is under the mouse, the same fraction into it.
        double after = rows.Take(3).Sum(r => Height(r, 44)) + 5 * Height(rows[3], 44) / Height(rows[3], 22);
        Assert.Equal(after - before, scroll, 6);
    }

    [Fact]
    public void The_active_lane_is_kept_in_the_project_and_cleared_when_deleted()
    {
        var model = Model();
        var lane = model.LaneRows[2].Lane;
        model.SetActive(lane);
        Assert.Equal(lane, model.ActiveLane);

        var fresh = Model();
        fresh.ApplyLaneLayout(new ProjectData { ActiveLane = ProjectStore.KeyOf(lane) }, _ => true);
        Assert.Equal(lane, fresh.ActiveLane);

        model.HideLane(lane);
        Assert.Null(model.ActiveLane);
    }

    [Fact]
    public void A_lane_order_from_a_version_1_project_is_ignored_but_its_names_are_kept()
    {
        var model = Model();
        var lane = model.LaneRows[1].Lane;
        var defaultOrder = model.LaneRows.Select(r => r.Lane).ToList();
        var reversed = defaultOrder.Select(ProjectStore.KeyOf).Reverse().ToList();

        model.ApplyLaneLayout(new ProjectData { Version = 1, LaneOrder = reversed, LaneNames = new() { [ProjectStore.KeyOf(lane)] = "kept" } }, _ => true);

        Assert.Equal(defaultOrder, model.LaneRows.Select(r => r.Lane));
        Assert.Equal("kept", model.NameOf(lane));
    }

    [Fact]
    public void Folder_moves_include_filtered_and_hidden_lanes_and_survive_reopening()
    {
        var model = Model();
        var before = model.LaneRows.Select(r => r.Lane).ToList();
        var soft = model.LaneRows.Where(r => r.Group == LaneGroup.SoftSet).Select(r => r.Lane).ToList();
        model.HideLane(soft[0]);
        model.SetFilter("clap");
        model.ToggleCollapsed(LaneGroup.SoftSet);
        model.MoveFolderTo(LaneGroup.SoftSet, LaneGroup.DrumSet, true);

        Assert.Equal(soft, model.LaneRows.Where(r => r.Group == LaneGroup.SoftSet).Select(r => r.Lane));
        Assert.Equal(LaneGroup.SoftSet, model.LaneRows.Last().Group);
        Assert.Equal(before.OrderBy(l => l.Name), model.LaneRows.Select(r => r.Lane).OrderBy(l => l.Name));
        var (names, order, added, hidden) = model.LaneLayout();
        var fresh = Model();
        fresh.ApplyLaneLayout(new ProjectData { LaneNames = names, LaneOrder = order, AddedLanes = added, HiddenLanes = hidden }, _ => true);
        Assert.Equal(model.LaneRows.Select(r => r.Lane), fresh.LaneRows.Select(r => r.Lane));
    }

    [Fact]
    public void Layout_and_note_edits_share_chronological_undo_and_redo()
    {
        var model = Model();
        var session = model.Session;
        var before = model.LaneRows.Select(r => r.Lane).ToList();
        var hit = session.State.Hits[0];
        model.MoveLaneTo(before[0], before[1], true);
        var reordered = model.LaneRows.Select(r => r.Lane).ToList();
        Assert.False(session.IsEdited); // layout is saved in the project, not the beatmap
        session.Remove([hit]);
        model.MoveFolderTo(LaneGroup.SoftSet, LaneGroup.DrumSet, true);
        Assert.Equal(3, session.Position);

        session.Undo();
        Assert.Equal(reordered, model.LaneRows.Select(r => r.Lane));
        Assert.DoesNotContain(hit, session.State.Hits);
        session.Undo();
        Assert.Contains(hit, session.State.Hits);
        Assert.False(session.IsEdited);
        session.Undo();
        Assert.Equal(before, model.LaneRows.Select(r => r.Lane));
        session.Redo(); session.Redo(); session.Redo();
        Assert.Equal(LaneGroup.SoftSet, model.LaneRows.Last().Group);
        Assert.DoesNotContain(hit, session.State.Hits);
    }

    [Fact]
    public void Invalid_or_unchanged_layout_moves_do_not_add_history_or_change_notes()
    {
        var model = Model();
        var session = model.Session;
        var first = model.LaneRows[0].Lane;
        var second = model.LaneRows[1].Lane;
        var other = model.LaneRows.First(r => r.Group == LaneGroup.DrumSet).Lane;
        var hits = session.State.Hits;
        model.MoveLaneTo(first, first, true);
        model.MoveLaneTo(first, second, false);
        model.MoveLaneTo(first, other, true);
        model.MoveFolderTo(LaneGroup.SoftSet, LaneGroup.SoftSet, true);
        Assert.Equal(0, session.StepCount);
        Assert.Same(hits, session.State.Hits);
        model.MoveLaneTo(first, second, true);
        Assert.Equal(1, session.StepCount);
        Assert.Same(hits, session.State.Hits);
        session.GoTo(0);
        Assert.Equal(first, model.LaneRows[0].Lane);
        session.GoTo(1);
        Assert.Equal(first, model.LaneRows[1].Lane);
    }
}

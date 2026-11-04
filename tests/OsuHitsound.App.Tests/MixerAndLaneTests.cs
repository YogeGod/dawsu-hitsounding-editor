using System.Text;
using OsuHitsound.App;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class MixerAndLaneTests
{
    [Fact]
    public void Export_auditions_replace_previous_voices_and_can_be_stopped()
    {
        var mixer = new PlaybackMixer();
        mixer.Audition(Enumerable.Repeat(0.2f, 100).ToArray());
        mixer.Audition(Enumerable.Repeat(0.3f, 100).ToArray());
        var output = new float[2];
        mixer.Read(output);
        Assert.Equal(0.3f, output[0], 5);
        mixer.Audition(null);
        mixer.Read(output);
        Assert.Equal(0f, output[0]);
    }

    [Fact]
    public void Addition_companion_is_silent_and_never_shorter_than_25_ms()
    {
        var blank = SampleMixer.Mix(new MixFile("soft-hitnormal2.wav", []) { SilenceFor = "mix" }, _ => throw new Exception("No input is needed"));
        Assert.True(blank.Length >= 44 + SampleMixer.Rate * .025 * 4);
        Assert.All(blank.Skip(44), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Mixing_sums_layers_and_preserves_float_headroom_when_the_sum_exceeds_full_scale()
    {
        var a = new float[SampleMixer.Rate * 2]; // 1 s stereo
        var b = new float[100];
        a[0] = 0.5f;
        b[0] = 0.5f;
        a[2] = 0.9f;
        b[2] = 0.9f; // clips

        var wav = SampleMixer.Mix([(a, 1f), (b, 0.5f)]);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(44 + a.Length * 4, wav.Length);
        Assert.Equal(3, BitConverter.ToInt16(wav, 20)); // IEEE float WAV
        Assert.Equal(0.75f, BitConverter.ToSingle(wav, 44));
        Assert.Equal(1.35f, BitConverter.ToSingle(wav, 52), 5);
    }

    [Fact]
    public void A_mix_is_never_shorter_than_25_ms()
    {
        var wav = SampleMixer.Mix([(new float[10], 1f)]);
        Assert.True((wav.Length - 44) / 4.0 / SampleMixer.Rate >= 0.025);
        Assert.Equal(0, (wav.Length - 44) % 4); // every PCM frame has both 16-bit channels
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));
    }

    [Fact]
    public void Required_mix_layers_cannot_be_silently_omitted_and_slider_loops_are_finite()
    {
        var hit = new Lane(LaneSource.BeatmapFile, "hit.wav");
        var slide = new Lane(LaneSource.BeatmapFile, "slide.wav");
        var mix = new MixFile("soft-hitnormal2.wav", [(hit, 100)]) { Loops = [(slide, 50, 100)] };
        var ex = Assert.Throws<IOException>(() => SampleMixer.Mix(mix, lane => lane == hit ? [0.2f, 0.2f] : null));
        Assert.Contains("slide.wav", ex.Message);
        var wav = SampleMixer.Mix(mix, lane => lane == hit ? [0.2f, 0.2f] : [0.5f, 0.5f]);
        Assert.Equal(44 + 4410 * 4, wav.Length);
        Assert.Equal((short)Math.Round(0.45 * short.MaxValue), BitConverter.ToInt16(wav, 44));
        Assert.Equal((short)Math.Round(0.25 * short.MaxValue), BitConverter.ToInt16(wav, 48));
    }

    [Fact]
    public void Official_blank_wav_decodes_as_silence_even_with_trailing_tags()
    {
        byte[] blank = [
            0x52, 0x49, 0x46, 0x46, 0x24, 0, 0, 0, 0x57, 0x41, 0x56, 0x45,
            0x66, 0x6d, 0x74, 0x20, 0x10, 0, 0, 0, 1, 0, 2, 0,
            0x44, 0xac, 0, 0, 0x10, 0xb1, 2, 0, 4, 0, 0x10, 0,
            0x64, 0x61, 0x74, 0x61, 0, 0, 0, 0,
        ];
        Assert.Equal([0f, 0f], SampleMixer.Decode(blank)!);
        Assert.Equal([0f, 0f], SampleMixer.Decode([.. blank, 1, 2, 3])!);
        var lane = new Lane(LaneSource.BeatmapFile, "soft-sliderslide.wav");
        var mix = new MixFile("soft-hitnormal2.wav", []) { Loops = [(lane, 100, 100)] };
        var wav = SampleMixer.Mix(mix, _ => SampleMixer.Decode(blank));
        Assert.Equal(44 + 4410 * 4, wav.Length);
        Assert.All(wav.Skip(44), value => Assert.Equal(0, value));
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
    public void Passive_slider_samples_are_not_offered_as_editable_channel_assignments()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet: Soft\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n256,192,1000,2,2,B|356:192,1,560\n"));
        var folder = new MapFolder([]);
        var session = new EditSession(file, folder, Decompiler.Decompile(file, folder));
        var model = new ArrangeModel(session, file, new HashSet<Lane>());
        Assert.NotEmpty(model.Map.Passive);
        Assert.All(model.Map.Passive, p => Assert.False(model.CanReplaceSample(p.Lane)));
        Assert.True(model.CanReplaceSample(new Lane(LaneSource.Skin, "soft-hitclap")));
    }

    [Fact]
    public void Numbered_sample_families_collapse_without_changing_notes_or_playback_and_search_reveals_matches()
    {
        var model = Model();
        var parent = model.LaneRows.First(r => r.Lane.Name == "soft-hitclap").Lane;
        var child = model.LaneRows.First(r => r.Lane.Name == "soft-hitclap2.wav").Lane;
        model.Session.Add([model.Session.HitAt(child, 1000)]);
        var before = model.Session.State;
        Assert.True(model.IsFamilyParent(parent));
        model.ToggleFamily(parent);
        Assert.DoesNotContain(model.Rows.OfType<LaneRow>(), r => r.Lane == child);
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == parent);
        Assert.Equal(child, Assert.Single(model.CollapsedChildren(parent)).Lane);
        Assert.Same(before, model.Session.State);
        Assert.True(model.IsAudible(child));
        model.SetFilter("hitclap2");
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == child);
        Assert.Empty(model.CollapsedChildren(parent)); // visible search results must not also be projected onto the parent
        model.SetFilter("");
        model.ToggleFamily(parent);
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == child);
        Assert.Empty(model.CollapsedChildren(parent));
        model.Session.Remove(model.Session.State.Hits.Where(h => h.Lane == parent).ToList());
        model.HideLane(parent);
        var extra = new Lane(LaneSource.BeatmapFile, "soft-hitclap3.wav"); model.AddLane(extra);
        model.ToggleFamily(child);
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == child); // replacing/hiding the base must not hide the whole family
        Assert.DoesNotContain(model.Rows.OfType<LaneRow>(), r => r.Lane == extra);
        Assert.Equal(extra, Assert.Single(model.CollapsedChildren(child)).Lane);
    }

    [Fact]
    public void Lanes_can_be_renamed_moved_added_hidden_and_filtered()
    {
        var model = Model();
        var skin = model.LaneRows.Where(r => r.Group == LaneGroup.SoftSet && r.Lane.Source == LaneSource.Skin).Select(r => r.Lane).ToList();

        model.RenameLane(skin[0], "Big clap");
        Assert.Equal("Big clap", model.NameOf(skin[0]));

        model.MoveLane(skin[2], -1);
        Assert.Equal(skin[2], model.LaneRows.Where(r => r.Group == LaneGroup.SoftSet).Skip(1).First().Lane);

        var boom = new Lane(LaneSource.BeatmapFile, "boom.wav");
        model.AddLane(boom);
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Lane == boom);

        model.HideLane(boom);
        Assert.DoesNotContain(model.Rows.OfType<LaneRow>(), r => r.Lane == boom);

        model.SetFilter("clap2");
        Assert.Equal(["soft-hitclap", "soft-hitclap2.wav"], model.Rows.OfType<LaneRow>().Select(r => r.Lane.Name)); // Parent context stays visible.
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
        model.MoveLaneTo(before[2], before[1], false);
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
        var first = model.LaneRows[1].Lane;
        var second = model.LaneRows[2].Lane;
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
        Assert.Equal(first, model.LaneRows[1].Lane);
        session.GoTo(1);
        Assert.Equal(first, model.LaneRows[2].Lane);
    }
}

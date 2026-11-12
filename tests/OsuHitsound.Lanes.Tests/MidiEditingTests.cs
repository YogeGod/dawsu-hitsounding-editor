using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class MidiEditingTests
{
    private static EditSession Session()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Soft\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n256,192,1000,1,0,0:0:0:0:\n"));
        var folder = new MapFolder([]);
        return new EditSession(file, folder, Decompiler.Decompile(file, folder));
    }

    private static readonly Lane Clap = new(LaneSource.Skin, "soft-hitclap");
    private static readonly MidiHitData Quiet = new(0, 2, 40, 50, 3, 250);
    private static readonly MidiHitData Loud = new(1, 3, 41, 100, 4, 500);
    private static readonly MidiDocument Source = new([], [new MidiTempo(0, 500000)], 480);

    [Fact]
    public void Length_edit_changes_only_midi_notes_and_is_undoable_without_destroying_redo_on_noop()
    {
        var s = Session();
        var first = s.HitAt(Clap, 1250);
        var second = s.HitAt(Clap, 1500);
        var ordinary = s.HitAt(Clap, 1750);
        s.Add([ordinary]);
        s.ImportMidi([(first, Quiet), (second, Loud)], Source);
        Assert.True(s.SetMidiDuration([first, second, ordinary], 123.456));
        Assert.Equal(Quiet with { Duration = 123.456 }, s.MidiOf(first));
        Assert.Equal(Loud with { Duration = 123.456 }, s.MidiOf(second));
        Assert.Null(s.MidiOf(ordinary));
        s.Undo();
        Assert.Equal(Quiet, s.MidiOf(first));
        Assert.Equal(Loud, s.MidiOf(second));
        Assert.True(s.SetMidiDuration([first], Quiet.Duration));
        Assert.True(s.CanRedo);
        s.Redo();
        Assert.Equal(123.456, s.MidiOf(first)!.Duration);
        Assert.True(s.SetMidiDuration([first], 0));
        Assert.Equal(0, s.MidiOf(first)!.Duration);
        foreach (double invalid in new[] { -1, double.NaN, double.PositiveInfinity, int.MaxValue / 2.0 })
            Assert.False(s.SetMidiDuration([first], invalid));
        Assert.Equal(0, s.MidiOf(first)!.Duration);
    }

    [Fact]
    public void Import_uses_loudest_velocity_per_moment_and_undo_restores_all_editor_data()
    {
        var s = Session();
        var hit = s.HitAt(Clap, 1000);
        var second = s.HitAt(new Lane(LaneSource.Skin, "soft-hitwhistle"), 1000);
        var free = s.HitAt(Clap, 1250);
        s.ImportMidi([(hit, Quiet), (hit, Loud), (second, Quiet), (free, Quiet)], Source, [(Clap, new MidiLaneMapping(41, 3))]);
        Assert.Equal(Loud, s.MidiOf(hit));
        Assert.Equal(79, s.VolumeOf(hit));
        Assert.Equal(39, s.VolumeOf(free));
        Assert.Equal(Quiet, s.MidiOf(free));
        s.Undo();
        Assert.Null(s.MidiOf(hit));
        Assert.Equal(100, s.VolumeOf(hit));
        Assert.Null(s.State.MidiSource);
        Assert.Empty(s.State.MidiLanes);
        s.Redo();
        Assert.Equal(Loud, s.MidiOf(hit));
        Assert.Equal(new MidiLaneMapping(41, 3), s.State.MidiLanes[Clap]);
    }

    [Fact]
    public void Loose_hits_share_the_loudest_midi_velocity_and_non_midi_hits_default_to_full_volume()
    {
        var s = Session();
        var quiet = s.HitAt(Clap, 1250);
        var loud = s.HitAt(new Lane(LaneSource.Skin, "soft-hitwhistle"), 1250.1);
        var ordinary = s.HitAt(new Lane(LaneSource.Skin, "soft-hitfinish"), 1250.2);
        var separate = s.HitAt(Clap, 1400);
        s.Add([ordinary, separate]);
        s.ImportMidi([(quiet, Quiet), (loud, Loud)], Source);
        Assert.Equal(79, s.VolumeOf(quiet));
        Assert.Equal(79, s.VolumeOf(loud));
        Assert.Equal(79, s.VolumeOf(ordinary));
        Assert.Equal(100, s.VolumeOf(separate));
        s.Remove([loud]);
        Assert.Equal(39, s.VolumeOf(quiet));
        s.Undo();
        Assert.Equal(79, s.VolumeOf(quiet));
    }

    [Fact]
    public void Generate_uses_the_shared_loose_midi_volume()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Soft\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n"));
        var folder = new MapFolder([]);
        var s = new EditSession(file, folder, Decompiler.Decompile(file, folder));
        var normal = s.HitAt(new Lane(LaneSource.Skin, "soft-hitnormal"), 1250);
        var clap = s.HitAt(Clap, 1250);
        s.ImportMidi([(normal, Quiet), (clap, Loud)], Source);
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, s);
        Assert.Empty(plan.Conflicts);
        var generated = Decompiler.Decompile(plan.Diff, folder);
        Assert.Equal(79, Assert.Single(generated.Moments).Volume);
    }

    [Fact]
    public void Move_copy_delete_and_undo_keep_metadata_with_the_hit_and_existing_destination_wins()
    {
        var s = Session();
        var hit = s.HitAt(Clap, 1250);
        s.ImportMidi([(hit, Quiet)], Source);
        var copy = Assert.Single(s.Copy([hit], 250));
        Assert.Equal(Quiet, s.MidiOf(copy));
        var moved = Assert.Single(s.Move([copy], 250, h => h.Lane));
        Assert.Equal(Quiet, s.MidiOf(moved));
        s.Remove([moved]);
        Assert.Null(s.MidiOf(moved));
        s.Undo();
        Assert.Equal(Quiet, s.MidiOf(moved));
        s.Move([moved], -500, h => h.Lane);
        Assert.Equal(Quiet, s.MidiOf(hit));
        Assert.Single(s.State.Hits, h => h == hit);
        s.Undo();
        Assert.Equal(Quiet, s.MidiOf(moved));
    }

    [Fact]
    public void Lane_mapping_is_undoable_and_project_json_round_trips_midi_data()
    {
        var s = Session();
        s.SetMidiLane(Clap, new MidiLaneMapping(42, 10));
        s.Undo();
        Assert.Empty(s.State.MidiLanes);
        s.Redo();
        var root = Directory.CreateTempSubdirectory("hse-midi-project-").FullName;
        try
        {
            var source = Source with { SourceFormat = 1, SourceTrackCount = 2, OtherEvents = [new MidiPreservedEvent(1, 750, [0xb0, 7, 100])] };
            var free = s.HitAt(Clap, 1250);
            s.ImportMidi([(free, Quiet)], source);
            Assert.True(s.SetMidiDuration([free], 123.456));
            var edited = s.MidiOf(free)!;
            var data = new ProjectData { MidiLanes = new() { [ProjectStore.KeyOf(Clap)] = s.State.MidiLanes[Clap] }, MidiHits = [new(ProjectStore.KeyOf(Clap), 1250, edited)], MidiSource = source };
            ProjectStore.Save(root, "map.osu", data);
            var loaded = ProjectStore.Load(root, "map.osu")!;
            Assert.Equal(edited, Assert.Single(loaded.MidiHits).Midi);
            Assert.Equal(new MidiLaneMapping(42, 10), loaded.MidiLanes[ProjectStore.KeyOf(Clap)]);
            Assert.Equal(480, loaded.MidiSource!.TicksPerQuarter);
            Assert.Equal((1, 750.0), (Assert.Single(loaded.MidiSource.OtherEvents).Track, loaded.MidiSource.OtherEvents[0].Time));
            Assert.Equal(new byte[] { 0xb0, 7, 100 }, loaded.MidiSource.OtherEvents[0].Data);
            var reopened = Session();
            reopened.Restore([free]);
            reopened.RestoreMidi([(free, loaded.MidiHits[0].Midi)], [(Clap, loaded.MidiLanes[ProjectStore.KeyOf(Clap)])], loaded.MidiSource);
            Assert.Equal(edited, reopened.MidiOf(free));
        }
        finally { Directory.Delete(root, true); }
    }
}

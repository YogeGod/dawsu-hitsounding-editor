using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class MidiWorkflowTests
{
    private static (OsuFile File, EditSession Session) Setup()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Soft\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,0,100,1,0\n3000,400,3,2,0,100,1,0\n[HitObjects]\n256,192,1000,1,0,0:0:0:0:\n"));
        var folder = new MapFolder([]);
        return (file, new EditSession(file, folder, Decompiler.Decompile(file, folder)));
    }

    private static readonly Lane Clap = new(LaneSource.Skin, "soft-hitclap");
    private static readonly Dictionary<(int, int), Lane> Map = new() { [(2, 40)] = Clap };
    private static MidiDocument Source(params MidiNote[] notes) => new(notes, [new MidiTempo(0, 500000)], 480)
    {
        SourceFormat = 1, SourceTrackCount = 2,
        OtherEvents = [new MidiPreservedEvent(0, 0, [0xff, 0x58, 4, 4, 2, 24, 8])],
    };

    [Fact]
    public void Import_rejects_collisions_and_anchoring_that_moves_a_note()
    {
        var (_, session) = Setup();
        var note = new MidiNote(1, 2, 40, 90, 4, 1500, 200);
        Assert.Throws<InvalidDataException>(() => MainWindow.PrepareMidiImport(session, Source(note, note), Map));
        Assert.Throws<InvalidDataException>(() => MainWindow.PrepareMidiImport(session, Source(note with { Time = 1001 }), Map));
        session.Add([session.HitAt(Clap, 1500)]);
        Assert.Throws<InvalidDataException>(() => MainWindow.PrepareMidiImport(session, Source(note), Map));
    }

    [Fact]
    public void Offset_moves_metadata_and_export_uses_edited_hits_not_original_source_notes()
    {
        var (file, session) = Setup();
        var source = MainWindow.ShiftMidi(Source(new MidiNote(1, 2, 40, 90, 4, 1500, 200)), 250);
        Assert.Equal(250, source.Tempos[0].Time);
        Assert.Equal(250, source.OtherEvents[0].Time);
        var imported = MainWindow.PrepareMidiImport(session, source, Map);
        session.ImportMidi(imported, source);
        session.Move([imported[0].Hit], 500, h => h.Lane);
        var editedHit = session.State.MidiHits.Keys.Single();
        Assert.True(session.SetMidiDuration([editedHit], 375));
        var mappings = session.State.Hits.Where(h => session.MidiOf(h) is null).Select(h => h.Lane).Distinct().ToDictionary(l => l, _ => new MidiLaneMapping(60, 10));
        var output = MainWindow.BuildMidiExport(session, file, mappings);
        var moved = Assert.Single(output.Notes, n => n.Track == 1);
        Assert.Equal(2250, moved.Time);
        Assert.Equal(90, moved.Velocity);
        Assert.Equal(375, moved.Duration);
        Assert.Equal(source.OtherEvents, output.OtherEvents);
        Assert.Equal(source.Tempos, output.Tempos);
    }

    [Fact]
    public void Export_without_source_uses_map_tempos_and_current_hit_mappings()
    {
        var (file, session) = Setup();
        var mappings = session.State.Hits.Select(h => h.Lane).Distinct().ToDictionary(l => l, _ => new MidiLaneMapping(62, 3));
        var output = MainWindow.BuildMidiExport(session, file, mappings);
        Assert.Equal([500000, 400000], output.Tempos.Select(t => t.MicrosecondsPerQuarter));
        Assert.All(output.Notes, n => { Assert.Equal(62, n.Note); Assert.Equal(3, n.Channel); });
        var roundtrip = MidiFile.Read(MidiFile.Write(output));
        Assert.Equal(session.State.Hits.Count, roundtrip.Notes.Count);
        Assert.Equal([0.0, 3000], roundtrip.OtherEvents.Select(e => e.Time));
        Assert.Equal(new byte[] { 0xff, 0x58, 4, 4, 2, 24, 8 }, roundtrip.OtherEvents[0].Data);
        Assert.Equal(new byte[] { 0xff, 0x58, 4, 3, 2, 24, 8 }, roundtrip.OtherEvents[1].Data);
    }

    [Fact]
    public void Restoring_nearby_midi_hits_keeps_each_notes_metadata()
    {
        var (file, session) = Setup();
        var source = Source(new MidiNote(1, 2, 40, 90, 4, 1500, 200),
                            new MidiNote(1, 2, 40, 45, 8, 1500.25, 300));
        var imported = MainWindow.PrepareMidiImport(session, source, Map);
        session.ImportMidi(imported, source);
        var saved = session.State.MidiHits.Select(kv => new ProjectMidiHit(ProjectStore.KeyOf(kv.Key.Lane), kv.Key.Time, kv.Value)).ToList();
        var (_, reopened) = Setup();
        reopened.Restore(imported.Select(x => reopened.HitAt(x.Hit.Lane, x.Hit.Time)));
        reopened.RestoreMidi(MainWindow.MatchMidiHits(saved, reopened.State.Hits), [], source);

        var mappings = reopened.State.Hits.Where(h => reopened.MidiOf(h) is null).Select(h => h.Lane).Distinct()
            .ToDictionary(l => l, _ => new MidiLaneMapping(60, 10));
        var exported = MainWindow.BuildMidiExport(reopened, file, mappings).Notes.Where(n => n.Track == 1).OrderBy(n => n.Time).ToArray();
        Assert.Equal(2, exported.Length);
        Assert.Equal((1500, 90, 4, 200), (exported[0].Time, exported[0].Velocity, exported[0].ReleaseVelocity, exported[0].Duration));
        Assert.Equal((1500.25, 45, 8, 300), (exported[1].Time, exported[1].Velocity, exported[1].ReleaseVelocity, exported[1].Duration));
    }

    [Fact]
    public void Restore_reserves_exact_matches_before_matching_rounded_times()
    {
        var (_, session) = Setup();
        var first = session.HitAt(Clap, 1500);
        var second = session.HitAt(Clap, 1500.25);
        var fuzzy = new MidiHitData(1, 2, 40, 90, 4, 200);
        var exact = new MidiHitData(1, 2, 40, 45, 8, 300);
        var saved = new[] { new ProjectMidiHit(ProjectStore.KeyOf(Clap), 1500.1, fuzzy), new ProjectMidiHit(ProjectStore.KeyOf(Clap), 1500.25, exact) };
        var restored = MainWindow.MatchMidiHits(saved, [first, second]).ToDictionary(x => x.Hit, x => x.Midi);
        Assert.Equal(fuzzy, restored[first]);
        Assert.Equal(exact, restored[second]);
    }

    [Fact]
    public void Imported_note_keeps_event_order_through_editor_export()
    {
        var (file, session) = Setup();
        var note = new MidiNote(1, 2, 40, 90, 4, 1500, 200) { StartOrder = 10, EndOrder = 12 };
        var source = Source(note) with { OtherEvents = [new MidiPreservedEvent(1, 1500, [0xc1, 5]) { Order = 11 }] };
        session.ImportMidi(MainWindow.PrepareMidiImport(session, source, Map), source);
        var mappings = session.State.Hits.Where(h => session.MidiOf(h) is null).Select(h => h.Lane).Distinct()
            .ToDictionary(l => l, _ => new MidiLaneMapping(60, 10));
        var read = MidiFile.Read(MidiFile.Write(MainWindow.BuildMidiExport(session, file, mappings)));
        Assert.True(read.Notes.Single(n => n.Track == 1).StartOrder < read.OtherEvents.Single().Order);
    }

    [Fact]
    public void Midi_export_replaces_destination_and_cleans_up_after_failure()
    {
        var root = Directory.CreateTempSubdirectory("hse-midi-export-").FullName;
        try
        {
            var path = Path.Combine(root, "song.mid");
            File.WriteAllBytes(path, [1, 2, 3]);
            MainWindow.WriteMidiFile(path, [4, 5]);
            Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(path));
            var directoryDestination = Path.Combine(root, "blocked.mid");
            Directory.CreateDirectory(directoryDestination);
            var error = Record.Exception(() => MainWindow.WriteMidiFile(directoryDestination, [6]));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.Empty(Directory.GetFiles(root, "*.saving"));
            Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }
}

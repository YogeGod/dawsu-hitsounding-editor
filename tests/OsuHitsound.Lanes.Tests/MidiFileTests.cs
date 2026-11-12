using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class MidiFileTests
{
    [Fact]
    public void Format_one_combines_tracks_and_tempo_changes_with_running_status()
    {
        // 480 ticks/quarter; first quarter at 120 BPM, then 60 BPM.
        byte[] midi = [
            0x4d,0x54,0x68,0x64, 0,0,0,6, 0,1, 0,2, 1,0xe0,
            0x4d,0x54,0x72,0x6b, 0,0,0,19,
            0,0xff,0x51,3,0x07,0xa1,0x20, 0x83,0x60,0xff,0x51,3,0x0f,0x42,0x40, 0,0xff,0x2f,0,
            0x4d,0x54,0x72,0x6b, 0,0,0,19,
            0,0x99,36,100, 0x83,0x60,36,0, 0,38,80, 0x83,0x60,38,0, 0,0xff,0x2f,0
        ];
        var document = MidiFile.Read(midi);
        Assert.Equal(480, document.TicksPerQuarter);
        Assert.Collection(document.Notes,
            n => { Assert.Equal((1, 10, 36, 100), (n.Track, n.Channel, n.Note, n.Velocity)); Assert.Equal(0, n.Time); Assert.Equal(500, n.Duration); },
            n => { Assert.Equal((1, 10, 38, 80), (n.Track, n.Channel, n.Note, n.Velocity)); Assert.Equal(500, n.Time); Assert.Equal(1000, n.Duration); });
        Assert.Contains(document.Tempos, t => t.Time == 500 && t.MicrosecondsPerQuarter == 1_000_000);
        var roundTrip = MidiFile.Read(MidiFile.Write(document));
        Assert.Equal(document.Notes.Select(n => (n.Channel, n.Note, n.Velocity, n.Time, n.Duration)),
            roundTrip.Notes.Select(n => (n.Channel, n.Note, n.Velocity, n.Time, n.Duration)));
        Assert.Contains(roundTrip.Tempos, t => t.Time == 500 && t.MicrosecondsPerQuarter == 1_000_000);
    }

    [Fact]
    public void Export_retains_note_performance_and_release_velocity()
    {
        var notes = new[] { new MidiNote(4, 2, 61, 32, 71, 123.5, 25), new MidiNote(0, 10, 42, 127, 0, 200, 125) };
        var result = MidiFile.Read(MidiFile.Write(notes));
        Assert.Equal(notes.Select(n => (n.Channel, n.Note, n.Velocity, n.ReleaseVelocity, n.Time, n.Duration)),
            result.Notes.Select(n => (n.Channel, n.Note, n.Velocity, n.ReleaseVelocity, n.Time, n.Duration)));
    }

    [Fact]
    public void Invalid_or_ambiguous_input_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => MidiFile.Read([0x4d, 0x54]));
        var valid = MidiFile.Write([new MidiNote(0, 1, 60, 90, 0, 0, 100)]);
        Assert.Throws<InvalidDataException>(() => MidiFile.Read(valid[..^2]));
        Assert.Throws<ArgumentException>(() => MidiFile.Write([new MidiNote(0, 1, 60, 90, 0, 0, 200), new MidiNote(1, 1, 60, 50, 0, 50, 100)]));
        Assert.Throws<ArgumentException>(() => MidiFile.Write([new MidiNote(0, 1, 60, 0, 0, 0, 100)]));
    }

    [Fact]
    public void Overlapping_and_coincident_notes_round_trip_in_fifo_order()
    {
        MidiNote[] notes = [new(0, 1, 60, 80, 22, 0, 100), new(0, 1, 60, 90, 33, 0, 150)];
        var read = MidiFile.Read(MidiFile.Write(notes));
        Assert.Equal(notes.Select(n => (n.Velocity, n.ReleaseVelocity, n.Time, n.Duration)),
            read.Notes.Select(n => (n.Velocity, n.ReleaseVelocity, n.Time, n.Duration)));
    }

    [Fact]
    public void Round_trip_preserves_tracks_controllers_metadata_and_sysex_across_tempos()
    {
        var document = new MidiDocument(
            [new MidiNote(1, 10, 36, 100, 30, 0, 1500)],
            [new MidiTempo(0, 500_000), new MidiTempo(500, 1_000_000)], 480)
        {
            SourceFormat = 1, SourceTrackCount = 3,
            OtherEvents =
            [
                new MidiPreservedEvent(1, 0, [0xff, 0x03, 3, (byte)'D', (byte)'r', (byte)'m']),
                new MidiPreservedEvent(1, 500, [0xb9, 64, 127]),
                new MidiPreservedEvent(2, 1500, [0xf0, 3, 1, 2, 0xf7]),
                new MidiPreservedEvent(0, 500, [0xff, 0x58, 4, 3, 2, 24, 8])
            ]
        };
        var read = MidiFile.Read(MidiFile.Write(document));
        Assert.Equal(1, read.SourceFormat);
        Assert.Equal(3, read.SourceTrackCount);
        Assert.Equal((1, 1500d), (read.Notes.Single().Track, read.Notes.Single().Duration));
        Assert.Equal(document.OtherEvents.OrderBy(e => e.Track).ThenBy(e => e.Time).Select(e => (e.Track, e.Time, Convert.ToHexString(e.Data))),
            read.OtherEvents.OrderBy(e => e.Track).ThenBy(e => e.Time).Select(e => (e.Track, e.Time, Convert.ToHexString(e.Data))));
    }

    [Fact]
    public void Independent_tracks_can_contain_simultaneous_same_pitch_notes()
    {
        var document = new MidiDocument(
            [new MidiNote(0, 1, 60, 80, 0, 0, 100), new MidiNote(1, 1, 60, 90, 0, 0, 100)], [], 480)
        { SourceFormat = 1, SourceTrackCount = 2 };
        var read = MidiFile.Read(MidiFile.Write(document));
        Assert.Equal(new[] { 0, 1 }, read.Notes.Select(n => n.Track));
    }

    [Fact]
    public void Invalid_preserved_events_are_rejected()
    {
        var document = new MidiDocument([], [], 480)
        { OtherEvents = [new MidiPreservedEvent(0, 0, [0xff, 0x03, 4, 65])] };
        Assert.Throws<ArgumentException>(() => MidiFile.Write(document));
    }

    [Fact]
    public void Running_status_controller_events_survive_export()
    {
        byte[] midi = [
            0x4d,0x54,0x68,0x64, 0,0,0,6, 0,0, 0,1, 1,0xe0,
            0x4d,0x54,0x72,0x6b, 0,0,0,16,
            0,0xb0,7,50, 0,10,40, 0,0xff,3,1,65, 0,0xff,0x2f,0
        ];
        var first = MidiFile.Read(midi);
        var second = MidiFile.Read(MidiFile.Write(first));
        Assert.Equal(new[] { "B00732", "B00A28", "FF030141" }, second.OtherEvents.Select(e => Convert.ToHexString(e.Data)));
    }

    [Fact]
    public void Repeated_round_trips_do_not_accumulate_default_tempo_events()
    {
        var document = new MidiDocument([], [new MidiTempo(0, 400_000)], 480);
        for (int i = 0; i < 3; i++) document = MidiFile.Read(MidiFile.Write(document));
        var tempo = Assert.Single(document.Tempos);
        Assert.Equal(400_000, tempo.MicrosecondsPerQuarter);
    }

    [Fact]
    public void Round_trip_keeps_same_tick_program_changes_relative_to_notes()
    {
        byte[] midi = [
            0x4d,0x54,0x68,0x64, 0,0,0,6, 0,0, 0,1, 1,0xe0,
            0x4d,0x54,0x72,0x6b, 0,0,0,20,
            0,0x90,60,100, 0,0xc0,5, 0x83,0x60,0x80,60,40, 0,0xb0,64,0, 0,0xff,0x2f,0
        ];
        var read = MidiFile.Read(MidiFile.Write(MidiFile.Read(midi)));
        Assert.True(read.Notes[0].StartOrder < read.OtherEvents[0].Order);
        Assert.True(read.Notes[0].EndOrder < read.OtherEvents[1].Order);
    }
}

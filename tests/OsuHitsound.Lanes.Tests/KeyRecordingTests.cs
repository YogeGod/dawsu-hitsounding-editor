using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.Lanes.Tests;

public class KeyRecordingTests
{
    private static readonly TempoMap Tempo = new([(0, 500, 4)]); // 120 BPM from 0: 1 ms = 1.92 ticks
    private static KeyNote N(long start, long length, int pitch = 60) => new(Guid.NewGuid(), start, length, pitch, 100);
    private static KeyController C(long tick, ControllerKind kind, int value) => new(tick, kind, value);

    [Fact]
    public void The_recorder_keeps_played_timing_ignores_the_count_in_and_ends_held_notes_at_stop()
    {
        var r = new KeyRecorder(Tempo, startMs: 1000);
        r.NoteOn(900, 59, 80);          // during the count-in: ignored
        r.NoteOn(1013, 60, 90);         // not snapped
        r.NoteOff(1263, 60);
        r.NoteOn(1500, 64, 70);         // still held at stop
        r.Controller(1100, ControllerKind.Sustain, 127);
        r.NoteOff(1600, 59);            // its note-on was ignored, so nothing happens
        var take = r.Finish(2000);

        Assert.Equal((Tempo.RoundTick(1000), Tempo.RoundTick(2000)), (take.Start, take.End));
        Assert.Equal([(Tempo.RoundTick(1013), 60, 90), (Tempo.RoundTick(1500), 64, 70)], take.Notes.Select(n => (n.Start, n.Pitch, n.Velocity)));
        Assert.Equal(Tempo.RoundTick(2000) - Tempo.RoundTick(1500), take.Notes[1].Length);
        Assert.Equal(Tempo.RoundTick(1100), take.Controllers.Single().Tick);
    }

    [Fact]
    public void The_preview_shows_held_notes_growing()
    {
        var r = new KeyRecorder(Tempo, 0);
        r.NoteOn(100, 60, 100);
        Assert.Equal(Tempo.RoundTick(300) - Tempo.RoundTick(100), Assert.Single(r.Preview(300)).Length);
    }

    [Fact]
    public void Notes_starting_in_the_range_are_replaced_and_others_stay()
    {
        var before = N(0, 2000);        // starts before, overlaps into the range: stays
        var inside = N(1000, 100);      // replaced
        var atEnd = N(3000, 100);       // starts exactly at the end: after the range, stays
        var track = KeyTrack.Create("Piano") with { Notes = [before, inside, atEnd] };
        var played = N(1200, 300, 67);
        var result = KeyRecording.Apply(track, new KeyTake(960, 3000, [played], []));
        Assert.Equal([before.Id, atEnd.Id, played.Id], result.Notes.Select(n => n.Id));
    }

    [Fact]
    public void Controllers_in_the_range_are_replaced_and_restored_at_the_end()
    {
        var track = KeyTrack.Create("Piano") with
        {
            Controllers = [C(0, ControllerKind.Sustain, 127), C(1500, ControllerKind.Sustain, 0), C(1600, ControllerKind.PitchBend, 4000), C(5000, ControllerKind.Sustain, 127)],
        };
        // The take presses the pedal at 1200 and leaves it down; it plays no bend.
        var take = new KeyTake(1000, 3000, [N(1100, 100)], [C(1200, ControllerKind.Sustain, 127)]);
        var result = KeyRecording.Apply(track, take);

        Assert.Equal([C(0, ControllerKind.Sustain, 127), C(1200, ControllerKind.Sustain, 127),
            C(3000, ControllerKind.Sustain, 0), C(3000, ControllerKind.PitchBend, 4000), C(5000, ControllerKind.Sustain, 127)], result.Controllers);
        Assert.Equal(KeyControllers.ValueAt(track.Controllers, ControllerKind.Sustain, 4000), KeyControllers.ValueAt(result.Controllers, ControllerKind.Sustain, 4000));
        Assert.Equal(KeyControllers.ValueAt(track.Controllers, ControllerKind.PitchBend, 4000), KeyControllers.ValueAt(result.Controllers, ControllerKind.PitchBend, 4000));
    }

    [Fact]
    public void A_take_with_nothing_played_changes_nothing_and_a_pass_is_one_undo_step()
    {
        var track = KeyTrack.Create("Piano") with { Notes = [N(1000, 100)] };
        Assert.Same(track, KeyRecording.Apply(track, new KeyTake(0, 5000, [], [])));

        var session = new KeysoundSession(new KeysoundState([track]));
        session.ReplaceTrack(KeyRecording.Apply(track, new KeyTake(0, 5000, [N(200, 50, 72)], [C(300, ControllerKind.Modulation, 40)])), "Record");
        Assert.Equal(72, session.State.Tracks[0].Notes.Single().Pitch);
        session.Undo();
        Assert.Same(track, session.State.Tracks[0]);
    }

    [Fact]
    public void Controller_values_rest_until_their_first_change()
    {
        var list = new[] { C(100, ControllerKind.PitchBend, -300), C(200, ControllerKind.PitchBend, 0) };
        Assert.Equal(0, KeyControllers.ValueAt(list, ControllerKind.PitchBend, 50));
        Assert.Equal(-300, KeyControllers.ValueAt(list, ControllerKind.PitchBend, 150));
        Assert.Equal(0, KeyControllers.ValueAt(list, ControllerKind.Sustain, 150));
    }
}

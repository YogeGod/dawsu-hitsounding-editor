using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.Lanes.Tests;

public class KeySyncTests
{
    // 120 BPM from 0 ms: 960 ticks = 500 ms, so 1 ms ≈ 1.92 ticks.
    private static readonly TempoMap Tempo = new([(0, 500, 4)]);
    private static readonly KeySyncTarget CircleA = new(0, 0, 1000, HitPosition.Circle);
    private static readonly KeySyncTarget Head = new(1, 0, 1500, HitPosition.SliderHead);
    private static readonly KeySyncTarget Tail = new(1, 1, 1520, HitPosition.SliderTail);
    private static readonly KeySyncTarget CircleB = new(2, 0, 2000, HitPosition.Circle);
    private static readonly KeySyncTarget CircleC = new(3, 0, 2020, HitPosition.Circle);
    private static readonly KeySyncTarget[] Targets = [CircleA, Head, Tail, CircleB, CircleC];

    private static KeyNote At(double ms, int pitch = 60) => new(Guid.NewGuid(), Tempo.RoundTick(ms), 240, pitch, 100);

    private static List<KeySyncProposal> Preview(KeySyncOptions? options, params KeyNote[] notes) =>
        KeySync.Preview(notes.Select(n => (Guid.Empty, n)), Targets, Tempo, options ?? new KeySyncOptions());

    [Fact]
    public void A_note_moves_to_the_nearest_target_within_the_limit()
    {
        var near = At(1012);
        var far = At(1200); // 200 ms from anything
        var p = Preview(null, near, far);
        Assert.Equal(CircleA, p[0].Candidates[0]);
        Assert.False(p[1].InRange);

        var moves = KeySync.Moves(p, Tempo);
        var move = Assert.Single(moves);
        Assert.Equal(near.Id, move.Note);
        Assert.Equal(Tempo.RoundTick(1000), move.Start);
        Assert.Equal(CircleA, move.Target);
    }

    [Fact]
    public void Equally_close_targets_need_a_choice()
    {
        var between = At(2010); // 10 ms from both circles
        var p = Assert.Single(Preview(null, between));
        Assert.True(p.Ambiguous);
        Assert.Empty(KeySync.Moves([p], Tempo)); // never resolved automatically
        var chosen = Assert.Single(KeySync.Moves([p], Tempo, new Dictionary<Guid, KeySyncTarget> { [between.Id] = CircleC }));
        Assert.Equal(CircleC, chosen.Target);
        // A choice that isn't one of the candidates is ignored.
        Assert.Empty(KeySync.Moves([p], Tempo, new Dictionary<Guid, KeySyncTarget> { [between.Id] = CircleA }));
    }

    [Fact]
    public void Type_filters_and_the_limit_narrow_the_candidates()
    {
        var note = At(1515);
        var noTails = new KeySyncOptions { Positions = new HashSet<HitPosition> { HitPosition.Circle, HitPosition.SliderHead } };
        Assert.Equal([Head], Assert.Single(Preview(noTails, note)).Candidates);
        Assert.False(Assert.Single(Preview(new KeySyncOptions { MaxShift = 2 }, note)).InRange);
    }

    [Fact]
    public void Valid_assignments_are_kept_unless_resyncing_all()
    {
        var synced = At(1000) with { Target = CircleA };
        Assert.Empty(Preview(null, synced));
        Assert.Single(Preview(new KeySyncOptions { ResyncAll = true }, synced));
    }

    [Fact]
    public void Moved_and_missing_targets_are_flagged_and_offered_again()
    {
        var moved = At(1000) with { Target = CircleA with { Time = 990 } };
        var missing = At(1000) with { Target = new KeySyncTarget(9, 0, 1000, HitPosition.Circle) };
        Assert.Equal(KeySyncStatus.Moved, KeySync.Status(moved, Targets));
        Assert.Equal(KeySyncStatus.Missing, KeySync.Status(missing, Targets));
        Assert.Equal(KeySyncStatus.Synced, KeySync.Status(At(1000) with { Target = CircleA }, Targets));
        Assert.Equal(2, Preview(null, moved, missing).Count); // need re-syncing
    }

    [Fact]
    public void Sync_is_one_step_and_moving_a_start_clears_the_target()
    {
        var track = KeyTrack.Create("Piano");
        var a = At(1012);
        var b = At(2003, 64);
        var s = new KeysoundSession(new KeysoundState([track with { Notes = [a, b] }]));
        s.ApplySync(KeySync.Moves(KeySync.Preview([(track.Id, a), (track.Id, b)], Targets, Tempo, new KeySyncOptions()), Tempo));
        var notes = s.State.Track(track.Id)!.Notes;
        Assert.Equal([CircleA, CircleB], notes.Select(n => n.Target));
        Assert.Equal("Sync", s.UndoLabel);

        var synced = notes[0];
        s.UpdateNotes(track.Id, [synced with { Pitch = 62, Velocity = 50, Length = 480 }], "Edit");
        Assert.Equal(CircleA, s.State.Track(track.Id)!.Notes[0].Target); // pitch, velocity and length keep it
        s.UpdateNotes(track.Id, [s.State.Track(track.Id)!.Notes[0] with { Start = synced.Start + 10 }], "Move");
        Assert.Null(s.State.Track(track.Id)!.Notes[0].Target);

        s.Undo();
        s.Undo();
        s.Undo(); // back before Sync: times and assignments together
        Assert.Equal([a, b], s.State.Track(track.Id)!.Notes);
    }

    [Fact]
    public void A_render_job_carries_the_controllers_in_force_and_the_pedal_hold()
    {
        var track = KeyTrack.Create("Piano") with
        {
            Controllers = [new KeyController(0, ControllerKind.Sustain, 127), new KeyController(480, ControllerKind.Modulation, 40), new KeyController(1920, ControllerKind.Sustain, 0)],
        };
        var note = new KeyNote(Guid.NewGuid(), 960, 480, 60, 90); // 500 to 750 ms; the pedal lets go at 1000 ms
        var job = KeyRender.Job(track, note, Tempo);
        Assert.Equal((250.0, 500.0), (job.Length, job.Held));
        Assert.Equal(
        [
            new KeyRenderController(0, ControllerKind.Sustain, 127), new KeyRenderController(0, ControllerKind.Modulation, 40),
            new KeyRenderController(0, ControllerKind.PitchBend, 0), new KeyRenderController(500, ControllerKind.Sustain, 0),
        ], job.Controllers);
        Assert.Equal(job.Key, KeyRender.Job(track, note with { Id = Guid.NewGuid(), Start = note.Start }, Tempo).Key);
    }

    [Fact]
    public void A_pedal_never_released_lets_go_with_the_key_in_a_render()
    {
        var track = KeyTrack.Create("Piano") with { Controllers = [new KeyController(0, ControllerKind.Sustain, 127)] };
        var job = KeyRender.Job(track, new KeyNote(Guid.NewGuid(), 960, 480, 60, 90), Tempo);
        Assert.Equal(job.Length, job.Held);
        Assert.Equal(new KeyRenderController(250, ControllerKind.Sustain, 0), job.Controllers[^1]);
    }

    [Fact]
    public void Generate_moves_only_green_lines_that_change_nothing_but_sounds()
    {
        var reference = OsuHitsound.Format.OsuFile.Parse(System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n",
            "osu file format v14", "", "[General]", "AudioFilename: a.mp3", "", "[Metadata]", "Version:Hard", "",
            "[Difficulty]", "SliderMultiplier:1.4", "SliderTickRate:1", "",
            "[TimingPoints]", "0,500,4,1,0,100,1,0", "1002,-100,4,1,0,80,0,0", "2003,-50,4,1,0,80,0,0", "3002,-50,4,1,0,60,0,1", "",
            "[HitObjects]", "256,192,1000,2,0,L|356:192,1,100", "256,192,2000,2,0,L|356:192,1,100", "256,192,3000,2,0,L|356:192,1,100", "")));
        var lines = KeyGenerate.DiffText(reference).Split("\r\n");
        Assert.Contains("1000,-100,4,1,0,80,0,0", lines); // only the volume changes: moved onto the slider head
        Assert.Contains("2003,-50,4,1,0,80,0,0", lines); // a new slider velocity: osu! reads it at the exact time, so it stays
        Assert.Contains("3002,-50,4,1,0,60,0,1", lines); // kiai starts there: it stays too
    }

    [Fact]
    public void Split_parts_and_pasted_copies_are_not_synced()
    {
        var note = At(1000) with { Target = CircleA, Length = 960 };
        var (changed, added) = KeyEditing.Split([note], note.Start + 480);
        Assert.Equal(CircleA, Assert.Single(changed).Target); // the left part keeps its start
        Assert.Null(Assert.Single(added).Target);
        Assert.Null(Assert.Single(KeyEditing.PasteAt([note], 0)).Target);
    }
}

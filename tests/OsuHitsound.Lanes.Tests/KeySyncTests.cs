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
    public void Split_parts_and_pasted_copies_are_not_synced()
    {
        var note = At(1000) with { Target = CircleA, Length = 960 };
        var (changed, added) = KeyEditing.Split([note], note.Start + 480);
        Assert.Equal(CircleA, Assert.Single(changed).Target); // the left part keeps its start
        Assert.Null(Assert.Single(added).Target);
        Assert.Null(Assert.Single(KeyEditing.PasteAt([note], 0)).Target);
    }
}

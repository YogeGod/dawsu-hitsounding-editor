namespace OsuHitsound.Lanes;

/// <summary>
/// Collects one live recording pass in Hitsound mode: the lanes tapped (from the computer keyboard or a MIDI keyboard)
/// at the times they were heard, in timeline ms. Taps during the count-in are ignored, except ones just before the
/// record start (<see cref="EarlyGraceMs"/>), which were meant for its first beat.
/// </summary>
public sealed class HitRecorder(double startMs)
{
    /// <summary>How early a tap may come before the record start and still count: a player hitting the first beat early.</summary>
    public const double EarlyGraceMs = 60;

    private readonly List<(double Time, Lane Lane)> taps = [];

    public double StartMs => startMs;

    public IReadOnlyList<(double Time, Lane Lane)> Taps => taps;

    public void Tap(double ms, Lane lane)
    {
        if (ms < startMs - EarlyGraceMs) return;
        taps.Add((ms, lane));
    }

    /// <summary>Ends the pass at <paramref name="stopMs"/>: the recorded range and what was tapped in it.</summary>
    public HitTake Finish(double stopMs) => new(startMs, Math.Max(startMs, stopMs), [.. taps.OrderBy(t => t.Time)]);
}

/// <summary>One recorded pass: its range in ms [Start, End) and the taps, in time order, as played.</summary>
public sealed record HitTake(double Start, double End, IReadOnlyList<(double Time, Lane Lane)> Taps);

/// <summary>Replacement recording: how a take goes into the lanes.</summary>
public static class HitRecording
{
    /// <summary>
    /// The hits a take makes: each tap moved by <paramref name="snap"/> (the grid, or as played), then placed on the
    /// object there if one is within <see cref="EditSession.AnchorTolerance"/>, else a hit with no object. Two taps of
    /// one lane that snap to the same place make one hit.
    /// </summary>
    public static IReadOnlyList<EditHit> Hits(EditSession session, HitTake take, Func<double, double> snap) =>
        [.. take.Taps.Select(t => session.HitAt(t.Lane, snap(t.Time))).Distinct()];

    /// <summary>
    /// Puts a take into the session as one undo step named <paramref name="label"/>:
    /// <list type="bullet">
    /// <item>In each lane that was tapped, the hits inside the recorded range (widened to cover the recorded hits, so a
    /// tap snapped just past either end still replaces) are removed.</item>
    /// <item>Lanes that weren't tapped keep their hits.</item>
    /// <item>The take's hits are added.</item>
    /// </list>
    /// A take with no taps leaves the session unchanged. Returns the recorded hits, to select them.
    /// </summary>
    public static IReadOnlyList<EditHit> Apply(EditSession session, HitTake take, Func<double, double> snap, string label = "Record")
    {
        if (take.Taps.Count == 0 || session.IsGestureActive) return [];
        var hits = Hits(session, take, snap);
        double start = Math.Min(take.Start, hits.Min(h => h.Time));
        double end = Math.Max(take.End, hits.Max(h => h.Time) + 1);
        var lanes = take.Taps.Select(t => t.Lane).ToHashSet();
        var replaced = session.InRange(start, end, lanes);

        session.BeginGesture(label);
        session.Remove(replaced, label);
        session.Add(hits, label);
        session.EndGesture();
        return hits;
    }
}

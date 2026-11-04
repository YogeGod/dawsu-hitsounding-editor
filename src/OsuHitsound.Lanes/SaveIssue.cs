namespace OsuHitsound.Lanes;

/// <summary>An intended edit that normal Save cannot write; no automatic repair changes musical intent.</summary>
public sealed record SaveIssue(double Time, MomentKey? Moment, string Reason, string Resolution)
{
    public static IReadOnlyList<SaveIssue> Find(EditSession session, EditState state, CompileResult result) =>
        [.. result.Conflicts.Select(c => new SaveIssue(c.Time, c.Moment, c.Reason,
                "Go to this moment and adjust its sounds so they can be saved together."))
            .Concat(state.Hits.Where(h => h.Anchor is null).GroupBy(h => h.Time).Select(g =>
                new SaveIssue(g.Key, null, $"{g.Count()} hit{(g.Count() == 1 ? " has" : "s have")} no gameplay object.",
                    "Move the hits onto an existing object, remove them, or use Generate to create a separate hitsound difficulty.")))
            .OrderBy(i => i.Time)];
}

public sealed class SaveBlockedException(IReadOnlyList<SaveIssue> issues)
    : InvalidOperationException("Save blocked: resolve the listed issues first. Your map has not been changed.")
{
    public IReadOnlyList<SaveIssue> Issues { get; } = issues;
}

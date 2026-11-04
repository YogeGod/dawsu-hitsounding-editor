namespace OsuHitsound.Lanes;

/// <summary>An intended edit that normal Save cannot write; no automatic repair changes musical intent.</summary>
public sealed record SaveIssue(double Time, MomentKey? Moment, string Reason, string Resolution)
{
    public static IReadOnlyList<SaveIssue> Find(EditSession session, EditState state, CompileResult result) =>
        [.. result.Conflicts.Select(c => new SaveIssue(c.Time, c.Moment, c.Reason,
                ResolutionFor(c.Reason)))
            .Concat(state.Hits.Where(h => h.Anchor is null).GroupBy(h => h.Time).Select(g =>
                new SaveIssue(g.Key, null, $"{g.Count()} hit{(g.Count() == 1 ? " has" : "s have")} no gameplay object.",
                    "Move the hits onto an existing object, remove them, or use Generate to create a separate hitsound difficulty.")))
            .OrderBy(i => i.Time)];

    private static string ResolutionFor(string reason) => reason switch
    {
        var r when r.StartsWith("No hitnormal:", StringComparison.Ordinal) => "Add one hitnormal at this moment, then save again.",
        var r when r.StartsWith("Two hitnormals", StringComparison.Ordinal) => "Keep one hitnormal at this moment and remove the extra one.",
        var r when r.StartsWith("The same addition twice", StringComparison.Ordinal) => "Keep one instance of each addition: whistle, finish or clap.",
        var r when r.StartsWith("Additions from two sample sets", StringComparison.Ordinal) => "Choose additions from the same sample set for this moment.",
        var r when r.StartsWith("Needs index", StringComparison.Ordinal) => "Choose sample lanes that can share one index at this moment.",
        _ => "Go to this moment and adjust its sounds so they can be saved together.",
    };

}

public sealed class SaveBlockedException(IReadOnlyList<SaveIssue> issues)
    : InvalidOperationException("Save blocked: resolve the listed issues first. Your map has not been changed.")
{
    public IReadOnlyList<SaveIssue> Issues { get; } = issues;
}

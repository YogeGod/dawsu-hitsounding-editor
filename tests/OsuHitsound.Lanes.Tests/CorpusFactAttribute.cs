namespace OsuHitsound.Lanes.Tests;

/// <summary>
/// A test over the local corpus/ (tools/build-corpus.ps1). The folder is git-ignored, so on a clone without it the
/// test is skipped instead of failing.
/// </summary>
public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute()
    {
        if (!Directory.Exists(Folder))
            Skip = "corpus/ is missing. Run tools/build-corpus.ps1 to copy maps from your osu!lazer library or beatmap packs.";
    }

    /// <summary>The corpus folder at the repo root.</summary>
    public static string Folder => Path.Combine(RepoRoot(), "corpus");

    /// <summary>The folder holding the solution file.</summary>
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any()) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repo root (*.slnx) not found.");
    }
}

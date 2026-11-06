namespace OsuHitsound.App.Tests;

/// <summary>Files in the repository, found from the test's output folder (the folder with the .slnx).</summary>
internal static class TestPaths
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any()) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("The repository root (with the .slnx) wasn't found.");
    }

    public static string RepoFile(params string[] parts) => Path.Combine([RepoRoot(), .. parts]);
}

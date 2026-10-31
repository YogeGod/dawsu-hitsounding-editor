using OsuHitsound.Format;

namespace OsuHitsound.Format.Tests;

/// <summary>
/// The null round-trip over real maps in corpus/. Build the corpus first with tools/build-corpus.ps1.
/// </summary>
public class CorpusTests
{
    [Fact]
    public void Every_corpus_file_round_trips()
    {
        var corpus = Path.Combine(RepoRoot(), "corpus");
        Assert.True(Directory.Exists(corpus), "corpus/ is missing. Run tools/build-corpus.ps1 to copy maps from your osu!lazer library.");

        var files = RoundTripCheck.FindFiles(corpus).ToList();
        Assert.NotEmpty(files);

        var failures = files
            .Select(f => (f, problems: RoundTripCheck.RunFile(f)))
            .Where(r => r.problems.Count > 0)
            .Select(r => $"{Path.GetFileName(r.f)}: {r.problems[0]}")
            .ToList();

        Assert.True(failures.Count == 0, $"{failures.Count} of {files.Count} files failed:\n" + string.Join('\n', failures.Take(20)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any()) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repo root (*.slnx) not found.");
    }
}

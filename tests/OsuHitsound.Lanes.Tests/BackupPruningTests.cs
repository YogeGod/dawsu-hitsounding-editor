using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

/// <summary>Backups keep the newest 20 per difficulty (issue #29).</summary>
public sealed class BackupPruningTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hse-backups-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private const string Insane = @"C:\maps\x\Band - Song (Me) [Insane].osu";
    private const string Hard = @"C:\maps\x\Band - Song (Me) [Hard].osu";

    private string FolderOf(string osu) => Path.GetDirectoryName(SafeFile.Backup(root, osu, [0]))!;

    [Fact]
    public void The_21st_backup_removes_the_oldest_and_other_difficulties_keep_theirs()
    {
        var hard = SafeFile.Backup(root, Hard, [9]);
        var first = SafeFile.Backup(root, Insane, [1]);
        for (int i = 0; i < 19; i++) SafeFile.Backup(root, Insane, [2]);
        var insaneFolder = Path.GetDirectoryName(first)!;
        Assert.Equal(20, Directory.GetFiles(insaneFolder, "*.osu").Length);
        Assert.True(File.Exists(first));

        var newest = SafeFile.Backup(root, Insane, [3]);

        Assert.Equal(20, Directory.GetFiles(insaneFolder, "*.osu").Length);
        Assert.False(File.Exists(first));   // the oldest went
        Assert.True(File.Exists(newest));
        Assert.True(File.Exists(hard));     // another difficulty's backup is untouched
    }

    [Fact]
    public void Backups_taken_in_the_same_millisecond_are_all_kept()
    {
        var names = Enumerable.Range(0, 5).Select(_ => SafeFile.Backup(root, Insane, [1])).ToList();
        Assert.Equal(5, names.Distinct().Count());
        Assert.All(names, n => Assert.True(File.Exists(n)));
    }
}

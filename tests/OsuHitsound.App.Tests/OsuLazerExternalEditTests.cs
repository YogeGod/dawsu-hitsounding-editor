namespace OsuHitsound.App.Tests;

public class OsuLazerExternalEditTests
{
    [Fact]
    public void The_newest_mounted_set_and_its_last_saved_difficulty_are_found()
    {
        string root = Path.Combine(Path.GetTempPath(), "dawsu-lazer-" + Guid.NewGuid().ToString("N"));
        try
        {
            string older = Path.Combine(root, new string('a', 64)), newer = Path.Combine(root, new string('b', 64));
            Directory.CreateDirectory(older); Directory.CreateDirectory(newer);
            Directory.CreateDirectory(Path.Combine(root, "not-a-mount"));
            Directory.SetCreationTimeUtc(older, new DateTime(2026, 1, 1));
            File.WriteAllText(Path.Combine(older, "Old [Hard].osu"), "");
            File.WriteAllText(Path.Combine(newer, "Song [Easy].osu"), "");
            File.WriteAllText(Path.Combine(newer, "Song [Insane].osu"), "");
            File.SetLastWriteTimeUtc(Path.Combine(newer, "Song [Easy].osu"), new DateTime(2026, 1, 1));
            File.WriteAllText(Path.Combine(root, "not-a-mount", "x.osu"), "");
            Assert.Equal(Path.Combine(newer, "Song [Insane].osu"), OsuLazerExternalEdit.FindMountedBeatmap(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Nothing_is_found_when_no_set_is_being_edited_externally()
    {
        string root = Path.Combine(Path.GetTempPath(), "dawsu-lazer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, new string('c', 64))); // mounted, but no .osu yet
        try { Assert.Null(OsuLazerExternalEdit.FindMountedBeatmap(root)); }
        finally { Directory.Delete(root, recursive: true); }
        Assert.Null(OsuLazerExternalEdit.FindMountedBeatmap(Path.Combine(root, "gone")));
    }
}

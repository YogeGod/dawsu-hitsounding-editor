using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

/// <summary><see cref="SafeFile.ReplaceIfUnchanged"/>: replace a file only while it still holds what was checked.</summary>
public class SafeFileTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hse-safefile-").FullName;
    private string File1 => Path.Combine(dir, "a.osu");

    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void An_unchanged_file_is_replaced()
    {
        File.WriteAllBytes(File1, [1, 2, 3]);
        SafeFile.ReplaceIfUnchanged(File1, [1, 2, 3], [4, 5], "changed");
        Assert.Equal([4, 5], File.ReadAllBytes(File1));
        Assert.Equal(["a.osu"], Directory.EnumerateFiles(dir).Select(Path.GetFileName)); // no temp left behind
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 9 })] // same length, other bytes
    [InlineData(new byte[] { 1, 2, 3, 4 })] // grew
    public void A_changed_file_is_left_as_it_is(byte[] onDisk)
    {
        File.WriteAllBytes(File1, onDisk);
        var ex = Assert.Throws<IOException>(() => SafeFile.ReplaceIfUnchanged(File1, [1, 2, 3], [4, 5], "changed"));
        Assert.Equal("changed", ex.Message);
        Assert.Equal(onDisk, File.ReadAllBytes(File1));
        Assert.Equal(["a.osu"], Directory.EnumerateFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public void A_file_that_was_deleted_meanwhile_is_not_recreated()
    {
        Assert.Throws<IOException>(() => SafeFile.ReplaceIfUnchanged(File1, [1, 2, 3], [4, 5], "changed"));
        Assert.False(File.Exists(File1));
    }

    [Fact]
    public void A_new_file_is_written_only_while_none_exists()
    {
        SafeFile.ReplaceIfUnchanged(File1, null, [4, 5], "changed");
        Assert.Equal([4, 5], File.ReadAllBytes(File1));

        Assert.Throws<IOException>(() => SafeFile.ReplaceIfUnchanged(File1, null, [6], "changed"));
        Assert.Equal([4, 5], File.ReadAllBytes(File1));
    }

    [Fact]
    public void A_file_another_program_has_open_for_writing_is_not_replaced()
    {
        File.WriteAllBytes(File1, [1, 2, 3]);
        using (var writer = new FileStream(File1, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            Assert.Throws<IOException>(() => SafeFile.ReplaceIfUnchanged(File1, [1, 2, 3], [4, 5], "changed"));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(File1));
    }
}

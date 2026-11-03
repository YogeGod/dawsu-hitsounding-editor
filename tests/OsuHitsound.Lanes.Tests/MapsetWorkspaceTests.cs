using System.Text;

namespace OsuHitsound.Lanes.Tests;

public class MapsetWorkspaceTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hse-working-").FullName;
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void Working_copy_is_independent_complete_and_reused_without_overwriting_edits()
    {
        string source = Directory.CreateDirectory(Path.Combine(root, "Original")).FullName;
        string songs = Directory.CreateDirectory(Path.Combine(root, "Songs")).FullName;
        string path = Path.Combine(source, "map.osu");
        byte[] original = Encoding.UTF8.GetBytes("osu file format v14\n[Metadata]\nBeatmapID:123\nBeatmapSetID:456\n[HitObjects]\n256,192,1000,1,0,0:0:0:0:\n");
        File.WriteAllBytes(path, original);
        Directory.CreateDirectory(Path.Combine(source, "samples"));
        File.WriteAllBytes(Path.Combine(source, "samples", "kick.wav"), [1, 2, 3]);
        string copy = MapsetWorkspace.Prepare(path, songs);
        Assert.NotEqual(path, copy);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Contains("BeatmapID:0", File.ReadAllText(copy));
        Assert.Contains("BeatmapSetID:-1", File.ReadAllText(copy));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(copy)!, "samples", "kick.wav")));
        File.AppendAllText(copy, "// working edit\n");
        Assert.Equal(copy, MapsetWorkspace.Prepare(path, songs));
        Assert.EndsWith("// working edit\n", File.ReadAllText(copy));
        Assert.Equal(copy, MapsetWorkspace.Prepare(copy, songs));
        Assert.Equal(2, Directory.GetDirectories(root).Length);
    }

    [Fact]
    public void A_map_directly_in_songs_is_rejected_instead_of_copying_the_entire_library()
    {
        string songs = Directory.CreateDirectory(Path.Combine(root, "Songs")).FullName;
        string path = Path.Combine(songs, "map.osu"); File.WriteAllText(path, "osu file format v14\n");
        Assert.Throws<IOException>(() => MapsetWorkspace.Prepare(path, songs));
        Assert.Empty(Directory.GetDirectories(songs));
    }

    [Fact]
    public void Existing_mapset_inside_songs_gets_a_sibling_copy_and_failed_copy_leaves_no_partial_folder()
    {
        string songs = Directory.CreateDirectory(Path.Combine(root, "Songs")).FullName;
        string source = Directory.CreateDirectory(Path.Combine(songs, "Original")).FullName;
        string path = Path.Combine(source, "map.osu"); File.WriteAllText(path, "osu file format v14\n");
        File.WriteAllText(Path.Combine(source, ".dawsu-working-mapset"), source); // an unrelated marker must not cause editing the original
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => MapsetWorkspace.Prepare(path, songs));
        Assert.Single(Directory.GetDirectories(songs));
        string copy = MapsetWorkspace.Prepare(path, songs);
        Assert.Equal(songs, Path.GetDirectoryName(Path.GetDirectoryName(copy)));
        Assert.NotEqual(source, Path.GetDirectoryName(copy));
        Assert.Equal(copy, MapsetWorkspace.Prepare(path, songs));
    }

    [Fact]
    public void Missing_difficulty_in_existing_copy_is_reported_without_replacing_user_edits()
    {
        string source = Directory.CreateDirectory(Path.Combine(root, "Original")).FullName;
        string songs = Directory.CreateDirectory(Path.Combine(root, "Songs")).FullName;
        string path = Path.Combine(source, "map.osu"); File.WriteAllText(path, "osu file format v14\n");
        string copy = MapsetWorkspace.Prepare(path, songs);
        File.Delete(copy);
        Assert.Throws<IOException>(() => MapsetWorkspace.Prepare(path, songs));
        Assert.False(File.Exists(copy));
        Assert.True(File.Exists(path));
    }
}

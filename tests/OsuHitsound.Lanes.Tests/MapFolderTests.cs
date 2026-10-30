using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class MapFolderTests
{
    [Theory]
    [InlineData("a.wav", true)]
    [InlineData(@"sub\a.wav", true)]
    [InlineData("sub/../a.wav", true)]
    [InlineData("../a.wav", false)]
    [InlineData(@"sub\..\..\a.wav", false)]
    [InlineData(@"\\server\share\a.wav", false)]
    [InlineData(@"C:\a.wav", false)]
    [InlineData("/a.wav", false)]
    [InlineData("", false)]
    [InlineData("..", false)]
    public void Only_paths_inside_the_folder_are_allowed(string path, bool inside) =>
        Assert.Equal(inside, MapFolder.IsInside(path));

    [Fact]
    public void Lookups_are_case_insensitive_and_return_the_name_on_disk()
    {
        var folder = new MapFolder(["Soft-HitClap2.WAV", @"sfx\Boom.ogg"]);
        Assert.Equal("Soft-HitClap2.WAV", folder.FindSample("soft-hitclap2"));
        Assert.Equal("sfx/Boom.ogg", folder.Find(@"SFX\boom.OGG"));
        Assert.Equal("sfx/Boom.ogg", folder.Find("x/../sfx/boom.ogg"));
        Assert.Null(folder.Find("missing.wav"));
    }
}

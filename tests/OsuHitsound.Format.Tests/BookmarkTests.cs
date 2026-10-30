using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Format.Tests;

public class BookmarkTests
{
    private static OsuFile Parse(string text) => OsuFile.Parse(Encoding.UTF8.GetBytes(text));

    private static string Text(OsuFile file) => Encoding.UTF8.GetString(file.ToBytes());

    private const string WithEditor = "osu file format v14\r\n\r\n[General]\r\nMode: 0\r\n\r\n[Editor]\r\nBookmarks: 1000,2500\r\nDistanceSpacing: 1\r\n\r\n[Metadata]\r\nTitle:x\r\n";

    [Fact]
    public void Reads_the_bookmarks_line()
    {
        Assert.Equal([1000, 2500], Parse(WithEditor).Bookmarks);
        Assert.Empty(Parse("osu file format v14\r\n\r\n[General]\r\nMode: 0\r\n").Bookmarks);
    }

    [Fact]
    public void Changing_bookmarks_rewrites_only_that_line()
    {
        var file = Parse(WithEditor);
        file.SetBookmarks([1000, 1750, 2500]);
        Assert.Equal(WithEditor.Replace("Bookmarks: 1000,2500", "Bookmarks: 1000,1750,2500"), Text(file));
    }

    [Fact]
    public void The_same_bookmarks_change_nothing()
    {
        var file = Parse(WithEditor);
        file.SetBookmarks([1000, 2500]);
        Assert.False(file.IsEdited);
    }

    [Fact]
    public void A_map_without_an_editor_section_gets_one_before_metadata()
    {
        var file = Parse("osu file format v14\r\n\r\n[General]\r\nMode: 0\r\n\r\n[Metadata]\r\nTitle:x\r\n");
        file.SetBookmarks([500]);
        Assert.Equal("osu file format v14\r\n\r\n[General]\r\nMode: 0\r\n\r\n[Editor]\r\nBookmarks: 500\r\n\r\n[Metadata]\r\nTitle:x\r\n", Text(file));
        Assert.Equal([500], Parse(Text(file)).Bookmarks);
    }

    [Fact]
    public void An_editor_section_without_bookmarks_gets_the_line_first()
    {
        var file = Parse("osu file format v14\r\n\r\n[Editor]\r\nDistanceSpacing: 1\r\n\r\n[Metadata]\r\n");
        file.SetBookmarks([10, 20]);
        Assert.Equal("osu file format v14\r\n\r\n[Editor]\r\nBookmarks: 10,20\r\nDistanceSpacing: 1\r\n\r\n[Metadata]\r\n", Text(file));
    }
}

using System.IO.Compression;
using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

/// <summary>New hitsound diff and .osz import (docs/spec-wave2.md, parts 8 and 9).</summary>
public class NewDiffAndOszTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hse-files-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private const string Map = "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\n\r\n[Editor]\r\nBookmarks: 1000,2000\r\n\r\n" +
                               "[Metadata]\r\nTitle:Song\r\nArtist:Band\r\nCreator:me\r\nVersion:Insane\r\nBeatmapID:12345\r\n\r\n" +
                               "[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n[Events]\r\n0,0,\"bg.jpg\",0,0\r\n\r\n" +
                               "[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n1000,-100,4,2,1,60,0,0\r\n2000,400,3,2,0,100,1,0\r\n\r\n" +
                               "[HitObjects]\r\n256,192,1000,1,8,0:0:0:0:\r\n";

    private string WriteMap()
    {
        var path = Path.Combine(dir, "Band - Song (me) [Insane].osu");
        File.WriteAllText(path, Map);
        return path;
    }

    // ---- New hitsound diff ----

    [Fact]
    public void A_new_diff_has_the_name_red_lines_only_no_objects_and_the_bookmarks()
    {
        var source = WriteMap();
        var path = HitsoundDiff.CreateEmpty(OsuFile.Read(source), source, "Hitsounds");

        Assert.Equal(Path.Combine(dir, "Band - Song (me) [Hitsounds].osu"), path);
        var diff = OsuFile.Read(path);
        var text = File.ReadAllText(path);
        Assert.Contains("Version:Hitsounds\r\n", text);
        Assert.Contains("BeatmapID:0\r\n", text);
        Assert.Equal(["0,500,4,2,0,100,1,0", "2000,400,3,2,0,100,1,0"], diff.TimingPoints.Select(p => p.Line.Text));
        Assert.Empty(diff.HitObjects);
        Assert.Equal([1000, 2000], diff.Bookmarks);
        Assert.Contains("AudioFilename: song.mp3\r\n", text); // other header lines as they were
        Assert.DoesNotContain("bg.jpg", text);                  // no [Events], as Generate's diff
        Assert.Equal(Map, File.ReadAllText(source)); // the open map is untouched
    }

    [Fact]
    public void A_new_diff_never_takes_a_name_or_file_already_there()
    {
        var source = WriteMap();
        Assert.Throws<IOException>(() => HitsoundDiff.CreateEmpty(OsuFile.Read(source), source, "Insane")); // the open map's own name
        HitsoundDiff.CreateEmpty(OsuFile.Read(source), source, "Hitsounds");
        Assert.Throws<IOException>(() => HitsoundDiff.CreateEmpty(OsuFile.Read(source), source, "hitsounds")); // osu! names ignore case
        Assert.Throws<ArgumentException>(() => HitsoundDiff.CreateEmpty(OsuFile.Read(source), source, "  "));
    }

    // ---- .osz import ----

    private string Osz(string name, params (string Entry, string Text)[] entries)
    {
        var path = Path.Combine(dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, text) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open());
            w.Write(text);
        }

        return path;
    }

    private string Songs() => Directory.CreateDirectory(Path.Combine(dir, "Songs")).FullName;

    [Fact]
    public void An_osz_unpacks_into_a_new_folder_named_after_it_and_a_clash_is_numbered()
    {
        var songs = Songs();
        var osz = Osz("123 Band - Song.osz", ("Band - Song (me) [Insane].osu", Map), ("sounds/soft-hitclap2.wav", "RIFF"));

        var first = OszImport.Extract(osz, songs);
        Assert.Equal(Path.Combine(songs, "123 Band - Song"), first);
        Assert.True(File.Exists(Path.Combine(first, "sounds", "soft-hitclap2.wav")));
        Assert.Equal([Path.Combine(first, "Band - Song (me) [Insane].osu")], OszImport.Difficulties(first));

        Assert.Equal(Path.Combine(songs, "123 Band - Song (2)"), OszImport.Extract(osz, songs)); // never merges
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("sub/../../evil.txt")]
    [InlineData("/evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("map.osu:hidden")]
    [InlineData("sounds/NUL.wav")]
    [InlineData("COM1")]
    public void An_unsafe_entry_stops_the_import_and_nothing_is_left_behind(string entry)
    {
        var songs = Songs();
        var osz = Osz("bad.osz", ("ok.osu", Map), (entry, "x"));

        Assert.Throws<InvalidDataException>(() => OszImport.Extract(osz, songs));
        Assert.Empty(Directory.EnumerateFileSystemEntries(songs));
        Assert.False(File.Exists(Path.Combine(dir, "evil.txt")));
    }

    [Fact]
    public void An_archive_name_ending_in_a_dot_still_imports()
    {
        var songs = Songs();
        var folder = OszImport.Extract(Osz("123 Band - Hello..osz", ("a.osu", Map)), songs); // folder "123 Band - Hello." would lose its dot
        Assert.Equal(Path.Combine(songs, "123 Band - Hello"), folder);
        Assert.Single(Directory.EnumerateDirectories(songs)); // the staging folder is gone
    }

    [Fact]
    public void An_osz_larger_than_the_limit_is_refused()
    {
        var songs = Songs();
        var osz = Osz("big.osz", ("a.osu", new string('x', 5000)));
        Assert.Throws<InvalidDataException>(() => OszImport.Extract(osz, songs, maxBytes: 1000));
        Assert.Empty(Directory.EnumerateFileSystemEntries(songs));
    }
}

using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Format.Tests;

/// <summary>The change summary shown before a write (issue #27, ADR 0002).</summary>
public class WriteSummaryTests
{
    private const string Map =
        "osu file format v14\r\n\r\n[Metadata]\r\nTitle:Song\r\nVersion:Insane\r\n\r\n" +
        "[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n1000,-100,4,2,0,60,0,0\r\n\r\n" +
        "[HitObjects]\r\n256,192,500,1,0,0:0:0:0:\r\n256,192,1500,1,0,0:0:0:0:\r\n256,192,2500,1,0,0:0:0:0:\r\n";

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static WriteSummary Summary(string after) => WriteSummary.Compare(Bytes(Map), Bytes(after));

    [Fact]
    public void The_same_bytes_change_nothing()
    {
        var summary = Summary(Map);
        Assert.True(summary.IsEmpty);
        Assert.True(summary.OnlyHitsounds);
        Assert.Equal("no changes", summary.Counts());
    }

    [Fact]
    public void An_edited_object_counts_once_and_only_as_an_object()
    {
        var summary = Summary(Map.Replace("256,192,1500,1,0,", "256,192,1500,1,8,"));

        Assert.Equal(1, summary.ObjectsChanged);
        Assert.Equal(0, summary.OtherLines);
        Assert.True(summary.OnlyHitsounds);
        var change = Assert.Single(summary.Changes);
        Assert.Equal(LineKind.HitObject, change.Kind);
        Assert.Equal("256,192,1500,1,0,0:0:0:0:", change.Before);
        Assert.Equal("256,192,1500,1,8,0:0:0:0:", change.After);
    }

    [Fact]
    public void Added_edited_and_removed_green_lines_are_counted_apart()
    {
        var after = Map
            .Replace("1000,-100,4,2,0,60,0,0\r\n", "1000,-100,4,2,0,80,0,0\r\n1500,-100,4,2,101,60,0,0\r\n");
        var summary = Summary(after);
        Assert.Equal((1, 1, 0), (summary.GreenLinesAdded, summary.GreenLinesEdited, summary.GreenLinesRemoved));
        Assert.Equal("1 green line added, 1 green line edited", summary.Counts());

        var removed = Summary(Map.Replace("1000,-100,4,2,0,60,0,0\r\n", ""));
        Assert.Equal(1, removed.GreenLinesRemoved);
        Assert.True(removed.OnlyHitsounds);
    }

    [Fact]
    public void A_red_line_change_is_an_other_line()
    {
        var summary = Summary(Map.Replace("0,500,4,2,0,100,1,0", "0,500,4,2,0,90,1,0"));
        Assert.Equal(1, summary.OtherLines);
        Assert.False(summary.OnlyHitsounds);
    }

    [Fact]
    public void A_line_that_only_changes_its_line_ending_counts()
    {
        var summary = Summary(Map.Replace("Title:Song\r\n", "Title:Song\n"));

        Assert.Equal(1, summary.OtherLines);
        var change = Assert.Single(summary.Changes);
        Assert.Equal("Title:Song", change.Before);
        Assert.Equal("Title:Song (LF)", change.After);
    }

    [Fact]
    public void A_lost_bom_counts_as_an_other_line()
    {
        var withBom = (byte[])[0xEF, 0xBB, 0xBF, .. Bytes(Map)];
        var summary = WriteSummary.Compare(withBom, Bytes(Map));
        Assert.Equal(1, summary.OtherLines);
        Assert.Equal("(UTF-8 BOM)", Assert.Single(summary.Changes).Before);
    }

    [Fact]
    public void Removed_and_added_objects_count_as_objects()
    {
        var summary = Summary(Map.Replace("256,192,2500,1,0,0:0:0:0:\r\n", "256,192,3000,1,0,0:0:0:0:\r\n256,192,3500,1,0,0:0:0:0:\r\n"));
        Assert.Equal(3, summary.ObjectsChanged); // 2500 removed, 3000 and 3500 added
        Assert.True(summary.OnlyHitsounds);
    }

    [Fact]
    public void A_changed_name_is_paired_as_one_edit()
    {
        var change = Assert.Single(Summary(Map.Replace("Version:Insane", "Version:Hitsounds")).Changes);
        Assert.Equal(("Metadata", "Version:Insane", "Version:Hitsounds"), (change.Section, change.Before, change.After));
    }

    [Fact]
    public void An_edited_file_is_compared_as_it_would_be_written()
    {
        var before = OsuFile.Parse(Bytes(Map));
        var after = OsuFile.Parse(Bytes(Map));
        after.HitObjects[0].HitSound = HitSound.Clap;
        after.InsertTimingPoint("2000,-100,4,2,0,40,0,0");

        var summary = WriteSummary.Compare(before, after);
        Assert.Equal("1 object, 1 green line added", summary.Counts());
    }

    [Fact]
    public void Repeated_identical_lines_are_matched_one_for_one()
    {
        var summary = Summary(Map.Replace("Title:Song\r\n", "Title:Song\r\n// note\r\n// note\r\n"));
        Assert.Equal(2, summary.OtherLines);
        Assert.Equal(0, Summary(Map).OtherLines);
    }
}

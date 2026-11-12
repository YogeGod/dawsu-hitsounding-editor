using System.Text;

namespace OsuHitsound.Format.Tests;

public class HitCircleEditingTests
{
    [Theory]
    [InlineData("\r\n", true)]
    [InlineData("\n", false)]
    public void Adding_and_removing_circles_preserves_existing_object_bytes(string newline, bool bom)
    {
        string text = "osu file format v14" + newline + "[HitObjects]" + newline + "123,45,2000,5,8,0:0:0:33:" + newline + "//tail" + newline;
        byte[] body = Encoding.UTF8.GetBytes(text);
        byte[] original = bom ? [.. Encoding.UTF8.Preamble, .. body] : body;
        var map = OsuFile.Parse(original);
        var first = map.InsertHitCircle(1000);
        Assert.Equal(2, map.HitObjects.Count);
        Assert.Contains("123,45,2000,5,8,0:0:0:33:" + newline, Encoding.UTF8.GetString(map.ToBytes()));
        map.RemoveHitObject(first);
        Assert.Equal(original, map.ToBytes());
    }

    [Theory]
    [InlineData("osu file format v14")]
    [InlineData("osu file format v14\n[HitObjects]")]
    public void Empty_maps_without_a_final_newline_can_receive_their_first_circle(string text)
    {
        var map = OsuFile.Parse(Encoding.UTF8.GetBytes(text));
        map.InsertHitCircle(1234);
        var reopened = OsuFile.Parse(map.ToBytes());
        Assert.Equal(1234, Assert.Single(reopened.HitObjects).Time);
        Assert.True(map.IsEdited);
    }

    // Appending (the fast path) and inserting between objects must agree after the latest time moves or the latest is removed.
    [Fact]
    public void Circles_go_before_the_first_later_object_whatever_order_they_are_added_in()
    {
        var map = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\r\n[HitObjects]\r\n256,192,3000,1,0,0:0:0:0:\r\n1,2,1000\r\n"));
        map.InsertHitCircle(4000); // after everything: appended
        map.InsertHitCircle(2000); // before 3000 (the file isn't sorted: 1000 comes after it)
        var latest = map.InsertHitCircle(5000);
        map.RemoveHitObject(latest);
        map.InsertHitCircle(4500); // the latest is 4000 again
        Assert.Equal([2000, 3000, 1000, 4000, 4500], map.HitObjects.Select(o => o.Time));
        Assert.Equal(map.HitObjects.Select(o => o.Line), map.Lines.Where(l => l.Section == "HitObjects" && l.Text.Contains(',')));
    }

    [Fact]
    public void Adding_thousands_of_circles_in_time_order_stays_fast()
    {
        var map = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\r\n[HitObjects]\r\n"));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 20000; i++) map.InsertHitCircle(i * 10);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"20000 circles took {watch.ElapsedMilliseconds} ms");
        Assert.Equal(20000, map.HitObjects.Count);
    }

    [Fact]
    public void An_unreadable_object_line_does_not_stop_a_circle_being_added()
    {
        var map = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\r\n[HitObjects]\r\n256,192,500,1,0,0:0:0:0:\r\n1,2,oops,1,0\r\n256,192,3000,1,0,0:0:0:0:\r\n"));
        map.InsertHitCircle(2000);
        Assert.Equal("osu file format v14\r\n[HitObjects]\r\n256,192,500,1,0,0:0:0:0:\r\n1,2,oops,1,0\r\n256,192,2000,1,0,0:0:0:0:\r\n256,192,3000,1,0,0:0:0:0:\r\n",
            Encoding.UTF8.GetString(map.ToBytes()));
    }

    [Theory]
    [InlineData("237,124,65060,2,0,P|304:134|336:121,1,90")] // ends after length, as old maps do
    [InlineData("174,5,141252,2,8,P|210:21|232:45,1,60,8|8")] // edgeSounds only
    public void Writing_slider_edge_fields_also_writes_the_default_hitSample(string line)
    {
        var map = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\r\n[HitObjects]\r\n" + line + "\r\n"));
        var slider = map.HitObjects[0];
        slider.SetNodeSound(1, HitSound.Whistle);
        slider.SetNodeSet(0, SampleSet.None, SampleSet.Drum);
        // Mapset Verifier reads a final edgeSets field as hitSample and then can't open the mapset at all.
        var written = Encoding.UTF8.GetString(map.ToBytes()).Split("\r\n")[2];
        Assert.EndsWith(",0:3|0:0,0:0:0:0:", written);
        Assert.Equal(HitSound.Whistle, OsuFile.Parse(map.ToBytes()).HitObjects[0].NodeSounds[1]);
    }
}

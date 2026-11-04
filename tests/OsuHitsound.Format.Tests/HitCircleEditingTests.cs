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
}

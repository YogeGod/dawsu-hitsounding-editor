using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class GenerateWriterTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hse-generate-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private GeneratePlan Plan(params string[] mixes)
    {
        var mapset = Path.Combine(dir, "mapset");
        Directory.CreateDirectory(mapset);
        var diff = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\r\n\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:0:0:\r\n"));
        return new GeneratePlan(Path.Combine(mapset, "a [Hitsounds].osu"), diff,
            [.. mixes.Select(m => new MixFile(m, [(new Lane(LaneSource.Skin, "soft-hitclap"), 100)]))], 1, []);
    }

    [Fact]
    public void Writes_the_mixes_and_the_diff_and_backs_up_a_replaced_diff()
    {
        var plan = Plan("soft-hitnormal4.wav");
        File.WriteAllText(plan.OsuPath, "old");

        var backup = GenerateWriter.Write(plan, _ => [1, 2, 3], Path.Combine(dir, "backups"));

        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(plan.OsuPath)!, "soft-hitnormal4.wav")));
        Assert.Contains("256,192,1000", File.ReadAllText(plan.OsuPath));
        Assert.Equal("old", File.ReadAllText(backup!));
        Assert.StartsWith(Path.Combine(dir, "backups"), backup);
    }

    [Fact]
    public void A_mix_that_cannot_be_rendered_stops_everything_before_any_file_is_written()
    {
        var plan = Plan("soft-hitnormal4.wav", "soft-hitnormal5.wav");
        Assert.Throws<IOException>(() => GenerateWriter.Write(plan, m => m.FileName.Contains('5') ? null : [1], Path.Combine(dir, "backups")));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(plan.OsuPath)!));
    }

    [Fact]
    public void An_existing_sample_file_is_never_overwritten()
    {
        var plan = Plan("soft-hitnormal4.wav");
        var taken = Path.Combine(Path.GetDirectoryName(plan.OsuPath)!, "soft-hitnormal4.wav");
        File.WriteAllText(taken, "mine");

        Assert.Throws<IOException>(() => GenerateWriter.Write(plan, _ => [1], Path.Combine(dir, "backups")));
        Assert.Equal("mine", File.ReadAllText(taken));
        Assert.False(File.Exists(plan.OsuPath));
    }
}

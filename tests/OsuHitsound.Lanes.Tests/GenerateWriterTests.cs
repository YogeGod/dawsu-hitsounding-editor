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

        var backup = GenerateWriter.Write(plan, _ => [1, 2, 3], Path.Combine(dir, "backups")).Backup;

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

    [Fact]
    public void An_unneeded_mix_of_ours_is_overwritten_or_removed_and_the_manifest_records_what_was_written()
    {
        var plan = Plan("soft-hitnormal4.wav") with { Removes = ["soft-hitnormal4.wav", "soft-hitnormal5.wav"] };
        var mapset = Path.GetDirectoryName(plan.OsuPath)!;
        File.WriteAllText(Path.Combine(mapset, "soft-hitnormal4.wav"), "old");
        File.WriteAllText(Path.Combine(mapset, "soft-hitnormal5.wav"), "old");

        var result = GenerateWriter.Write(plan, _ => [7], Path.Combine(dir, "backups"));

        Assert.Equal([7], File.ReadAllBytes(Path.Combine(mapset, "soft-hitnormal4.wav")));
        Assert.False(File.Exists(Path.Combine(mapset, "soft-hitnormal5.wav")));
        var mix = Assert.Single(result.Manifest.Mixes);
        Assert.Equal(("soft-hitnormal4.wav", GenerateManifest.Hash([7])), (mix.FileName, mix.Sha256));
    }

    [Fact]
    public void A_failure_puts_back_an_overwritten_mix_and_removes_nothing()
    {
        var plan = Plan("soft-hitnormal4.wav") with { Removes = ["soft-hitnormal4.wav", "soft-hitnormal5.wav"] };
        var mapset = Path.GetDirectoryName(plan.OsuPath)!;
        File.WriteAllText(Path.Combine(mapset, "soft-hitnormal4.wav"), "old");
        File.WriteAllText(Path.Combine(mapset, "soft-hitnormal5.wav"), "old");
        Directory.CreateDirectory(plan.OsuPath); // the diff can't be written: a folder is in its place

        Assert.ThrowsAny<SystemException>(() => GenerateWriter.Write(plan, _ => [7], Path.Combine(dir, "backups")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(mapset, "soft-hitnormal4.wav")));
        Assert.True(File.Exists(Path.Combine(mapset, "soft-hitnormal5.wav")));
    }

    [Fact]
    public void Manifest_round_trips_and_a_broken_one_reads_as_none()
    {
        var root = Path.Combine(dir, "generated");
        var manifest = new GenerateManifest([new GeneratedMix("soft-hitnormal4.wav", "sig", "AB")]);
        manifest.Save(root, @"C:\maps\a [Hitsounds].osu");
        Assert.Equal(manifest.Mixes, GenerateManifest.Load(root, @"C:\maps\a [Hitsounds].osu")!.Mixes);

        File.WriteAllText(ProjectStore.PathFor(root, @"C:\maps\b.osu"), "{ broken");
        Assert.Null(GenerateManifest.Load(root, @"C:\maps\b.osu"));
    }
}

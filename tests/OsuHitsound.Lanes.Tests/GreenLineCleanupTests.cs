using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

/// <summary>Clean up green lines (issue #29): only lines that change nothing go, and nothing plays differently.</summary>
public class GreenLineCleanupTests
{
    private static OsuFile Map(params string[] timing) => OsuFile.Parse(Encoding.UTF8.GetBytes(
        "osu file format v14\r\n\r\n[General]\r\nSampleSet: Normal\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
        "[TimingPoints]\r\n" + string.Concat(timing.Select(t => t + "\r\n")) +
        "\r\n[HitObjects]\r\n256,192,250,1,0,0:0:0:0:\r\n100,100,750,2,0,L|300:100,2,140\r\n256,192,2500,1,2,0:0:0:0:\r\n"));

    private const string Red = "0,500,4,1,0,100,1,0";

    [Fact]
    public void A_green_line_that_repeats_the_settings_in_force_is_removed()
    {
        var file = Map(Red, "500,-100,4,1,0,100,0,0", "1000,-50,4,1,0,100,0,0", "1500,-50,4,1,0,100,0,0");

        Assert.Equal([500.0, 1500.0], GreenLineCleanup.Redundant(file).Select(p => p.Time));
        Assert.Equal(2, GreenLineCleanup.Clean(file));
        Assert.Equal([0.0, 1000.0], file.TimingPoints.Select(p => p.Time));
    }

    [Theory]
    [InlineData("500,-100,4,2,0,100,0,0")]  // another sample set
    [InlineData("500,-100,4,1,3,100,0,0")]  // another index
    [InlineData("500,-100,4,1,0,60,0,0")]   // another volume
    [InlineData("500,-80,4,1,0,100,0,0")]   // another slider velocity
    [InlineData("500,-100,4,1,0,100,0,1")]  // kiai starts
    public void A_green_line_that_changes_any_one_setting_is_kept(string green)
    {
        Assert.Empty(GreenLineCleanup.Redundant(Map(Red, green)));
    }

    [Fact]
    public void A_set_of_zero_plays_soft_so_it_is_not_the_same_as_an_absent_one()
    {
        // The red line has no set field (map default: Normal); a green line with set 0 plays Soft, so it stays.
        Assert.Empty(GreenLineCleanup.Redundant(Map("0,500,4", "500,-100,4,0,0,100,0,0")));
    }

    [Fact]
    public void A_map_with_nothing_redundant_is_not_changed()
    {
        var file = Map(Red, "500,-80,4,1,0,100,0,0");
        Assert.Equal(0, GreenLineCleanup.Clean(file));
        Assert.False(file.IsEdited);
    }

    private static string Plays(OsuFile file)
    {
        var map = Decompiler.Decompile(file, new MapFolder([]));
        return string.Join('\n', map.Moments.Select(m => $"{m.Time}:{m.Volume}:{string.Join('+', m.Hits.Select(h => h.Lane.Name).Order())}")
            .Concat(map.Passive.Select(p => $"{p.Kind}@{p.Time}-{p.EndTime}:{p.Volume}:{p.Lane.Name}")));
    }

    public static TheoryData<string> PackFiles => new(
        Directory.EnumerateFiles(Path.Combine(CorpusFactAttribute.RepoRoot(), "tests", "fixtures", "save-pack"), "*.osu", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Path.Combine(CorpusFactAttribute.RepoRoot(), "tests", "fixtures", "save-pack"), f)));

    [Theory]
    [MemberData(nameof(PackFiles))]
    public void Cleaning_never_changes_what_any_hit_tick_or_slider_body_plays(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(CorpusFactAttribute.RepoRoot(), "tests", "fixtures", "save-pack", file));
        var cleaned = OsuFile.Parse(bytes);
        GreenLineCleanup.Clean(cleaned);
        Assert.Equal(Plays(OsuFile.Parse(bytes)), Plays(OsuFile.Parse(cleaned.ToBytes())));
    }

    [CorpusFact]
    public void Every_corpus_map_plays_the_same_after_cleaning()
    {
        var failures = new List<string>();
        int removed = 0;
        foreach (var path in Directory.EnumerateFiles(CorpusFactAttribute.Folder, "*.osu"))
        {
            var bytes = File.ReadAllBytes(path);
            var cleaned = OsuFile.Parse(bytes);
            removed += GreenLineCleanup.Clean(cleaned);
            if (Plays(OsuFile.Parse(bytes)) != Plays(OsuFile.Parse(cleaned.ToBytes()))) failures.Add(Path.GetFileName(path));
        }

        Assert.True(failures.Count == 0, $"{failures.Count} maps play differently: {string.Join(", ", failures.Take(10))}");
        Assert.True(removed > 0); // the corpus does have redundant lines, so the check above means something
    }
}

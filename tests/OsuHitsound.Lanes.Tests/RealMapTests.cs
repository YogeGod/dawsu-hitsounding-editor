using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

/// <summary>Real maps: the local corpus (tools/build-corpus.ps1) and the user's own Catalyst map, when present.</summary>
public class RealMapTests
{
    private const string Catalyst = @"F:\osu!\Songs\2507351 Arch Echo - Catalyst\Arch Echo - Catalyst (Yoge God) [Impetus].osu";

    [Fact]
    public void Every_corpus_map_decompiles()
    {
        var corpus = Path.Combine(RepoRoot(), "corpus");
        Assert.True(Directory.Exists(corpus), "corpus/ is missing. Run tools/build-corpus.ps1 to copy maps from your osu!lazer library.");

        var failures = new List<string>();
        int maps = 0;
        foreach (var path in Directory.EnumerateFiles(corpus, "*.osu"))
        {
            maps++;
            try
            {
                // The corpus holds .osu files only, so every custom sample resolves to the skin.
                var map = Decompiler.Decompile(OsuFile.Read(path), new MapFolder([]));
                if (map.Moments.Count == 0 && map.Settings.Mode == 0 && OsuFile.Read(path).HitObjects.Count > 0)
                    failures.Add($"{Path.GetFileName(path)}: no moments");
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(maps > 0);
        Assert.True(failures.Count == 0, $"{failures.Count} of {maps} maps failed:\n" + string.Join('\n', failures.Take(20)));
    }

    [Fact]
    public void Catalyst_spot_check()
    {
        if (!File.Exists(Catalyst)) return; // only on the author's machine

        var map = Decompiler.Decompile(Catalyst);

        // 1531 objects: circles, every slider head/repeat/tail and spinner ends.
        Assert.Equal(2218, map.Moments.Count);
        Assert.Empty(map.Warnings);
        Assert.Contains(map.Lanes, l => l is { Source: LaneSource.BeatmapFile, Name: "drum-hitclap2.ogg" });
        Assert.Contains(map.Passive, p => p.Kind == PassiveKind.SliderTick);

        // The first circle (4480 ms): soft hitnormal + drum clap at index 1, from the red line at 4480 (index 1, volume 40).
        var first = map.Moments[0];
        Assert.Equal(4480, first.Time);
        Assert.Equal(["soft-hitnormal.wav", "drum-hitclap.wav"], first.Hits.Select(h => h.Lane.Name));
        Assert.Equal(40, first.Volume);
    }

    [Fact]
    public void Catalyst_generates_a_hitsound_diff_that_plays_every_sound()
    {
        if (!File.Exists(Catalyst)) return; // only on the author's machine

        var file = OsuFile.Read(Catalyst);
        var folder = MapFolder.FromDirectory(Path.GetDirectoryName(Catalyst)!);
        var session = new EditSession(file, folder, Decompiler.Decompile(file, folder));

        var plan = HitsoundDiff.Plan(file, Catalyst, folder, session);

        int times = session.State.Hits.Select(h => (int)Math.Round(h.Time)).Distinct().Count();
        Assert.Equal(times, plan.Circles);
        Assert.Empty(plan.Conflicts);
        Assert.EndsWith("[Hitsounds].osu", plan.OsuPath);
        var after = new MapFolder(folder.Files.Concat(plan.Mixes.Select(m => m.FileName)));
        Assert.Equal(times, Decompiler.Decompile(plan.Diff, after).Moments.Count);
    }

    [Fact]
    public void Catalyst_compiles_unchanged_and_takes_slider_head_volumes_through_green_lines()
    {
        if (!File.Exists(Catalyst)) return; // only on the author's machine

        var file = OsuFile.Read(Catalyst);
        var folder = MapFolder.FromDirectory(Path.GetDirectoryName(Catalyst)!);
        Assert.Empty(Compiler.Apply(file, folder, new Dictionary<MomentKey, WantedSound>()).Conflicts);
        Assert.False(file.IsEdited);

        var map = Decompiler.Decompile(file, folder);
        var heads = map.Moments.Where(m => m.Position == HitPosition.SliderHead).ToList();
        var wanted = heads.ToDictionary(m => Compiler.KeyOf(file, m), m => Compiler.AsWanted(m) with { Volume = 50 });
        int pointsBefore = file.TimingPoints.Count;

        var result = Compiler.Apply(file, folder, wanted);

        Assert.DoesNotContain(result.Conflicts, c => c.Reason.Contains("report"));
        Assert.True(result.TimingPointsAdded > 0);
        Assert.Equal(pointsBefore + result.TimingPointsAdded, file.TimingPoints.Count);
        var after = Decompiler.Decompile(file, folder);
        var conflicted = result.Conflicts.Select(c => c.Moment).ToHashSet();
        Assert.All(after.Moments.Where(m => m.Position == HitPosition.SliderHead && !conflicted.Contains(Compiler.KeyOf(file, m))),
            m => Assert.Equal(50, m.Volume));
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any()) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repo root (*.slnx) not found.");
    }
}

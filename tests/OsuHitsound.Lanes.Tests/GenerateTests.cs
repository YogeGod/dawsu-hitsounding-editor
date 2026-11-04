using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class GenerateTests
{
    private const string Header =
        "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\nSampleSet: Soft\r\n\r\n[Metadata]\r\nTitle:Song\r\nArtist:Band\r\nCreator:Me\r\nVersion:Insane\r\nBeatmapID:123\r\n\r\n" +
        "[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n[Events]\r\n0,0,\"bg.jpg\",0,0\r\n\r\n[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n800,-50,4,2,0,40,0,0\r\n\r\n";

    private static (EditSession Session, OsuFile File, MapFolder Folder) Load(string hitObjects, string[]? files = null, Func<string, byte[]>? read = null)
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(Header + $"[HitObjects]\r\n{hitObjects.Replace("\n", "\r\n")}\r\n"));
        var folder = new MapFolder(files ?? [], read);
        return (new EditSession(file, folder, Decompiler.Decompile(file, folder)), file, folder);
    }

    private static Lane Skin(string name) => new(LaneSource.Skin, name);

    private static Lane File(string name) => new(LaneSource.BeatmapFile, name);

    private static readonly byte[] EmptyWav = [
        0x52, 0x49, 0x46, 0x46, 0x24, 0, 0, 0, 0x57, 0x41, 0x56, 0x45,
        0x66, 0x6d, 0x74, 0x20, 0x10, 0, 0, 0, 1, 0, 2, 0,
        0x44, 0xac, 0, 0, 0x10, 0xb1, 2, 0, 4, 0, 0x10, 0,
        0x64, 0x61, 0x74, 0x61, 0, 0, 0, 0,
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Default_generate_preserves_node_hits_and_volumes_without_rendering_slider_audio(bool pairing)
    {
        var (session, file, folder) = Load("256,192,1000,2,0,B|356:192,2,560,2|4|8\n256,192,4000,1,8,0:0:0:0:\n256,192,4500,8,4,5000");
        Assert.Contains(session.Map.Moments, m => m.Position == HitPosition.SliderHead);
        Assert.Contains(session.Map.Moments, m => m.Position == HitPosition.SliderRepeat);
        Assert.Contains(session.Map.Moments, m => m.Position == HitPosition.SliderTail);
        Assert.Contains(session.Map.Passive, p => p.Kind == PassiveKind.SliderTick);
        Assert.Contains(session.Map.Passive, p => p.Kind == PassiveKind.SliderSlide);
        var skin = session.State.Hits.Select(h => h.Lane.Name).Distinct().ToDictionary(n => n, _ => EmptyWav);
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, session, skin: skin, mergeSettings: new() { PairSamples = pairing });
        Assert.Empty(plan.Mixes);
        Assert.Empty(plan.Conflicts);
        var generated = Decompiler.Decompile(plan.Diff, folder);
        Assert.Equal(session.Map.Moments.Count, generated.Moments.Count);
        foreach (var original in session.Map.Moments)
        {
            var output = Assert.Single(generated.Moments, m => m.Time == Math.Round(original.Time - session.Map.Settings.TimeOffset));
            Assert.Equal(original.PlayedVolume, output.PlayedVolume);
            Assert.Equal(original.Hits.Select(h => h.Lane).OrderBy(l => l.Name), output.Hits.Select(h => h.Lane).OrderBy(l => l.Name));
        }
    }

    [Fact]
    public void Default_generate_also_excludes_explicit_slider_audio_lanes_but_keeps_loose_hits()
    {
        var (session, file, folder) = Load("256,192,1000,1,0,0:0:0:0:");
        session.Add([session.HitAt(Skin("soft-slidertick"), 2000), session.HitAt(Skin("soft-sliderslide"), 2100),
            session.HitAt(Skin("soft-sliderwhistle"), 2200), session.HitAt(Skin("soft-hitnormal"), 3000)]);
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, session);
        Assert.Equal(new double[] { 1000, 3000 }, plan.Diff.HitObjects.Select(o => o.Time));
        Assert.Empty(plan.Mixes);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void Empty_slider_loop_does_not_force_a_mix_but_ticks_and_audible_loops_remain()
    {
        var (session, file, folder) = Load("256,192,1000,2,0,B|356:192,1,560");
        var slide = Assert.Single(session.Map.Passive, p => p.Kind == PassiveKind.SliderSlide);
        var tick = Assert.Single(session.Map.Passive, p => p.Kind == PassiveKind.SliderTick);
        var blankSkin = new Dictionary<string, byte[]> { [slide.Lane.Name] = [.. EmptyWav, 1, 2] };
        var blank = HitsoundDiff.Plan(file, "map.osu", folder, session, skin: blankSkin, includeSliderAudio: true);
        Assert.DoesNotContain(blank.Mixes.SelectMany(m => m.Loops), l => l.Lane == slide.Lane);
        Assert.Contains(blank.Diff.HitObjects, o => o.Time == tick.Time);
        var audible = EmptyWav.ToArray();
        audible[40] = 4; // This data chunk is no longer empty.
        var nonempty = HitsoundDiff.Plan(file, "map.osu", folder, session, skin: new Dictionary<string, byte[]> { [slide.Lane.Name] = audible }, includeSliderAudio: true);
        Assert.Contains(nonempty.Mixes.SelectMany(m => m.Loops), l => l.Lane == slide.Lane);
        Assert.Contains(HitsoundDiff.Plan(file, "map.osu", folder, session, includeSliderAudio: true).Mixes.SelectMany(m => m.Loops), l => l.Lane == slide.Lane);
    }

    [Fact]
    public void Empty_map_slider_sample_is_skipped()
    {
        var text = Header.Replace("800,-50,4,2,0,40,0,0", "800,-50,4,2,1,40,0,0") + "[HitObjects]\r\n256,192,1000,2,0,B|356:192,1,560\r\n";
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(text));
        var folder = new MapFolder(["soft-sliderslide.wav"], _ => EmptyWav);
        var session = new EditSession(file, folder, Decompiler.Decompile(file, folder));
        var slide = Assert.Single(session.Map.Passive, p => p.Kind == PassiveKind.SliderSlide);
        Assert.Equal(LaneSource.BeatmapFile, slide.Lane.Source);
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, session, includeSliderAudio: true);
        Assert.DoesNotContain(plan.Mixes.SelectMany(m => m.Loops), l => l.Lane == slide.Lane);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void Generate_renders_slider_ticks_and_body_segments_as_timed_mixes()
    {
        var (session, file, folder) = Load("256,192,1000,2,0,B|356:192,1,560");
        var slide = Assert.Single(session.Map.Passive, p => p.Kind == PassiveKind.SliderSlide);
        var tick = Assert.Single(session.Map.Passive, p => p.Kind == PassiveKind.SliderTick);
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, session, includeSliderAudio: true);
        Assert.Contains(plan.Diff.HitObjects, o => o.Time == tick.Time);
        var loop = Assert.Single(plan.Mixes.SelectMany(m => m.Loops));
        Assert.Equal(slide.Lane, loop.Lane);
        Assert.Equal(slide.EndTime - slide.Time, loop.Duration);
        Assert.Contains(plan.Mixes, m => m.Layers.Any(l => l.Lane == tick.Lane));
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void Simultaneous_slider_ticks_and_body_loops_keep_both_voices()
    {
        var (session, file, folder) = Load("256,192,1000,2,0,B|356:192,1,560\n256,192,1000,2,0,B|356:192,1,560");
        var tick = session.Map.Passive.First(p => p.Kind == PassiveKind.SliderTick);
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, session, includeSliderAudio: true);
        Assert.Contains(plan.Mixes, m => m.Loops.Count == 2);
        Assert.Contains(plan.Mixes, m => m.Layers.Count(l => l.Lane == tick.Lane) == 2);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void Missing_source_files_remain_required_in_the_mix_instead_of_disappearing()
    {
        var (session, file, folder) = Load("256,192,1000,1,0,0:0:0:0:missing.wav");
        session.Add([session.HitAt(Skin("soft-hitclap"), 1000)]);
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, session);
        Assert.Contains(plan.Mixes.SelectMany(m => m.Layers), layer => layer.Lane.Name == "missing.wav" && layer.Lane.Missing);
    }

    [Fact]
    public void Generate_works_for_a_mapset_with_a_storyboard_file()
    {
        // The decompiler reads every .osb in the folder; the folder Generate plans against must still be able to read it
        // (it crashed with "This folder has no file contents").
        var osb = Encoding.UTF8.GetBytes("[Events]\r\nSample,2000,0,\"sb-boom.wav\",70\r\n");
        var (s, file, folder) = Load("256,192,1000,1,8,0:0:0:0:", ["Band - Song (Me).osb", "sb-boom.wav"], name => osb);
        s.Add([s.HitAt(Skin("soft-hitwhistle"), 1500)]);

        var plan = HitsoundDiff.Plan(file, @"C:\maps\x\Band - Song (Me) [Insane].osu", folder, s);

        Assert.Equal(2, plan.Circles);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void Every_sound_becomes_a_circle_in_a_hitsounds_diff_with_the_red_lines_only()
    {
        var (s, file, folder) = Load("256,192,1000,1,8,0:0:0:0:");
        s.Add([s.HitAt(Skin("soft-hitnormal"), 1500), s.HitAt(Skin("soft-hitwhistle"), 1500)]); // no object there

        var plan = HitsoundDiff.Plan(file, @"C:\maps\x\Band - Song (Me) [Insane].osu", folder, s);
        var text = Encoding.UTF8.GetString(plan.Diff.ToBytes());

        Assert.Equal(@"C:\maps\x\Band - Song (Me) [Hitsounds].osu", plan.OsuPath);
        Assert.Equal(2, plan.Circles);
        Assert.Empty(plan.Mixes);
        Assert.Empty(plan.Conflicts);
        Assert.Contains("Version:Hitsounds", text);
        Assert.Contains("BeatmapID:0", text);
        Assert.DoesNotContain("bg.jpg", text);
        Assert.DoesNotContain("800,-50", text); // green lines are not copied

        var moments = Decompiler.Decompile(plan.Diff, folder).Moments;
        Assert.Equal([1000.0, 1500.0], moments.Select(m => m.Time));
        Assert.Equal(["soft-hitclap", "soft-hitnormal"], moments[0].Hits.Select(h => h.Lane.Name).Order());
        Assert.Equal(["soft-hitnormal", "soft-hitwhistle"], moments[1].Hits.Select(h => h.Lane.Name).Order());
    }

    [Fact]
    public void Sounds_that_cannot_play_together_are_mixed_on_a_fresh_index_and_identical_mixes_share_a_file()
    {
        var files = new[] { "soft-hitnormal2.wav", "soft-hitclap3.wav" };
        var (s, file, folder) = Load("256,192,1000,1,0,0:0:0:0:\n256,192,2000,1,0,0:0:0:0:", files);
        foreach (int t in new[] { 1000, 2000 })
        {
            s.Remove(s.State.Hits.Where(h => Math.Abs(h.Time - t) < 1).ToList());
            s.Add([s.HitAt(File("soft-hitnormal2.wav"), t), s.HitAt(File("soft-hitclap3.wav"), t)]); // two indexes at once
        }

        s.Add([s.HitAt(Skin("drum-hitclap"), 1250)]); // an addition with no hitnormal

        var plan = HitsoundDiff.Plan(file, "map.osu", folder, s);

        Assert.Equal(2, plan.Mixes.Count);
        Assert.Equal(["soft-hitnormal4.wav", "soft-hitnormal5.wav"], plan.Mixes.Select(m => m.FileName).Order());
        Assert.Empty(plan.Conflicts);
        var both = plan.Mixes.Single(m => m.Layers.Count == 2);
        Assert.Equal(["soft-hitclap3.wav", "soft-hitnormal2.wav"], both.Layers.Select(l => l.Lane.Name).Order());

        var after = new MapFolder(files.Concat(plan.Mixes.Select(m => m.FileName)));
        var moments = Decompiler.Decompile(plan.Diff, after).Moments;
        Assert.Equal(3, moments.Count);
        Assert.All(moments, m => Assert.Single(m.Hits));
        Assert.Equal(moments[0].Hits[0].Lane, moments[2].Hits[0].Lane); // 1000 and 2000 share a mix
    }

    [Fact]
    public void Different_volumes_at_one_time_are_mixed()
    {
        var (s, file, folder) = Load("256,192,1000,1,0,0:0:0:0:");
        s.Add([s.HitAt(Skin("soft-hitclap"), 1000.4)]); // anchored to the circle: volume 100 like it
        s.Add([s.HitAt(Skin("soft-hitwhistle"), 1300)]);
        s.Add([s.HitAt(Skin("soft-hitnormal"), 1300)]);
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, s);
        Assert.Empty(plan.Mixes); // all at one volume per moment: no mix needed
    }

    [Fact]
    public void The_fresh_index_is_above_every_index_any_difficulty_or_file_uses()
    {
        var other = Encoding.UTF8.GetBytes(Header + "[HitObjects]\r\n256,192,1000,1,0,0:0:7:0:\r\n");
        var folder = new MapFolder(["soft-hitclap3.wav", "other.osu"], _ => other);
        Assert.Equal(8, HitsoundDiff.FirstFreeIndex(folder));
        Assert.Equal(2, HitsoundDiff.FirstFreeIndex(new MapFolder([])));
    }

    // ---- regenerating: reuse, refuse to touch changed files, remove what nothing uses

    private const string Diff = "Band - Song (Me) [Hitsounds].osu";
    private static readonly byte[] MixBytes = [1, 2, 3];

    /// <summary>A map whose drum clap at 1250 (no hitnormal) needs a mix, in a folder that already has the previous
    /// Generate's diff (using index 4) and its mix soft-hitnormal4.wav, plus any extra files.</summary>
    private static (EditSession Session, OsuFile File, MapFolder Folder) Regenerating(Dictionary<string, byte[]> extra, Lane? clap = null)
    {
        var oldDiff = Encoding.UTF8.GetBytes(Header + "[HitObjects]\r\n256,192,1250,1,0,0:0:4:0:\r\n");
        var bytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [Diff] = oldDiff, ["soft-hitnormal4.wav"] = MixBytes };
        foreach (var (k, v) in extra) bytes[k] = v;
        var (s, file, folder) = Load("256,192,1000,1,0,0:0:0:0:", [.. bytes.Keys], name => bytes[name]);
        s.Add([s.HitAt(clap ?? Skin("drum-hitclap"), 1250)]);
        return (s, file, folder);
    }

    private static GenerateManifest Previous(string signature, byte[]? bytes = null) =>
        new([new GeneratedMix("soft-hitnormal4.wav", signature, GenerateManifest.Hash(bytes ?? MixBytes))]);

    private static string ClapSignature => MixFile.SignatureOf([(Skin("drum-hitclap"), 100)]);

    [Fact]
    public void Regenerating_reuses_an_unchanged_mix_with_the_same_contents()
    {
        var (s, file, folder) = Regenerating([]);
        var skinBytes = new byte[] { 42 };
        var inputHash = GenerateManifest.Hash(Encoding.UTF8.GetBytes("mix-v2;" + GenerateManifest.Hash(skinBytes)));
        var previous = new GenerateManifest([new GeneratedMix("soft-hitnormal4.wav", ClapSignature, GenerateManifest.Hash(MixBytes)) { InputHash = inputHash }]);
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, s, previous, new Dictionary<string, byte[]> { ["drum-hitclap"] = skinBytes });

        Assert.Empty(plan.Mixes);
        Assert.Equal("soft-hitnormal4.wav", Assert.Single(plan.Reused).FileName);
        Assert.Empty(plan.Removes);
        var moments = Decompiler.Decompile(plan.Diff, folder).Moments;
        Assert.Contains(moments, m => m.Hits.Any(h => h.Lane.Name == "soft-hitnormal4.wav"));
    }

    [Fact]
    public void A_mix_file_changed_since_is_never_reused_or_removed()
    {
        var (s, file, folder) = Regenerating([]);
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, s, Previous(ClapSignature, bytes: [9, 9]));

        Assert.Empty(plan.Reused);
        Assert.Empty(plan.Removes);
        Assert.Equal("soft-hitnormal5.wav", Assert.Single(plan.Mixes).FileName);
    }

    [Fact]
    public void An_unneeded_mix_is_removed_but_new_mixes_continue_after_the_last_generated_index()
    {
        var (s, file, folder) = Regenerating([], clap: Skin("normal-hitclap")); // the mix now has other contents
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, s, Previous(ClapSignature));

        Assert.Equal(["soft-hitnormal4.wav"], plan.Removes);
        Assert.Equal("soft-hitnormal5.wav", Assert.Single(plan.Mixes).FileName);
    }

    [Fact]
    public void An_unneeded_mix_whose_index_another_difficulty_uses_is_kept()
    {
        var copied = Encoding.UTF8.GetBytes(Header + "[HitObjects]\r\n256,192,1250,1,0,0:0:4:0:\r\n"); // e.g. after Hitsound Copier
        var (s, file, folder) = Regenerating(new() { ["Band - Song (Me) [Insane].osu"] = copied }, clap: Skin("normal-hitclap"));
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, s, Previous(ClapSignature));

        Assert.Empty(plan.Removes);
        Assert.Equal("soft-hitnormal5.wav", Assert.Single(plan.Mixes).FileName);
    }

    [Fact]
    public void A_previous_generated_sample_used_by_unsaved_editor_notes_is_kept()
    {
        var (session, file, folder) = Regenerating([], clap: Skin("normal-hitclap"));
        session.Add([session.HitAt(File("soft-hitnormal4.wav"), 1500)]);
        var plan = HitsoundDiff.Plan(file, "map.osu", folder, session, Previous(ClapSignature));
        Assert.DoesNotContain("soft-hitnormal4.wav", plan.Removes);
    }
}

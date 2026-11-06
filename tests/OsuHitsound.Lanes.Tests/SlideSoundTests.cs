using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

/// <summary>Changing what a slider's body plays (slide, slide whistle, ticks), as osu!'s editor does on the whole slider.</summary>
public class SlideSoundTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hse-slide-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    // A soft red line; a slider with no edge fields (its nodes fall back to its own fields) and a slider with a whistle
    // on its tail only, then a circle.
    private string Map(string slider = "100,100,1000,2,0,L|300:100,1,280")
    {
        var path = Path.Combine(dir, "Band - Song (Me) [Insane].osu");
        File.WriteAllText(path,
            "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\nSampleSet: Soft\r\n\r\n[Metadata]\r\nTitle:Song\r\nArtist:Band\r\nCreator:Me\r\nVersion:Insane\r\n\r\n" +
            "[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:2\r\n\r\n[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n" +
            slider + "\r\n256,192,3000,1,0,0:0:0:0:\r\n");
        return path;
    }

    private static string[] Passive(DecompiledMap map, PassiveKind kind) =>
        [.. map.Passive.Where(p => p.Kind == kind).Select(p => p.Lane.Name).Distinct().Order()];

    private static string[] NodeLanes(DecompiledMap map) =>
        [.. map.Moments.Where(m => m.Object.Kind == HitObjectKind.Slider).SelectMany(m => m.Hits.Select(h => $"{m.Node}:{h.Lane.Name}"))];

    [Fact]
    public void A_drum_slide_shows_at_once_and_leaves_the_nodes_alone()
    {
        var doc = MapDocument.Load(Map());
        var nodes = NodeLanes(doc.Map);
        Assert.Equal(["soft-sliderslide"], Passive(doc.Map, PassiveKind.SliderSlide));

        doc.Session.SetSlides([0], s => s with { Normal = SampleSet.Drum });
        Assert.Equal(["drum-sliderslide"], Passive(doc.Map, PassiveKind.SliderSlide));
        Assert.Equal(["drum-slidertick"], Passive(doc.Map, PassiveKind.SliderTick));
        Assert.Equal(nodes, NodeLanes(doc.Map));
        Assert.True(doc.Session.IsEdited);
    }

    [Fact]
    public void Saving_writes_the_slider_fields_and_the_reopened_map_plays_the_same()
    {
        var path = Map();
        var doc = MapDocument.Load(path);
        var nodes = NodeLanes(doc.Map);
        doc.Session.SetSlides([0], s => new SlideSound(SampleSet.Drum, SampleSet.Normal, Whistle: true));
        doc.Save(Path.Combine(dir, "backups"));

        var reopened = MapDocument.Load(path);
        Assert.Equal(["drum-sliderslide"], Passive(reopened.Map, PassiveKind.SliderSlide));
        Assert.Equal(["normal-sliderwhistle"], Passive(reopened.Map, PassiveKind.SliderWhistle));
        Assert.Equal(nodes, NodeLanes(reopened.Map)); // the whistle flag doesn't reach the head or tail
        Assert.Equal(new SlideSound(SampleSet.Drum, SampleSet.Normal, true), reopened.Session.SlideOf(0));
    }

    [Fact]
    public void Nodes_with_their_own_entries_keep_them()
    {
        var path = Map("100,100,1000,2,0,L|300:100,1,280,0|2,0:0|0:0,0:0:0:0:");
        var doc = MapDocument.Load(path);
        var nodes = NodeLanes(doc.Map);
        doc.Session.SetSlides([0], s => s with { Normal = SampleSet.Normal, Whistle = true });
        doc.Save(Path.Combine(dir, "backups"));
        var reopened = MapDocument.Load(path);
        Assert.Equal(nodes, NodeLanes(reopened.Map));
        Assert.Equal(["normal-sliderslide"], Passive(reopened.Map, PassiveKind.SliderSlide));
    }

    [Fact]
    public void Undo_restores_the_slide_and_setting_it_back_is_no_edit()
    {
        var doc = MapDocument.Load(Map());
        doc.Session.SetSlides([0, 1], s => s with { Normal = SampleSet.Drum }); // object 1 is a circle: skipped
        Assert.Equal([0], doc.Session.State.Slides.Keys);
        doc.Session.Undo();
        Assert.Equal(["soft-sliderslide"], Passive(doc.Map, PassiveKind.SliderSlide));
        Assert.False(doc.Session.IsEdited);

        doc.Session.SetSlides([0], s => s with { Normal = SampleSet.Drum });
        doc.Session.SetSlides([0], s => s with { Normal = SampleSet.None });
        Assert.Empty(doc.Session.State.Slides);
    }

    [Fact]
    public void The_hs_save_path_writes_slides_too()
    {
        var path = Map();
        var doc = MapDocument.Load(path);
        doc.Session.SetSlides([0], s => s with { Normal = SampleSet.Drum });
        var (compiled, file) = doc.Compile(doc.Session.State);
        Assert.Empty(compiled.Conflicts);
        var map = Decompiler.Decompile(OsuFile.Parse(file.ToBytes()), doc.Folder);
        Assert.Equal(["drum-sliderslide"], Passive(map, PassiveKind.SliderSlide));
    }
}

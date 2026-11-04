using System.Text;
using Avalonia.Input;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public sealed class FeedbackWorkflowTests
{
    [Fact]
    public void Default_revert_binding_preserves_user_reassignment()
    {
        var gesture = new KeyGesture(Key.L, KeyModifiers.Control);
        var standard = Keymap.Build(KeyPreset.Osu, null, new Dictionary<string, IReadOnlyList<KeyGesture>>());
        Assert.Contains(gesture, standard["revert"]);
        var custom = Keymap.Build(KeyPreset.Osu, null, new Dictionary<string, IReadOnlyList<KeyGesture>> { ["play"] = new[] { gesture } });
        Assert.Contains(gesture, custom["play"]);
        Assert.DoesNotContain(gesture, custom["revert"]);
    }

    [Fact]
    public void Warning_visibility_is_independent_and_never_changes_hits()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Soft\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n256,192,1000,1,0,0:0:0:0:\n"));
        var session = new EditSession(file, new MapFolder([]), Decompiler.Decompile(file, new MapFolder([])));
        session.Add([session.HitAt(new Lane(LaneSource.Skin, "soft-hitclap"), 500)]);
        var model = new ArrangeModel(session, file, new HashSet<Lane>());
        var view = new ArrangeView { ExportProblems = new[] { new Conflict(1000, new(0, 0), "Unreadable sample") } };
        view.SetModel(model);
        var state = session.State;
        Assert.Single(view.VisibleWarnings());
        Assert.Equal(1000, view.VisibleWarnings().Single().Time);
        view.ShowMissingObjectWarnings = true;
        Assert.Equal(2, view.VisibleWarnings().Count());
        view.ShowExportWarnings = false;
        Assert.Equal(500, view.VisibleWarnings().Single().Time);
        view.ShowMissingObjectWarnings = false;
        Assert.Empty(view.VisibleWarnings());
        Assert.Same(state, session.State);
    }

    [Fact]
    public void Missing_remembered_sample_folder_falls_back_to_current_mapset()
    {
        string folder = Directory.CreateTempSubdirectory("sample-folder-").FullName;
        try
        {
            Assert.Equal(folder, MainWindow.SampleStartFolder(Path.Combine(folder, "missing"), folder));
            Assert.Equal(folder, MainWindow.SampleStartFolder(folder, null));
            Assert.Null(MainWindow.SampleStartFolder(null, null));
        }
        finally { Directory.Delete(folder); }
    }
}

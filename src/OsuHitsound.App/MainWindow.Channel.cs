using Avalonia.Platform.Storage;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private async Task BrowseChannelSample(Lane lane)
    {
        var current = doc;
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Choose sample for selected lane", AllowMultiple = false,
            FileTypeFilter = [new("Audio samples") { Patterns = ["*.wav", "*.ogg", "*.mp3"] }],
        });
        if (ReferenceEquals(doc, current) && files.FirstOrDefault()?.TryGetLocalPath() is { } path) AssignChannelFile(lane, path);
    }

    private void AssignChannelChoice(Lane lane, Lane choice)
    {
        if (choice.Source == LaneSource.Skin) { ReplaceChannelLane(lane, choice); return; }
        if (doc?.Folder.Find(choice.Name) is { } found)
            AssignChannelFile(lane, Path.Combine(Path.GetDirectoryName(doc.Path)!, found));
    }

    private void AssignChannelFile(Lane lane, string source)
    {
        if (doc is null || model is null || session is null) return;
        if (!model.CanReplaceSample(lane))
        {
            flash = "Slider tick, loop and storyboard samples are read-only. You can preview them here.";
            UpdateStatus();
            return;
        }
        try
        {
            string extension = Path.GetExtension(source).ToLowerInvariant();
            if (!HitsoundRules.SampleExtensions.Contains(extension)) throw new IOException("Choose a WAV, OGG or MP3 sample.");
            var fresh = MapFolder.FromDirectory(Path.GetDirectoryName(doc.Path)!);
            int index = HitsoundDiff.FirstFreeIndex(fresh);
            string slot = lane.Sample is { } sample ? HitsoundRules.SampleName(sample.Set, sample.Sound) : "soft-hitnormal";
            string name = $"{slot}{index}{extension}";
            File.Copy(source, Path.Combine(Path.GetDirectoryName(doc.Path)!, name), overwrite: false);
            doc.RescanFolder();
            ReplaceChannelLane(lane, new Lane(LaneSource.BeatmapFile, name));
            flash = $"Sample assigned: {name}. Existing sample files were kept.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { flash = $"Could not assign sample: {ex.Message}"; }
        UpdateStatus();
    }

    private void ReplaceChannelLane(Lane original, Lane replacement)
    {
        if (model is null || session is null || original == replacement || !model.CanReplaceSample(original)) return;
        string display = model.NameOf(original);
        model.AddLane(replacement);
        session.Move(session.State.Hits.Where(h => h.Lane == original).ToList(), 0, _ => replacement, "Replace lane sample");
        if (model.IsMuted(original) && !model.IsMuted(replacement)) model.ToggleMute(replacement);
        if (model.IsSoloed(original)) { model.ToggleSolo(original); if (!model.IsSoloed(replacement)) model.ToggleSolo(replacement); }
        model.RenameLane(replacement, display);
        model.HideLane(original);
        model.SetActive(replacement);
        if (doc is not null && replacement.Source == LaneSource.BeatmapFile && doc.Folder.Find(replacement.Name) is { } f)
            audio?.LoadLane(replacement, doc.Folder.ReadBytes(f));
        ScheduleProjectSave();
        RefreshInspector();
        SyncScrollBars();
    }
}

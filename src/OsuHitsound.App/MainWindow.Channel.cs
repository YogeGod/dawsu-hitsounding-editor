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
            SuggestedStartLocation = await SampleStartLocation(),
            FileTypeFilter = [new("Audio samples") { Patterns = ["*.wav", "*.ogg", "*.mp3"] }],
        });
        if (ReferenceEquals(doc, current) && files.FirstOrDefault()?.TryGetLocalPath() is { } path) { RememberSampleFolder(path); AssignChannelFile(lane, path); }
    }

    private void AssignChannelChoice(Lane lane, Lane choice)
    {
        if (!ArrangeModel.CanChooseChannelSample(choice)) return;
        if (lane == choice) return;
        if (choice.Source == LaneSource.Skin)
        {
            if (skin.Value.TryGetValue(choice.Name, out var bytes))
                ImportChannelSample(lane, ".wav", destination =>
                { using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write); stream.Write(bytes); });
            return;
        }
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
        ImportChannelSample(lane, Path.GetExtension(source), destination => File.Copy(source, destination, overwrite: false));
    }

    private void ImportChannelSample(Lane lane, string extension, Action<string> write)
    {
        if (doc is null || model is null || session is null || session.IsGestureActive || !model.CanReplaceSample(lane)) return;
        try
        {
            var fresh = MapFolder.FromDirectory(Path.GetDirectoryName(doc.Path)!);
            var slot = model.SlotOf(lane);
            string name = SampleSlots.BackingFile(fresh, slot, extension);
            write(Path.Combine(Path.GetDirectoryName(doc.Path)!, name));
            doc.RescanFolder();
            ReplaceChannelLane(lane, new Lane(LaneSource.BeatmapFile, name));
            flash = $"Sample assigned to {slot.Name}. Existing sample files were kept.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        { flash = $"Could not assign sample: {ex.Message}"; }
        UpdateStatus();
    }

    private void ReplaceChannelLane(Lane original, Lane replacement)
    {
        if (model is null || session is null || !model.ReplaceSample(original, replacement, preserveSlot: true)) return;
        if (doc is not null && replacement.Source == LaneSource.BeatmapFile && doc.Folder.Find(replacement.Name) is { } f)
            audio?.LoadLane(replacement, doc.Folder.ReadBytes(f));
        ScheduleProjectSave();
        RefreshInspector();
        SyncScrollBars();
    }
}

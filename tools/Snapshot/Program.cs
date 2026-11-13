// Renders the main screen headlessly to PNGs for visual checks, without sound: snapshot <map.osu> <outDir>
// Only reads the map; nothing is written next to it.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using OsuHitsound.App;
using OsuHitsound.Audio;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: snapshot <map.osu> <outDir> [width height]");
    return 1;
}

string outDir = args[0] == "--keysound" && args.Length > 2 ? args[2] : args[1];
Directory.CreateDirectory(outDir);
UiTheme.Current = UiTheme.Load(UiTheme.DefaultPath());

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

// snapshot --keysound <map.osu> <outDir>: Keysound mode at several window widths, from a project made in a temp folder.
if (args[0] == "--keysound") return KeysoundShots(args[1], outDir);

// Before a map is opened: the default lanes in their sampleset groups.
var empty = new MainWindow(null, startAudio: false, showInspector: false, usePreferences: false) { Width = 1920, Height = 1080 };
empty.Show();
while (!empty.Loading.IsCompleted) Dispatcher.UIThread.RunJobs();
for (int i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
AvaloniaHeadlessPlatform.ForceRenderTimerTick();
empty.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "00-no-map.png"));
empty.Close();

var window = new MainWindow(args[0], startAudio: false, showInspector: false, usePreferences: false)
{
    Width = args.Length > 2 ? int.Parse(args[2]) : 1920,
    Height = args.Length > 3 ? int.Parse(args[3]) : 1080,
};
window.Show();
while (!window.Loading.IsCompleted) Dispatcher.UIThread.RunJobs();
// There's no audio output here, so decode the song's waveform directly ("no sound" device: decoding only).
if (window.ArrangeArea.Model?.Map.Settings.AudioFilename is { Length: > 0 } songName
    && Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, songName) is var songPath && File.Exists(songPath)
    && ManagedBass.Bass.Init(0))
    window.ArrangeArea.Wave = Waveform.Decode(File.ReadAllBytes(songPath));
Pump();
Save("01-open.png");
SaveReadmeCrops(); // the map as opened, before the edits below

var view = window.ArrangeArea;
view.Axis.ZoomAt(0.4, 0);
Pump();
Save("02-zoomed-out.png");

var model = view.Model!;
model.ToggleCollapsed(model.LaneRows[0].Group);
model.ToggleSolo(model.LaneRows[^1].Lane);
model.ToggleSolo(model.LaneRows[^1].Lane); // solo off again for the editing shots
view.Playhead = view.Axis.Left + view.Axis.VisibleDuration / 3;
Pump();
Save("03-collapsed-solo.png");

// Editing: remove the first moment's hitnormal (a conflict: osu! always plays one) and draw a clap between objects
// (no object there). Both should show red after the background compile.
var session = model.Session;
var first = session.Moments[0];
session.Remove(session.State.Hits.Where(h => h.Anchor == first.Key && h.Lane.Name.Contains("hitnormal")).ToList());
var clap = model.LaneRows.First(r => r.Lane.Name.Contains("hitclap")).Lane;
session.Add([session.HitAt(clap, (session.Moments[0].Moment.Time + session.Moments[1].Moment.Time) / 2)]);
view.Axis.Left = first.Moment.Time - 300;
for (int i = 0; i < 20; i++)
{
    Thread.Sleep(50);
    Pump();
}

view.SetLocators(first.Moment.Time + 200, first.Moment.Time + 1400);
view.Cycle = true;
session.AddBookmark(first.Moment.Time + 800);
Pump();
model.SetActive(model.LaneRows.First(r => r.Group == OsuHitsound.Lanes.LaneGroup.NormalSet).Lane); // the lane touched last: highlighted
Pump();
Save("04-edited.png");
Console.WriteLine("status: " + window.FindControl<Avalonia.Controls.TextBlock>("StatusLeft")!.Text);

// The tutorial: a few of its steps, to check each spotlight and arrow lands on its control.
foreach (int step in new[] { 1, 9, 16, 22, 24, 25, 29 })
{
    window.ShowTutorialStepForSnapshot(step);
    Pump();
    Save($"08-tutorial-{step:00}.png");
}

window.CloseTutorialForSnapshot();
Pump();

// The tool cursors' pictures.
foreach (var kind in new[] { ToolCursor.Pen, ToolCursor.Eraser })
    ToolCursors.Picture(kind).Save(Path.Combine(outDir, $"09-cursor-{kind}.png".ToLowerInvariant()));

// The UI inspector: change two values the way its editors do and check they show at once (the theme is in memory only).
window.ToggleInspector();
UiTheme.Current.Set("lane.tint", "0.5");
UiTheme.Current.Set("control.bg", "#b8c4d6");
Pump();
Save("05-theme-edited.png");
var inspector = window.OwnedWindows.OfType<Avalonia.Controls.Window>().FirstOrDefault();
inspector?.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "06-inspector.png"));

// F1 help, then the same filtered by a search.
window.ShowHelpForSnapshot();
Pump();
var helpWindow = window.OwnedWindows.OfType<HelpWindow>().First();
helpWindow.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "07-help.png"));

// The map preview at the playhead: a few moments from the first object on, so circles, sliders and approach circles show.
if (model.Objects.Count > 0)
{
    double firstObject = model.Objects[0].Start;
    // lazer's pieces (approach circle, Torus digits) load in the background: give them a moment.
    for (int i = 0; i < 60 && !ArgonPieces.HasDigits; i++) { Thread.Sleep(50); Pump(); }
    double[] moments = args.Length > 4 ? [.. args[4].Split(',').Select(double.Parse)] : [0.0, 2500, 10200, 30450];
    foreach (var (offset, n) in moments.Select((o, n) => (o, n)))
    {
        window.ShowMapPreviewForSnapshot(firstObject + offset);
        if (args.Length > 5) window.SetPreviewSkinForSnapshot(args[5]); // "classic" or a skin folder
        var previewWindow = window.OwnedWindows.OfType<PlayfieldWindow>().First();
        (previewWindow.Width, previewWindow.Height) = (960, 720);
        Pump();
        window.OwnedWindows.OfType<PlayfieldWindow>().First().CaptureRenderedFrame()?.Save(Path.Combine(outDir, $"13-map-preview-{n}.png"));
    }

    Console.WriteLine("saved 13-map-preview-*.png");
    window.OwnedWindows.OfType<PlayfieldWindow>().First().Close();
}

// Map → Copy hitsounds…, on the map's own mapset (for the README).
var mapDir = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
var difficulties = Directory.GetFiles(mapDir, "*.osu").Select(p => (p, OsuHitsound.Lanes.HitsoundDiff.VersionOf(p) ?? Path.GetFileNameWithoutExtension(p))).OrderBy(d => d.Item2).ToList();
_ = Dialogs.CopyHitsounds(window, difficulties, Path.GetFullPath(args[0]));
Pump();
if (window.OwnedWindows.OfType<Window>().LastOrDefault(w => w.Title == "Copy hitsounds") is { } copyDialog)
{
    Pump();
    copyDialog.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "readme", "copy-hitsounds.png"));
    Console.WriteLine("saved readme/copy-hitsounds.png");
    copyDialog.Close();
}

// The change summary that follows it: the map copied into its other difficulties, planned only (nothing is written).
var others = difficulties.Select(d => d.Item1).Where(p => !string.Equals(p, Path.GetFullPath(args[0]), StringComparison.OrdinalIgnoreCase)).ToList();
if (others.Count > 0)
{
    var batch = OsuHitsound.Lanes.HitsoundCopier.PlanCopy(OsuHitsound.Format.OsuFile.Read(args[0]), OsuHitsound.Lanes.MapFolder.FromDirectory(mapDir),
        others, new OsuHitsound.Lanes.HitsoundCopyOptions(), canRender: true);
    _ = Dialogs.CopySummary(window, batch);
    Pump();
    if (window.OwnedWindows.OfType<Window>().LastOrDefault(w => w.Title == "Copy hitsounds: what will change") is { } summaryDialog)
    {
        Pump();
        summaryDialog.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "12-copy-summary.png"));
        Console.WriteLine("saved 12-copy-summary.png");
        summaryDialog.Close();
    }
}

// The lane list with its names hidden (the header's collapse button), then back.
window.ToggleLaneNamesForSnapshot();
Pump();
Save("11-names-collapsed.png");
window.ToggleLaneNamesForSnapshot();
window.Close();
return 0;

void Pump()
{
    for (int i = 0; i < 5; i++)
        Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
}

void Save(string name)
{
    window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, name));
    Console.WriteLine($"saved {name}");
}

// The README tutorial's pictures: small crops around the spots of the tour steps, so each shows only its own controls.
void SaveReadmeCrops()
{
    var readmeDir = Directory.CreateDirectory(Path.Combine(outDir, "readme")).FullName;
    var full = Path.Combine(outDir, "10-readme-full.png");
    window.CaptureRenderedFrame()?.Save(full);
    using var frame = SkiaSharp.SKBitmap.Decode(full);
    (string Name, int[] Steps)[] crops =
    [
        ("open-save-generate", [1, 2, 3]), ("menus", [4]), ("map-preferences-keys", [5, 6, 7]), ("undo-redo", [8]),
        ("tools", [9, 10, 11, 12, 13, 14]), ("snap", [15, 16]), ("view-colour-zoom", [17, 18, 19]),
        ("selection-bar", [20]), ("song", [21]), ("inspector", [22]), ("lanes-grid", [23, 24, 25]), ("waveform", [26]),
        ("volume", [27]), ("transport-position", [28, 29]), ("speed", [30]), ("global-volume", [31]),
    ];
    foreach (var (name, steps) in crops)
    {
        // Each step's spot as the tour draws it: 6 px around the control.
        var spots = steps.Select(window.TutorialSpotForSnapshot).OfType<Rect>().Select(r => r.Inflate(6)).ToList();
        if (spots.Count == 0) continue;
        var area = spots.Aggregate((a, b) => a.Union(b));
        // Room for the outline; the lanes and grid would be the whole screen, so they stop at 1200×640.
        int left = Math.Max(0, (int)area.X - 8), top = Math.Max(0, (int)area.Y - 8);
        int right = Math.Min(frame.Width, Math.Min((int)area.Right + 8, left + 1200));
        int bottom = Math.Min(frame.Height, Math.Min((int)area.Bottom + 8, top + 640));
        using var subset = new SkiaSharp.SKBitmap();
        frame.ExtractSubset(subset, new SkiaSharp.SKRectI(left, top, right, bottom));
        using var crop = subset.Copy();

        // The tour's highlight: everything around the spots dimmed, each spot outlined in blue.
        using (var canvas = new SkiaSharp.SKCanvas(crop))
        using (var holes = new SkiaSharp.SKPath())
        using (var outline = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColor.Parse("#0a84ff"), Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 2.5f, IsAntialias = true })
        {
            var inCrop = spots.Select(s => new SkiaSharp.SKRect((float)s.Left - left, (float)s.Top - top, (float)s.Right - left, (float)s.Bottom - top)).ToList();
            foreach (var s in inCrop) holes.AddRoundRect(s, 6, 6);
            canvas.Save();
            canvas.ClipPath(holes, SkiaSharp.SKClipOperation.Difference, true);
            canvas.DrawColor(new SkiaSharp.SKColor(0, 0, 0, 158), SkiaSharp.SKBlendMode.SrcOver); // the tour's 62 % black, over the screen
            canvas.Restore();
            foreach (var s in inCrop) canvas.DrawRoundRect(s, 6, 6, outline);
        }

        using var png = SkiaSharp.SKImage.FromBitmap(crop).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(readmeDir, name + ".png"), png.ToArray());
        Console.WriteLine($"saved readme/{name}.png ({right - left}×{bottom - top})");
    }
}

// Keysound mode, as the Hitsound shots: a throwaway project from the map with a few notes, at three window widths.
static int KeysoundShots(string osu, string outDir)
{
    var root = Path.Combine(Path.GetTempPath(), "dawsu-ks-snapshot-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var project = OsuHitsound.Lanes.Keysound.KeysoundStore.Create(osu, OsuHitsound.Lanes.Keysound.KeysoundStore.SuggestFolder(root, osu), new List<string>());
        var doc = OsuHitsound.Lanes.Keysound.KeysoundStore.Open(project);
        var track = doc.Session.State.Tracks.FirstOrDefault() ?? doc.Session.AddTrack("Track 1");
        long ppq = OsuHitsound.Lanes.Keysound.TempoMap.Ppq, at = doc.Tempo.RoundTick(doc.Objects.Count > 0 ? doc.Objects[0].Start : 0);
        int[] melody = [60, 64, 67, 72, 71, 67, 64, 62, 60, 65, 69, 72];
        doc.Session.AddNotes(track.Id, [.. melody.Select((p, i) => new OsuHitsound.Lanes.Keysound.KeyNote(Guid.NewGuid(), at + i * ppq / 2, ppq / 2, p, 60 + i * 5))]);
        doc.Session.AddBookmark(doc.Tempo.TimeAt(at + 4 * ppq), "Chorus");
        OsuHitsound.Lanes.Keysound.KeysoundStore.Save(doc);

        foreach (var (w, h) in new[] { (1920, 1080), (1280, 800), (1000, 700) })
        {
            var window = new MainWindow(project, startAudio: false, showInspector: false, usePreferences: false) { Width = w, Height = h };
            window.Show();
            while (!window.Loading.IsCompleted) Dispatcher.UIThread.RunJobs();
            for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
            var file = Path.Combine(outDir, $"ks-{w}.png");
            window.CaptureRenderedFrame()?.Save(file);
            Console.WriteLine("saved " + file);
            window.Close();
        }

        return 0;
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

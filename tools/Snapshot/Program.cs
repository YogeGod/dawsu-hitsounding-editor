// Renders the main screen headlessly to PNGs for visual checks, without sound: snapshot <map.osu> <outDir>
// Only reads the map; nothing is written next to it.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using OsuHitsound.App;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: snapshot <map.osu> <outDir> [width height]");
    return 1;
}

string outDir = args[1];
Directory.CreateDirectory(outDir);
UiTheme.Current = UiTheme.Load(UiTheme.DefaultPath());

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

var window = new MainWindow(args[0], startAudio: false, showInspector: false, usePreferences: false)
{
    Width = args.Length > 2 ? int.Parse(args[2]) : 1920,
    Height = args.Length > 3 ? int.Parse(args[3]) : 1080,
};
window.Show();
while (!window.Loading.IsCompleted) Dispatcher.UIThread.RunJobs();
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
        ("selection-bar", [20]), ("song", [21]), ("inspector", [22]), ("lanes-grid", [23, 24, 25]),
        ("volume", [26]), ("transport-position", [27, 28]), ("global-volume", [29]),
    ];
    foreach (var (name, steps) in crops)
    {
        Rect? spot = null;
        foreach (int step in steps)
            if (window.TutorialSpotForSnapshot(step) is { } r) spot = spot is { } s ? s.Union(r) : r;
        if (spot is not { } area) continue;
        // A little room around the controls; the lanes and grid would be the whole screen, so they stop at 1200×640.
        int left = Math.Max(0, (int)area.X - 8), top = Math.Max(0, (int)area.Y - 8);
        int right = Math.Min(frame.Width, Math.Min((int)area.Right + 8, left + 1200));
        int bottom = Math.Min(frame.Height, Math.Min((int)area.Bottom + 8, top + 640));
        using var crop = new SkiaSharp.SKBitmap();
        frame.ExtractSubset(crop, new SkiaSharp.SKRectI(left, top, right, bottom));
        using var png = SkiaSharp.SKImage.FromBitmap(crop).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(readmeDir, name + ".png"), png.ToArray());
        Console.WriteLine($"saved readme/{name}.png ({right - left}×{bottom - top})");
    }
}

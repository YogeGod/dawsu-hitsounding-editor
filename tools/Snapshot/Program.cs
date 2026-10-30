// Renders the main screen headlessly to PNGs for visual checks, without sound: snapshot <map.osu> <outDir>
// Only reads the map; nothing is written next to it.
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using OsuHitsound.App;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: snapshot <map.osu> <outDir>");
    return 1;
}

string outDir = args[1];
Directory.CreateDirectory(outDir);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

var window = new MainWindow(args[0], startAudio: false, showInspector: false) { Width = 1920, Height = 1080 };
window.Show();
Pump();
Save("01-open.png");

var view = window.ArrangeArea;
view.Axis.ZoomAt(0.4, 0);
Pump();
Save("02-zoomed-out.png");

var model = view.Model!;
model.ToggleCollapsed(model.LaneRows[0].Group);
model.ToggleSolo(model.LaneRows[^1].Lane);
view.Playhead = view.Axis.Left + view.Axis.VisibleDuration / 3;
Pump();
Save("03-collapsed-solo.png");

// The UI inspector: change two values the way its editors do and check they show at once (the theme is in memory only).
window.ToggleInspector();
UiTheme.Current.Set("lane.tint", "0.5");
UiTheme.Current.Set("control.bg", "#b8c4d6");
Pump();
Save("04-theme-edited.png");
var inspector = window.OwnedWindows.OfType<Avalonia.Controls.Window>().FirstOrDefault();
inspector?.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "05-inspector.png"));

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

// Renders the main screen headlessly to PNGs for visual checks, without sound: snapshot <map.osu> <outDir>
// Only reads the map; nothing is written next to it.
using Avalonia;
using Avalonia.Controls;
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
Pump();
Save("04-edited.png");
Console.WriteLine("status: " + window.FindControl<Avalonia.Controls.TextBlock>("StatusLeft")!.Text);

// The UI inspector: change two values the way its editors do and check they show at once (the theme is in memory only).
window.ToggleInspector();
UiTheme.Current.Set("lane.tint", "0.5");
UiTheme.Current.Set("control.bg", "#b8c4d6");
Pump();
Save("05-theme-edited.png");
var inspector = window.OwnedWindows.OfType<Avalonia.Controls.Window>().FirstOrDefault();
inspector?.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "06-inspector.png"));

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

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using OsuHitsound.App;

string root = Path.GetFullPath(args[0]);
Directory.CreateDirectory(root);
// Usage: EditorCheck <output folder> <song.wav>. Drives the built-in detector through the real dialogs.
File.Copy(Path.GetFullPath(args[1]), Path.Combine(root, "song.wav"), overwrite: true);
string map = Path.Combine(root, "editor-check.osu");
File.WriteAllText(map, """
    osu file format v14
    [General]
    AudioFilename:song.wav
    SampleSet:Soft
    Mode:0
    [Metadata]
    Title:Drum auto-fill check
    Version:Test
    [TimingPoints]
    0,500,4,2,0,45,1,0
    [HitObjects]
    100,100,1000,1,0,0:0:0:0:
    100,100,1500,1,0,0:0:0:0:
    """);
var original = File.ReadAllBytes(map);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var w = new MainWindow(map, startAudio: false, showInspector: false, usePreferences: false) { Width = 1040, Height = 720 };
w.Show();
Wait(() => w.Loading.IsCompleted);
Check(w.Loading.IsCompletedSuccessfully, "map opens");
var session = w.ArrangeArea.Model!.Session;
var before = session.State;
int steps = session.StepCount;
Check(w.FindControl<MenuItem>("DrumAutoFillItem") is not null, "Edit menu entry exists");
var task = (Task)typeof(MainWindow).GetMethod("ShowDrumAutoFill", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, null)!;
Wait(() => w.OwnedWindows.Any());
var options = w.OwnedWindows.Last();
options.GetLogicalDescendants().OfType<CheckBox>().Single(c => c.Name == "AutoFillUnmatched").IsChecked = true;
Check(options.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Name == "AutoFillKick").SelectedIndex > 0, "kick destination defaults to a sample lane");
Check(options.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Name == "AutoFillSnare").SelectedIndex > 0, "snare destination defaults to a sample lane");
options.CaptureRenderedFrame()!.Save(Path.Combine(root, "options.png"));
Click(options, "Analyse");
Wait(() => w.OwnedWindows.Any(p => p.Title == "Review Drum Hits auto-fill" || p.GetLogicalDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "OK")) || task.IsCompleted);
if (w.OwnedWindows.Any(p => p.GetLogicalDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "OK")))
    throw new Exception(string.Join(" ", w.OwnedWindows.Last().GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text)));
Check(!task.IsCompleted, "built-in detector reaches preview");
var preview = w.OwnedWindows.Last(p => p.Title == "Review Drum Hits auto-fill");
preview.CaptureRenderedFrame()!.Save(Path.Combine(root, "preview.png"));
Check(preview.GetLogicalDescendants().OfType<CheckBox>().Any(), "preview contains selectable proposed hits");
var proposed = preview.GetLogicalDescendants().OfType<CheckBox>().ToArray();
proposed[0].IsChecked = false;
Click(preview, "Apply selected hits");
Wait(() => task.IsCompleted);
task.GetAwaiter().GetResult();
Check(session.State.Hits.Count > before.Hits.Count, "Apply adds detected hits");
Check(session.State.Hits.Count == before.Hits.Count + proposed.Length - 1, "unchecked preview hit is excluded");
Check(session.StepCount == steps + 1, "Apply creates exactly one Undo step");
Check(ReferenceEquals(session.State.Volumes, before.Volumes), "existing volume data is preserved");
Check(File.ReadAllBytes(map).SequenceEqual(original), "Apply does not write the map");
session.Undo();
Check(ReferenceEquals(session.State, before), "Undo restores the complete prior state");
session.Redo();
Check(session.State.Hits.Count > before.Hits.Count, "Redo restores added hits");
session.Undo();
task = (Task)typeof(MainWindow).GetMethod("ShowDrumAutoFill", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, null)!;
Wait(() => w.OwnedWindows.Any());
Click(w.OwnedWindows.Last(), "Analyse");
// Analysis is fast, so cancel from whichever window is showing: the progress window or the review.
Wait(() => w.OwnedWindows.Any(p => p.Title is "Drum Hits auto-fill" or "Review Drum Hits auto-fill"));
Click(w.OwnedWindows.Last(), "Cancel");
Wait(() => task.IsCompleted);
Check(ReferenceEquals(session.State, before), "cancelling detection leaves edits intact");
task = (Task)typeof(MainWindow).GetMethod("ShowDrumAutoFill", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, null)!;
Wait(() => w.OwnedWindows.Any());
w.OwnedWindows.Last().GetLogicalDescendants().OfType<CheckBox>().Single(c => c.Name == "AutoFillUnmatched").IsChecked = true;
Click(w.OwnedWindows.Last(), "Analyse");
Wait(() => w.OwnedWindows.Any(p => p.Title == "Review Drum Hits auto-fill"));
session.SetVolume([session.Moments.First().Key], 62);
var changed = session.State;
Click(w.OwnedWindows.Last(), "Apply selected hits");
Wait(() => w.OwnedWindows.Any(p => p.GetLogicalDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "OK")));
Click(w.OwnedWindows.Last(), "OK");
Wait(() => task.IsCompleted);
Check(ReferenceEquals(session.State, changed), "changed edits reject stale analysis without adding hits");
session.Undo();
w.CaptureRenderedFrame()!.Save(Path.Combine(root, "editor.png"));
w.Close(); Pump();
Console.WriteLine("PASS: editor auto-fill integration");

void Pump() => Dispatcher.UIThread.RunJobs();
void Wait(Func<bool> condition)
{
    var end = DateTime.UtcNow.AddSeconds(60);
    while (!condition() && DateTime.UtcNow < end) { Pump(); Thread.Sleep(10); }
    Pump();
    if (!condition()) throw new Exception("Timed out waiting for editor flow. Owned windows: " + string.Join(", ", w.OwnedWindows.Select(p => p.Title)));
}
void Click(Window window, string label) => window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content?.ToString() == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS: " + message); }

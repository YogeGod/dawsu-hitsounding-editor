using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using OsuHitsound.App;
using OsuHitsound.Lanes;

string artifacts = Path.GetFullPath(args[0]);
Directory.CreateDirectory(artifacts);
UiTheme.Current = UiTheme.Load(Path.Combine(AppContext.BaseDirectory, "ui-theme.json"));
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
int checks = 0, serial = 0;
var clean = Open(); clean.Close(); Pump(); Check(!clean.IsVisible, "clean close needs no prompt");
var w = Open(); var s = w.ArrangeArea.Model!.Session;
var hit = s.State.Hits.First();
s.SetVolume([hit.Anchor!.Value], 37); var edited = s.State;
w.Close(); Pump(); var prompt = Prompt(w);
Check(new[] { "Save", "Discard", "Cancel" }.All(n => Buttons(prompt).Any(b => Equals(b.Content, n))), "close presents three explicit choices");
w.Close(); Pump(); Check(w.OwnedWindows.Count == 1, "repeated close keeps one prompt");
Choose(prompt, "Cancel"); Check(w.IsVisible && ReferenceEquals(s.State, edited), "Cancel preserves dirty edits");
w.Close(); Pump(); Prompt(w).Close(); Pump(); Check(w.IsVisible && s.IsEdited, "dialog X cancels close");
w.Close(); Pump(); Press(Prompt(w), Key.Escape); Check(w.IsVisible && !w.OwnedWindows.Any(), "Escape cancels close");
w.Close(); Pump(); Choose(Prompt(w), "Save"); Wait(() => !w.IsVisible || w.OwnedWindows.Any() || !((bool)typeof(MainWindow).GetField("savingHs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!));
if (w.OwnedWindows.Any()) Choose(w.OwnedWindows.Last(), "Save");
Wait(() => !w.IsVisible); Check(!w.IsVisible, "successful Save closes editor");
Check(Volume(MapPath(w)) == 37, "close Save writes changed volume");

w = Open(); s = w.ArrangeArea.Model!.Session; hit = s.State.Hits.First();
string path = MapPath(w); var bytes = File.ReadAllBytes(path);
var loose = s.HitAt(hit.Lane, 2700); s.Add([loose]); Call(w, "SaveProject");
Check(ProjectStore.Load(ProjectStore.DefaultRoot(), path)!.Unanchored.Count == 1, "autosave records new loose hit");
w.Close(); Pump(); Choose(Prompt(w), "Discard"); Check(!w.IsVisible, "Discard closes");
Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Discard leaves map bytes unchanged");
Check(ProjectStore.Load(ProjectStore.DefaultRoot(), path)!.Unanchored.Count == 0, "Discard removes autosaved unsaved loose hit");
w = Open(path); Check(!w.ArrangeArea.Model!.Session.Unanchored.Any(), "discarded loose hit stays absent after reopen"); w.Close(); Pump();

w = Open(); s = w.ArrangeArea.Model!.Session; hit = s.State.Hits.First(); path = MapPath(w);
s.Add([s.HitAt(hit.Lane, 2750)]); Call(w, "SaveProject");
// Simulate a previously persisted project by reopening without closing this editor.
var loaded = Open(path); var baseline = loaded.ArrangeArea.Model!.Session;
Check(baseline.Unanchored.Count() == 1, "existing project loose hit restored");
baseline.Add([baseline.HitAt(hit.Lane, 2900)]); Call(loaded, "SaveProject");
loaded.Close(); Pump(); Choose(Prompt(loaded), "Discard");
Check(ProjectStore.Load(ProjectStore.DefaultRoot(), path)!.Unanchored.Count == 1, "Discard retains previously loaded loose hit");
s.Undo(); w.Close(); Pump();

w = Open(); s = w.ArrangeArea.Model!.Session; hit = s.State.Hits.First(); path = MapPath(w);
s.SetVolume([hit.Anchor!.Value], 42); File.AppendAllText(path, "\n// external change\n"); bytes = File.ReadAllBytes(path);
w.Close(); Pump(); Choose(Prompt(w), "Save"); Wait(() => w.FindControl<TextBlock>("WorkflowMessage")!.Text?.Contains("Save stopped") == true);
Check(w.IsVisible && s.IsEdited, "failed Save keeps dirty editor open");
Check(File.ReadAllBytes(path).SequenceEqual(bytes), "failed Save preserves external file");
File.Delete(path); w.Close(); Pump(); Choose(Prompt(w), "Discard"); Check(!w.IsVisible, "Discard works even after external map removal");

w = Open(); s = w.ArrangeArea.Model!.Session; hit = s.State.Hits.First();
var box = w.FindControl<TextBox>("LaneFilterBox")!; box.Focus(); Press(w, Key.F1);
Check(w.OwnedWindows.OfType<HelpWindow>().Any(), "F1 opens Help from text field"); w.OwnedWindows.OfType<HelpWindow>().Single().Close(); Pump();
s.SetVolume([hit.Anchor!.Value], 61); box.Focus(); Press(w, Key.S, RawInputModifiers.Control);
Wait(() => !w.ArrangeArea.Model!.Session.IsEdited || w.OwnedWindows.Any());
if (w.OwnedWindows.Any()) Choose(w.OwnedWindows.Last(), "Save");
Wait(() => !w.ArrangeArea.Model!.Session.IsEdited);
Check(Volume(MapPath(w)) == 61, "Ctrl+S from text field saves map");
s = w.ArrangeArea.Model!.Session; var before = s.State; box.Focus(); Press(w, Key.A, RawInputModifiers.Control); Press(w, Key.Left);
Check(ReferenceEquals(before, s.State), "text editing shortcuts do not mutate timeline");
hit = s.State.Hits.First(); var a = s.HitAt(hit.Lane, 2700); var b = s.HitAt(hit.Lane, 3100); s.Add([a, b]);
w.ArrangeArea.Edits.Select([a]); Call(w, "UpdateSavedTab");
Check(w.FindControl<TextBlock>("SavedSummary")!.Text!.Contains("no gameplay object"), "selected loose hit has specific save explanation");
Check(!w.FindControl<Expander>("SavedFields")!.IsVisible, "loose hit omits raw fields");
w.ArrangeArea.Edits.Select([hit, a]); Call(w, "UpdateSavedTab");
Check(w.FindControl<TextBlock>("SavedSummary")!.Text!.Contains("no gameplay object"), "mixed selection explains omitted loose hit");
w.ArrangeArea.Edits.Select([hit]); Call(w, "UpdateSavedTab");
Wait(() => { Call(w, "UpdateSavedTab"); return w.FindControl<Expander>("SavedFields")!.IsVisible; });
Check(w.FindControl<Expander>("SavedFields")!.IsVisible, "anchored hit retains Advanced fields");
box.Text = "unmatched lane"; w.ArrangeArea.Playhead = 0; Pump();
var hits = s.State.Hits.ToArray(); var volumes = s.State.Volumes.ToArray();
Call(w, "ShowNextOutputProblem"); Pump();
Check(box.Text == "" && w.ArrangeArea.Selection.Contains(a), "Next problem clears filter and selects first loose hit");
Check(Math.Abs(w.ArrangeArea.Playhead - 2700) < .01, "Next problem seeks first issue");
Call(w, "ShowNextOutputProblem"); Pump(); Check(w.ArrangeArea.Selection.Contains(b), "Next problem advances");
Call(w, "ShowNextOutputProblem"); Pump(); Check(w.ArrangeArea.Selection.Contains(a), "Next problem wraps");
Check(s.State.Hits.SequenceEqual(hits) && s.State.Volumes.SequenceEqual(volumes), "problem navigation changes no hit or volume data");
foreach (var size in new[] { (900, 500), (1366, 768), (1600, 900) })
{
    w.Width = size.Item1; w.Height = size.Item2; Pump();
    var next = w.FindControl<Button>("NextProblemButton")!;
    Check(next.IsEffectivelyVisible && next.Bounds.Width > 40, $"Next problem visible at {size}");
    var footer = w.FindControl<Control>("WorkflowFooter")!;
    Check(footer.TranslatePoint(default, w)!.Value.Y + footer.Bounds.Height <= w.Bounds.Height + 1, $"footer fits at {size}");
    Check(w.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "VOLUME AT TIMES · %" && t.IsEffectivelyVisible), $"shared volume scope visible at {size}");
    w.CaptureRenderedFrame()!.Save(Path.Combine(artifacts, $"safety-{size.Item1}x{size.Item2}.png"));
}
w.Close(); Pump(); Choose(Prompt(w), "Discard");

// Keep sample replacement gestures and audition separate, with an explicit destination.
var browser = new FileBrowserPanel();
var browserWindow = new Window { Content = browser, Width = 500, Height = 500 }; browserWindow.Show();
string samples = Directory.CreateDirectory(Path.Combine(artifacts, "samples")).FullName;
File.WriteAllBytes(Path.Combine(samples, "one.wav"), BlankSample.Bytes);
Directory.CreateDirectory(Path.Combine(samples, "folder"));
int assignments = 0, previews = 0;
browser.SampleRequested += _ => assignments++; browser.PreviewRequested += _ => previews++;
browser.SetRoots(samples, samples);
var entries = (ListBox)typeof(FileBrowserPanel).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(browser)!;
Wait(() => entries.ItemCount == 2); entries.SelectedIndex = 1; Pump();
var replace = browser.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Replace lane sample"));
Check(!replace.IsEnabled, "browser cannot assign without active lane");
browser.SetAssignmentTarget("Kick lane", false); Check(!replace.IsEnabled, "read-only lane cannot be replaced");
browser.SetAssignmentTarget("Kick lane", true);
Check(replace.IsEnabled && browser.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Kick lane for every hit") == true), "browser names lane and whole-lane replacement scope");
Check(browser.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Double-click a sample or press Enter") == true), "browser explains assignment gesture");
Check(entries.ContainerFromIndex(1)!.Focus(), "sample row accepts keyboard focus"); Press(browserWindow, Key.Enter); Check(assignments == 1, "Enter still assigns selected sample");
Choose(browserWindow, "Preview"); Check(previews == 1 && assignments == 1, "Preview auditions without assignment");
entries.SelectedIndex = 0; Pump(); Check(browser.GetLogicalDescendants().OfType<Button>().Any(b => Equals(b.Content, "Open folder") && b.IsEnabled), "folder browsing stays available");
browserWindow.Close(); Pump();

// Cancelling a second save confirmation must not authorize closing.
w = Open(); path = MapPath(w); s = w.ArrangeArea.Model!.Session;
string root = Path.GetDirectoryName(path)!;
File.WriteAllBytes(Path.Combine(root, "layer-a.wav"), BlankSample.Bytes);
File.WriteAllBytes(Path.Combine(root, "layer-b.wav"), BlankSample.Bytes);
// Reopen after adding the samples so the map folder resolver sees them.
w.Close(); Pump(); w = Open(path); s = w.ArrangeArea.Model!.Session;
s.Add([s.HitAt(new Lane(LaneSource.BeatmapFile, "layer-a.wav"), 1000), s.HitAt(new Lane(LaneSource.BeatmapFile, "layer-b.wav"), 1000)]);
bytes = File.ReadAllBytes(path); w.Close(); Pump(); Choose(Prompt(w), "Save");
Wait(() => w.OwnedWindows.Any()); var confirmation = w.OwnedWindows.Last();
Check(confirmation.Title != "Unsaved changes", "close Save waits for export confirmation");
confirmation.Close(); Pump();
Wait(() => !(bool)typeof(MainWindow).GetField("savingHs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!);
Check(w.IsVisible && s.IsEdited && File.ReadAllBytes(path).SequenceEqual(bytes), "cancelled Save keeps editor and file unchanged");
w.Close(); Pump(); Choose(Prompt(w), "Discard");
Console.WriteLine($"PASS: {checks} safety checks");

MainWindow Open(string? path = null)
{
    path ??= Path.Combine(artifacts, $"fixture-{++serial}-{Guid.NewGuid():N}.osu");
    if (!File.Exists(path)) File.WriteAllText(path, """
osu file format v14
[General]
Mode:0
[Metadata]
Title:Safety validation
Artist:Local fixture
Creator:Check
Version:Normal
[Difficulty]
SliderMultiplier:1.4
SliderTickRate:1
[TimingPoints]
0,500,4,2,0,100,1,0
[HitObjects]
256,192,1000,1,0,0:0:0:100:
256,192,1500,1,2,0:0:0:100:
256,192,2000,1,8,0:0:0:100:
""");
    var result = new MainWindow(path, startAudio: false, showInspector: false, usePreferences: false) { Width = 1600, Height = 900 }; result.Show(); Wait(() => result.Loading.IsCompleted); Pump(); return result;
}
string MapPath(MainWindow target) => (string)typeof(MainWindow).GetProperty("mapPath", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
object? Call(MainWindow target, string method) => typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
Window Prompt(MainWindow target) => target.OwnedWindows.Single(x => x.Title == "Unsaved changes");
IEnumerable<Button> Buttons(Window target) => target.GetLogicalDescendants().OfType<Button>();
void Choose(Window target, string label) { var button = Buttons(target).Single(b => Equals(b.Content, label)); button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump(); }
void Press(Window target, Key key, RawInputModifiers mods = RawInputModifiers.None) { target.KeyPress(key, mods); if (target.IsVisible) target.KeyRelease(key, mods); Pump(); }
void Pump() { for (int i = 0; i < 8; i++) Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
void Wait(Func<bool> done) { var until = DateTime.UtcNow.AddSeconds(20); while (!done() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); } Pump(); if (!done()) throw new Exception("Timed out waiting for UI"); }
void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }

int Volume(string path) { var document = MapDocument.Load(path); return document.Session.VolumeOf(document.Session.State.Hits.First()); }

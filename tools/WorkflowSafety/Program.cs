using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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
w.Close(); Pump(); Choose(Prompt(w), "Save"); Wait(() => !w.IsVisible || w.OwnedWindows.Any() || !((bool)typeof(MainWindow).GetProperty("WriteBusy", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!));
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
w.Close(); Pump(); Choose(Prompt(w), "Save"); Wait(() => ToolTip.GetTip(w.FindControl<Button>("WorkflowStatusButton")!)?.ToString()?.Contains("Save stopped") == true);
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
var laneModel = w.ArrangeArea.Model!;
var laneRows = laneModel.Rows.OfType<LaneRow>().Take(3).ToArray();
var hitSelection = w.ArrangeArea.Selection.ToArray();
w.ArrangeArea.ScrollY = 0; Pump();
ClickLane(laneRows[0]); ClickLane(laneRows[2], RawInputModifiers.Shift);
Check(laneModel.SelectedLanes.SetEquals(laneRows.Select(r => r.Lane)), "Shift-click selects a visible lane range");
Check(w.ArrangeArea.Selection.ToHashSet().SetEquals(hitSelection), "lane clicks preserve selected hits");
ClickLane(laneRows[1], RawInputModifiers.Control);
Check(laneModel.SelectedLanes.Count == 2 && !laneModel.SelectedLanes.Contains(laneRows[1].Lane), "Ctrl-click toggles one lane");
ClickLane(laneRows[0], x: ArrangeView.GridLeft - 48 + 9);
Check(laneModel.SelectedLanes.All(laneModel.IsMuted) && !laneModel.IsMuted(laneRows[1].Lane), "lane M button mutes only selected group");
ClickLane(laneRows[0], x: ArrangeView.GridLeft - 25 + 9);
Check(laneModel.SelectedLanes.All(laneModel.IsSoloed) && !laneModel.IsSoloed(laneRows[1].Lane), "lane S button solos only selected group");
ClickLane(laneRows[1]);
Check(laneModel.SelectedLanes.Count == 1 && laneModel.SelectedLanes.Contains(laneRows[1].Lane), "plain lane click ends previous group");
Press(w, Key.Escape);
Check(laneModel.SelectedLanes.Count == 0, "Escape clears lane selection");
Check(s.State.Hits.SequenceEqual(hits) && s.State.Volumes.SequenceEqual(volumes), "group mixer controls preserve hit and volume data");
ClickLane(laneRows[0]); ClickLane(laneRows[2], RawInputModifiers.Shift);
foreach (var size in new[] { (900, 500), (1366, 768), (1600, 900) })
{
    w.Width = size.Item1; w.Height = size.Item2; Pump();
    var status = w.FindControl<Button>("WorkflowStatusButton")!;
    Check(status.IsEffectivelyVisible, $"operation feedback available at {size}");
    status.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    var details = w.OwnedWindows.Single(d => d.Title == "Operation details");
    Check(Buttons(details).Any(b => Equals(b.Content, "Next problem")), $"Next problem available in details at {size}");
    details.Close(); Pump();
    Check(w.FindControl<Control>("WorkflowFooter") is null, $"persistent banner removed at {size}");
    Check(w.FindControl<InspectorPanel>("Inspector")!.GetLogicalDescendants().Contains(w.FindControl<Control>("VolumeScopeBox")), $"shared volume scope in the inspector's Selection card at {size}");
    // The L/R locator button needs 1000 px; narrower windows set the locators on the ruler.
    foreach (string id in new[] { "SnapModeBox", "TripletSnapToggle", "InspectorToggle", "LaneDetailsToggle", "UsedLanesToggle", "ObjectLinesToggle", "VolumeToggle", "SongControls" }.Concat(size.Item1 >= MainWindow.LocatorsMinWidth ? ["LocatorsButton"] : []))
        Check(w.FindControl<Control>(id)!.IsEffectivelyVisible, $"{id} directly available at {size}");
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
Wait(() => entries.Items.Cast<object>().Any(e => (string)e.GetType().GetProperty("Path")!.GetValue(e)! == Path.Combine(samples, "one.wav")));
var sampleRow = entries.Items.Cast<object>().Single(e => (string)e.GetType().GetProperty("Path")!.GetValue(e)! == Path.Combine(samples, "one.wav"));
entries.SelectedItem = sampleRow; Pump();
var replace = browser.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add to selected lane"));
Check(previews == 1 && assignments == 0, "highlight previews without assigning");
Check(!replace.IsEnabled, "browser cannot assign without active lane");
browser.SetAssignmentTarget("Kick lane", false); Check(!replace.IsEnabled, "read-only lane cannot be replaced");
browser.SetAssignmentTarget("Kick lane", true);
Check(replace.IsEnabled && browser.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("every hit in Kick lane") == true), "browser names lane and replacement scope");
entries.ScrollIntoView(sampleRow); Pump();
Check(entries.ContainerFromIndex(entries.SelectedIndex)!.Focus(), "sample row accepts keyboard focus"); Press(browserWindow, Key.Enter);
Check(assignments == 0 && previews >= 2, "Enter previews without assignment");
Choose(browserWindow, "Add to selected lane"); Check(assignments == 1, "explicit button assigns sample");
Check(entries.ItemCount > 2, "browser includes expanded parent folders");
Check(entries.Bounds.Height > 250, "browser fills vertical space");
browserWindow.Close(); Pump();

// The new workflow controls must change their intended scope only.
w = Open(); s = w.ArrangeArea.Model!.Session;
var area = w.ArrangeArea; var scopedModel = area.Model!;
Check(w.FindControl<ComboBox>("VolumeScopeBox")!.SelectedIndex == 0, "volume scope defaults to All lanes");
Check(w.FindControl<Button>("GenerateButton") is null && w.FindControl<MenuItem>("GenerateItem") is not null, "Generate lives in menus");
var inspector = w.GetLogicalDescendants().OfType<InspectorPanel>().Single();
Check(!inspector.GetLogicalDescendants().Contains(inspector.Browser), "tree browser stays outside the inspector");
Check(inspector.Order.FirstOrDefault() == InspectorPanel.SelectionCard, "the Selection card (D2) starts at the top of the inspector");
Check(!w.FindControl<MenuItem>("HistoryPanelItem")!.IsChecked && !w.FindControl<MenuItem>("SoundSetsPanelItem")!.IsChecked, "optional panels default hidden");
var browserHost = w.FindControl<Border>("BrowserHost")!;
Check(Grid.GetColumn(browserHost) == 4 && browserHost.Bounds.Height > 400, "browser fills right sidebar");
var stateBeforePan = s.State; int historyBeforePan = s.StepCount;
w.Height = 720; Pump();
area.Axis.Zoom = 1; area.Axis.Left = 1000; area.ScrollY = 100; Pump();
Press(w, Key.D2); Check(area.Tool == Tool.Grab, "2 activates Grab");
var panPoint = area.TranslatePoint(new Point(ArrangeView.GridLeft + 170, area.HeaderHeight + 75), w)!.Value;
double initialLeft = area.Axis.Left, initialScroll = area.ScrollY;
w.MouseDown(panPoint, MouseButton.Left); w.MouseMove(panPoint + new Vector(60, 25)); w.MouseUp(panPoint + new Vector(60, 25), MouseButton.Left); Pump();
Check(area.Axis.Left < initialLeft && area.ScrollY < initialScroll, "Grab pans time and lanes");
Check(ReferenceEquals(stateBeforePan, s.State) && s.StepCount == historyBeforePan, "Grab creates no edits or undo step");
w.Height = 900; Pump();
Press(w, Key.D6); Check(area.Tool == Tool.Ramp && w.FindControl<Control>("BottomPanel")!.IsEffectivelyVisible, "6 opens controller and activates Ramp");
Press(w, Key.D1);
var at1500 = s.State.Hits.Where(h => h.Time == 1500).ToList();
var targetHit = at1500[0]; var untouchedHit = at1500[1];
scopedModel.SelectLane(targetHit.Lane); area.Edits.Select(at1500);
w.FindControl<ComboBox>("VolumeScopeBox")!.SelectedIndex = 1;
w.FindControl<ComboBox>("VolumeModeBox")!.SelectedIndex = 1; // Set all; the editor defaults to Relative.
w.FindControl<TextBox>("VolumeValueBox")!.Text = "25"; Call(w, "ApplyVolumeValue"); Pump();
Check(s.VolumeOf(targetHit) == 25 && s.VolumeOf(untouchedHit) == 100, "numeric Selected lanes leaves simultaneous other layer unchanged");
var volumeControl = w.FindControl<VolumeLane>("Volume")!;
volumeControl.ApplyVolumes(new Dictionary<MomentKey, int> { [targetHit.Anchor!.Value] = 43 });
Check(s.VolumeOf(targetHit) == 43 && s.VolumeOf(untouchedHit) == 100, "bar/paint/ramp shared path obeys Selected lanes");
w.FindControl<ComboBox>("VolumeModeBox")!.SelectedIndex = 0;
w.FindControl<TextBox>("VolumeValueBox")!.Text = "7"; Call(w, "ApplyVolumeValue");
Check(s.VolumeOf(targetHit) == 50 && s.VolumeOf(untouchedHit) == 100, "relative edit shifts each selected lane level independently");
w.FindControl<ComboBox>("VolumeScopeBox")!.SelectedIndex = 0;
w.FindControl<TextBox>("VolumeValueBox")!.Text = "-10"; Call(w, "ApplyVolumeValue");
Check(s.VolumeOf(targetHit) == 40 && s.VolumeOf(untouchedHit) == 90, "relative All lanes preserves independent level differences");
volumeControl.ApplyVolumes(new Dictionary<MomentKey, int> { [targetHit.Anchor!.Value] = 67 });
Check(at1500.All(h => s.VolumeOf(h) == 67), "All lanes updates every layer and clears individual levels");
s.AddBookmark(1500); s.RenameBookmark(1500, "Chorus"); Pump();
Call(w, "SaveProject");
Check(ProjectStore.Load(ProjectStore.DefaultRoot(), MapPath(w))!.BookmarkLabels[1500] == "Chorus", "bookmark label saved in project");
Check(!File.ReadAllText(MapPath(w)).Contains("Chorus"), "bookmark labels stay out of osu file");
foreach (var size in new[] { (1920, 1030), (1040, 720) })
{
    w.Width = size.Item1; w.Height = size.Item2; Pump();
    var scopeControl = w.FindControl<ComboBox>("VolumeScopeBox")!;
    Check(scopeControl.IsEffectivelyVisible && scopeControl.TranslatePoint(default, w)!.Value.X + scopeControl.Bounds.Width < w.Bounds.Width, $"volume scope remains accessible at {size}");
    w.CaptureRenderedFrame()!.Save(Path.Combine(artifacts, $"workflow-{size.Item1}x{size.Item2}.png"));
}
w.Close(); Pump(); Choose(Prompt(w), "Discard");
Check(!ProjectStore.Load(ProjectStore.DefaultRoot(), MapPath(w))!.BookmarkLabels.ContainsKey(1500), "Discard does not resurrect bookmark label autosave");
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
Wait(() => !(bool)typeof(MainWindow).GetProperty("WriteBusy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!);
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

void ClickLane(LaneRow row, RawInputModifiers modifiers = RawInputModifiers.None, double x = 140)
{
    int index = laneModel.Rows.ToList().IndexOf(row);
    double top = (double)typeof(ArrangeView).GetMethod("RowTop", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w.ArrangeArea, [index])!;
    var point = w.ArrangeArea.TranslatePoint(new Point(x, w.ArrangeArea.HeaderHeight + top - w.ArrangeArea.ScrollY + w.ArrangeArea.RowHeight / 2), w)!.Value;
    w.MouseDown(point, MouseButton.Left, modifiers); w.MouseUp(point, MouseButton.Left, modifiers); Pump();
}

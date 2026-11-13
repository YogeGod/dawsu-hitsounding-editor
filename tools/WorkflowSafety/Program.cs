using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
Check(Grid.GetColumn(browserHost) == 0 && browserHost.Bounds.Height > 400 && browserHost.TranslatePoint(default, w)!.Value.X < inspector.TranslatePoint(default, w)!.Value.X, "browser fills the left sidebar, left of the inspector (UI refresh, 2026-11-13)");
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
// The bars follow the selected hits' lanes: with only the target selected, a bar edit leaves the other layer alone.
area.Edits.Select([targetHit]); Call(w, "UpdateStatus"); Pump();
Check(volumeControl.ShownLanes is { Count: 1 } one && one.Contains(targetHit.Lane), "the volume bars show the selected hit's lane");
Check(w.FindControl<TextBlock>("VolumeScopeHint")!.Text == scopedModel.NameOf(targetHit.Lane), "the label beside the bars names that lane");
volumeControl.ApplyVolumes(new Dictionary<MomentKey, int> { [targetHit.Anchor!.Value] = 43 });
Check(s.VolumeOf(targetHit) == 43 && s.VolumeOf(untouchedHit) == 100, "bar/paint/ramp shared path changes only the shown lane");
area.Edits.Select([untouchedHit]); Call(w, "UpdateStatus"); Pump();
Check(volumeControl.ShownLanes is { Count: 1 } other && other.Contains(untouchedHit.Lane), "selecting a hit in another lane switches the bars to that lane");
area.Edits.Select(at1500); Call(w, "UpdateStatus"); Pump();
Check(volumeControl.ShownLanes is { Count: 2 } both && at1500.All(h => both.Contains(h.Lane)), "selecting hits in two lanes shows both lanes");
Check(w.FindControl<TextBlock>("VolumeScopeHint")!.Text == "2 lanes", "the label says how many lanes");
area.Edits.Select([]); scopedModel.SetActive(untouchedHit.Lane); Call(w, "UpdateStatus"); Pump();
Check(volumeControl.ShownLanes is { Count: 1 } active && active.Contains(untouchedHit.Lane), "with nothing selected the bars show the active lane");
area.Edits.Select(at1500); Call(w, "UpdateStatus"); Pump();
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
    // The scope drop-down is shown as two buttons (SegmentedChoice): the row of buttons right after the hidden box.
    var scopeBox = w.FindControl<ComboBox>("VolumeScopeBox")!;
    var scopeControl = ((Panel)scopeBox.Parent!).Children[((Panel)scopeBox.Parent!).Children.IndexOf(scopeBox) + 1];
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
// Select tool: a double-click on empty grid space inserts one hit, like Draw; a single click and an occupied point do not.
w = Open(); s = w.ArrangeArea.Model!.Session; area = w.ArrangeArea;
Press(w, Key.D1); Check(area.Tool == Tool.Select, "1 activates Select");
area.Axis.Zoom = 0.5; area.Axis.Left = 1000; area.ScrollY = 0; Pump(); // 1000 to 2000 ms fits in 500 px
var clapRow = area.Model!.Rows.OfType<LaneRow>().Single(r => r.Lane.Name.EndsWith("hitclap") && s.State.Hits.Any(h => h.Lane == r.Lane && h.Time == 2000));
int hitCount = s.State.Hits.Count, stepsBefore = s.StepCount;
var single = GridPoint(clapRow, 1250);
w.MouseDown(single, MouseButton.Left); w.MouseUp(single, MouseButton.Left); Pump();
Check(s.State.Hits.Count == hitCount && s.StepCount == stepsBefore, "Select single click on empty space inserts nothing");
DoubleClick(GridPoint(clapRow, 1750));
var inserted = s.State.Hits.Where(h => h.Lane == clapRow.Lane && h.Time == 1750).ToList();
Check(inserted.Count == 1 && s.State.Hits.Count == hitCount + 1, "Select double-click on empty space inserts exactly one hit");
Check(s.StepCount == stepsBefore + 1, "double-click insertion is one undo step");
DoubleClick(GridPoint(clapRow, 2055)); // off the diamond (15 px = 30 ms here), but it snaps to 2000, where the clap is
Check(s.State.Hits.Count == hitCount + 1 && s.StepCount == stepsBefore + 1, "double-click on an occupied snap point adds nothing");
Check(area.Edits.Selected.Count == 0, "that double-click missed the hit itself, so it tested the empty-space path");
var onClap = GridPoint(clapRow, 2000);
w.MouseDown(onClap, MouseButton.Right); w.MouseUp(onClap, MouseButton.Right); Pump();
Check(s.State.Hits.Any(h => h.Lane == clapRow.Lane && h.Time == 2000) && s.StepCount == stepsBefore + 1, "right-click on a hit does not delete it");
Check(w.ToolPicker is { IsOpen: true }, "right-click in the grid opens the tool picker");
var pickerButtons = ((StackPanel)w.ToolPicker!.Content!).Children.OfType<ToggleButton>().ToList();
Check(pickerButtons.Count == 7 && pickerButtons[0].IsChecked == true, "the picker shows every tool, the current one checked");
var drawButton = pickerButtons.Single(b => AutomationProperties.GetName(b) == "Draw");
drawButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(area.Tool == Tool.Draw && !w.ToolPicker.IsOpen && s.StepCount == stepsBefore + 1, "picking Draw switches tool, closes the picker and edits nothing");
Press(w, Key.D1);
// Arrow keys as in Cubase's Drum Editor: Up/Down move the selected hit a lane, Left/Right select across lanes.
var movedHit = s.State.Hits.Single(h => h.Lane == clapRow.Lane && h.Time == 1750);
area.Edits.Select([movedHit]); Pump();
var laneAbove = HitEditor.ShiftLane(area.Model!, clapRow.Lane, -1);
var beforeLaneMoves = s.State;
Press(w, Key.Up);
Check(s.State.Hits.Any(h => h.Lane == laneAbove && h.Time == 1750) && !s.State.Hits.Any(h => h.Lane == clapRow.Lane && h.Time == 1750)
    && s.StepCount == stepsBefore + 2, "Up moves the selected hit to the lane above, keeping its time");
Press(w, Key.Down);
Check(s.State.Hits.Any(h => h.Lane == clapRow.Lane && h.Time == 1750), "Down moves it back");
s.Undo(); Pump(); s.Undo(); Pump();
Check(ReferenceEquals(s.State, beforeLaneMoves), "each lane move is one undo step");
area.Edits.Select([s.State.Hits.Single(h => h.Lane == clapRow.Lane && h.Time == 1750)]); Pump();
Press(w, Key.Right);
var afterRight = Assert1(area.Edits.Selected);
Check(afterRight.Time == 2000 && afterRight.Lane == area.Model!.Rows.OfType<LaneRow>().First(r => s.State.Hits.Any(h => h.Lane == r.Lane && h.Time == 2000)).Lane,
    "Right selects the next hit in any lane (top lane first at the same moment)");
s.Undo(); Pump();
Check(s.State.Hits.Count == hitCount && !s.State.Hits.Any(h => h.Time == 1750), "one Undo removes the double-click insertion");
w.Close(); Pump(); Check(!w.IsVisible, "editor closes cleanly once the insertion is undone");
// Keysound mode: the Hitsound grid's mouse actions and keys on notes (Keysound parity, 2026-11-13).
var hsForKeys = Open(); string keysMap = MapPath(hsForKeys); hsForKeys.Close(); Pump();
var keysProject = OsuHitsound.Lanes.Keysound.KeysoundStore.Create(keysMap, Path.Combine(Directory.CreateTempSubdirectory("dawsu-keys-check-").FullName, "Project"), []); // outside the map's folder
w = new MainWindow(keysProject, startAudio: false, showInspector: false, usePreferences: false) { Width = 1600, Height = 900 };
w.Show(); Wait(() => w.Loading.IsCompleted); Pump();
var keys = w.Keys; var roll = keys.Roll; var ks = keys.Document!.Session; var track = ks.State.Tracks[0].Id;
var keyTempo = keys.Document.Tempo;
Check(keys.IsVisible && w.FindControl<Menu>("KeysoundMenu")!.IsVisible && !w.FindControl<Menu>("HeaderMenu")!.IsVisible, "a Keysound project opens in Keysound mode with its own menus");
Check(keys.FindControl<StackPanel>("RibbonGroups") is { } ribbon && ribbon.Children.OfType<StackPanel>().Count(g => g.Classes.Contains("ribbonGroup")) >= 5
    && keys.FindControl<Button>("SaveButton") is null && w.FindControl<TextBlock>("SaveLabel")!.Text == "Save project" && w.FindControl<Button>("SaveButton")!.IsVisible,
    "the key editor has the Hitsound ribbon's groups, and the title bar's Save button saves the project (UI refresh, 2026-11-13)");
ThemeColors.Apply(UiTheme.Current, new UserColors { Scheme = UiTheme.SlateScheme }, darkModeFallback: true); Pump();
w.CaptureRenderedFrame()!.Save(Path.Combine(artifacts, "keysound-slate-1600x900.png"));
ThemeColors.Apply(UiTheme.Current, new UserColors { Scheme = 1 }, darkModeFallback: true); Pump();
roll.Axis.Zoom = 0.5; roll.Axis.Left = 1000; roll.TopPitch = 66.5; Pump(); // 1000 to 2000 ms in 500 px; pitch 60 in view
IReadOnlyList<OsuHitsound.Lanes.Keysound.KeyNote> Notes() => ks.State.Track(track)!.Notes;
long Tick(double ms) => keyTempo.RoundTick(ms);

int stepsKs = UndoDepth();
var emptyPoint = RollPoint(1250, 60);
w.MouseDown(emptyPoint, MouseButton.Left); w.MouseUp(emptyPoint, MouseButton.Left); Pump();
Check(Notes().Count == 0, "Select single click on empty roll space adds nothing");
DoubleClick(RollPoint(1760, 60));
Check(Notes().Count == 1 && Notes()[0].Start == Tick(1750) && Notes()[0].Pitch == 60 && UndoDepth() == stepsKs + 1,
    "Select double-click on empty roll space inserts one snapped note in one undo step");
var drawn = Notes()[0];

var onNote = RollPoint(1760, 60);
w.MouseDown(onNote, MouseButton.Right); w.MouseUp(onNote, MouseButton.Right); Pump();
Check(Notes().Count == 1 && keys.ToolPickerFlyout is { IsOpen: true }, "right-click on a note keeps it and opens the tool picker");
var keyPickerButtons = ((StackPanel)keys.ToolPickerFlyout!.Content!).Children.OfType<ToggleButton>().ToList();
Check(keyPickerButtons.Count == 9 && keyPickerButtons[0].IsChecked == true, "the key editor's picker shows its tools, the current one checked");
keyPickerButtons.Single(b => AutomationProperties.GetName(b) == "Mute").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(roll.Tool == KeyTool.Mute && !keys.ToolPickerFlyout.IsOpen, "picking Mute switches tool and closes the picker");
var stateBeforeMute = ks.State;
w.MouseDown(onNote, MouseButton.Left); w.MouseUp(onNote, MouseButton.Left); Pump();
Check(roll.PreviewMuted.Contains(drawn.Id) && ReferenceEquals(ks.State, stateBeforeMute), "the Mute tool silences a note for listening only, with no edit");
w.MouseDown(onNote, MouseButton.Left); w.MouseUp(onNote, MouseButton.Left); Pump();
Check(!roll.PreviewMuted.Contains(drawn.Id), "a second Mute click restores it");
Press(w, Key.D1);

var altPoint = RollPoint(1260, 62);
w.MouseDown(altPoint, MouseButton.Left, RawInputModifiers.Alt); w.MouseUp(altPoint, MouseButton.Left, RawInputModifiers.Alt); Pump();
Check(roll.Tool == KeyTool.Select && Notes().Count == 2 && Notes().Any(n => n.Pitch == 62 && n.Start == Tick(1250)), "Alt+click draws a note with the Select tool, as in the Hitsound grid");
var copyFrom = RollPoint(1810, 60); var copyTo = RollPoint(1940, 60); // the note's middle: its edges resize
w.MouseDown(copyFrom, MouseButton.Left, RawInputModifiers.Control | RawInputModifiers.Alt);
w.MouseMove(copyTo, RawInputModifiers.Control | RawInputModifiers.Alt);
w.MouseUp(copyTo, MouseButton.Left, RawInputModifiers.Control | RawInputModifiers.Alt); Pump();
Check(Notes().Count == 3 && Notes().Any(n => n.Id == drawn.Id && n.Start == drawn.Start) && Notes().Any(n => n.Pitch == 60 && n.Start == Tick(1875)),
    "Ctrl+Alt+drag copies a note and leaves the original in place");

// Keys the Hitsound editor has: Alt+Space to the selection, Ctrl+B bookmark, ] next bookmark, Ctrl+Alt+Right to the next grid line.
roll.Notes.SelectWhere(n => n.Id == drawn.Id); Pump();
Press(w, Key.Space, RawInputModifiers.Alt);
Check(Math.Abs(roll.Playhead - keyTempo.TimeAt(drawn.Start)) < 1, "Alt+Space moves the playhead to the selected note");
Press(w, Key.B, RawInputModifiers.Control);
Check(ks.State.Bookmarks.Any(b => Math.Abs(b.Time - roll.Playhead) < 1), "Ctrl+B adds a bookmark at the playhead");
Press(w, Key.Home); Press(w, Key.OemCloseBrackets);
Check(Math.Abs(roll.Playhead - ks.State.Bookmarks[0].Time) < 1, "] jumps to the next bookmark");
Press(w, Key.Right, RawInputModifiers.Control | RawInputModifiers.Alt);
Check(Notes().Single(n => n.Id == drawn.Id).Start == Tick(1875), "Ctrl+Alt+Right moves the selection to the next grid line");

// Snap mode Objects: a double-click lands on the nearest object (the circle at 1500 ms).
keys.FindControl<ComboBox>("SnapModeBox")!.SelectedIndex = 2; Pump();
DoubleClick(RollPoint(1560, 64));
Check(Notes().Any(n => n.Pitch == 64 && n.Start == Tick(1500)), "snap mode Objects puts an inserted note on the nearest gameplay object");

// The Range tool: drag a time range over keys, then Edit range copies, moves or deletes the notes in it; P puts the
// locators around it; Esc drops it. Scale by velocity scales the notes in a velocity range.
keys.FindControl<ComboBox>("SnapModeBox")!.SelectedIndex = 0; Pump();
keys.RunShortcut("toolRange"); Pump();
var rangeFrom = RollPoint(1490, 65); var rangeTo = RollPoint(1990, 59);
w.MouseDown(rangeFrom, MouseButton.Left); w.MouseMove(rangeTo); w.MouseUp(rangeTo, MouseButton.Left); Pump();
Check(roll.Notes.Range is { Start: 1500, End: 2000, Low: 59, High: 65 }, "dragging with the Range tool marks a snapped time range over the keys dragged across");
Check(roll.Notes.NotesInRange().Count >= 2, "the range holds the notes starting in it on its keys");
// What Edit range and Scale by velocity do to notes is tested in NoteEditorTests.
Press(w, Key.P);
Check(w.Keys.FindControl<TextBox>("LeftLocatorBox")!.Text == "1500" && w.Keys.FindControl<TextBox>("RightLocatorBox")!.Text == "2000", "P puts the locators around the range");
Press(w, Key.Escape);
Check(roll.Notes.Range is null, "Esc drops the range");

// Narrow windows close the inspector, as in Hitsound mode, and wide ones bring it back.
w.Width = 1000; Pump();
Check(!keys.FindControl<Control>("Side")!.IsVisible, "below 1100 px the key editor's inspector closes");
w.Width = 1600; Pump();
Check(keys.FindControl<Control>("Side")!.IsVisible, "a wide window shows it again");
w.Close(); Pump(); if (w.OwnedWindows.Any()) Choose(Prompt(w), "Discard"); Pump();
Check(!w.IsVisible, "the Keysound editor closes once its edits are discarded");

Console.WriteLine($"PASS: {checks} safety checks");

int UndoDepth()
{
    int depth = 0;
    var session = w.Keys.Document!.Session;
    var undo = (System.Collections.ICollection)typeof(OsuHitsound.Lanes.Keysound.KeysoundSession).GetField("undo", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
    depth = undo.Count;
    return depth;
}

Point RollPoint(double time, int pitch)
{
    var r = w.Keys.Roll;
    var local = new Point(PianoRollView.KeyboardWidth + r.Axis.X(time), PianoRollView.RulerHeight + PianoRollView.StripHeight + (r.TopPitch - pitch + 0.5) * r.RowHeight);
    if (local.X <= PianoRollView.KeyboardWidth || local.X >= r.Bounds.Width || local.Y <= PianoRollView.RulerHeight + PianoRollView.StripHeight || local.Y >= r.Bounds.Height - r.VelocityLaneHeight - r.BottomReserve)
        throw new Exception($"FAIL: {time} ms / pitch {pitch} is outside the visible roll ({local})");
    return r.TranslatePoint(local, w)!.Value;
}


Point GridPoint(LaneRow row, double time)
{
    int index = w.ArrangeArea.Model!.Rows.ToList().IndexOf(row);
    double top = (double)typeof(ArrangeView).GetMethod("RowTop", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w.ArrangeArea, [index])!;
    var local = new Point(ArrangeView.GridLeft + w.ArrangeArea.Axis.X(time), w.ArrangeArea.HeaderHeight + top - w.ArrangeArea.ScrollY + w.ArrangeArea.RowHeight / 2);
    if (local.X <= ArrangeView.GridLeft || local.X >= w.ArrangeArea.Bounds.Width || local.Y <= w.ArrangeArea.HeaderHeight || local.Y >= w.ArrangeArea.Bounds.Height)
        throw new Exception($"FAIL: {time} ms is outside the visible grid ({local})");
    return w.ArrangeArea.TranslatePoint(local, w)!.Value;
}

EditHit Assert1(IReadOnlyCollection<EditHit> hits) { if (hits.Count != 1) throw new Exception($"FAIL: expected one selected hit, got {hits.Count}"); return hits.First(); }

void DoubleClick(Point point)
{
    w.MouseDown(point, MouseButton.Left); w.MouseUp(point, MouseButton.Left);
    w.MouseDown(point, MouseButton.Left); w.MouseUp(point, MouseButton.Left); Pump();
}

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

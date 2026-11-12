using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using OsuHitsound.App;
using OsuHitsound.Lanes;

var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var map = Path.Combine(output, "ui-check.osu");
File.WriteAllText(map, """
osu file format v14
[General]
AudioFilename:song.wav
SampleSet:Soft
Mode:0
[Metadata]
Title:UI layout check
Artist:dawsu!
Creator:Validation
Version:Hitsounds
[Difficulty]
HPDrainRate:5
CircleSize:4
OverallDifficulty:5
ApproachRate:5
SliderMultiplier:1.4
SliderTickRate:1
[TimingPoints]
0,500,4,2,1,65,1,0
[HitObjects]
256,192,500,1,0,0:0:0:0:
192,192,1000,1,8,0:0:0:0:
320,192,1500,1,2,0:0:0:0:
256,192,2000,1,4,0:0:0:0:
192,192,2500,1,8,0:0:0:0:
""");
UiTheme.Current = UiTheme.Load(Path.Combine(AppContext.BaseDirectory, "ui-theme.json"));
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var checks = 0;
using var log = new StreamWriter(Path.Combine(output, "ui-checks.txt")) { AutoFlush = true };
var w = new MainWindow(map, startAudio: false, showInspector: false, usePreferences: false) { Width = 1920, Height = 1080 };
w.Show();
var deadline = DateTime.UtcNow.AddSeconds(30);
while (!w.Loading.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); }
Check(w.Loading.IsCompletedSuccessfully, "Map opened without audio output");
Pump();
var state = w.ArrangeArea.Model!.Session.State;
foreach (var (width, height) in new[] { (1600, 900), (1040, 720), (900, 600) })
{
    w.Width = width; w.Height = height; Pump();
    string[] controls = ["OpenButton", "HsModeButton", "UndoButton", "SelectTool", "MuteTool", "SnapToggle", "SnapModeBox", "SnapBox", "TripletSnapToggle", "AuditionPlacementButton", "DrumDetectButton", "DrumVstButton", "FitButton", "BrowserToggle", "InspectorToggle", "LaneDetailsToggle", "UsedLanesToggle", "ObjectLinesToggle", "VolumeToggle", "SaveButton", "ZoomSlider", "TimeZoomInButton", "TimeZoomOutButton", "RowHeightSlider", "RowZoomInButton", "RowZoomOutButton", "LocatorsButton", "ReturnOnStopButton", "FollowPlaybackButton", "SpeedPanel", "SongVolumeSlider", "SongMuteButton", "SongOffsetBox"];
    foreach (var id in controls.Where(id => id != "LocatorsButton" || width >= MainWindow.LocatorsMinWidth))
    {
        var c = Find<Control>(id);
        var rect = Rect(c);
        Check(c.IsEffectivelyVisible && rect.Width > 0 && rect.Height > 0 && rect.X >= 0 && rect.Right <= width + 1 && rect.Bottom <= height + 1, $"{width}: {id} visible and inside window ({rect})");
    }
    Check(Math.Abs(Rect(Find<Control>("Lcd")).Center.X - Rect(Find<Control>("StatusBar")).Center.X) < 1, $"{width}: time display centered");
    Check(Find<Control>("StatusBar").Bounds.Height == 56, $"{width}: original transport height remains 56 pixels");
    Check(w.FindControl<Control>("EditBar") is null && w.FindControl<Control>("SelectionDisclosure") is null, $"{width}: the selection bar is gone (its fields are in the inspector)");
    var ribbon = Find<StackPanel>("RibbonGroups");
    ribbon.Measure(Size.Infinity);
    Check(ribbon.DesiredSize.Width <= ribbon.Bounds.Width + 1 && Find<Control>("ControlBar").Bounds.Height < 60, $"{width}: the ribbon fits in one row (height {Find<Control>("ControlBar").Bounds.Height:0}, {ribbon.DesiredSize.Width:0} of {ribbon.Bounds.Width:0}, classes {string.Join(" ", Find<Control>("ControlBar").Classes)})");
    Check(Rect(Find<Control>("SaveButton")).Right <= width + 1 && Rect(Find<Control>("SaveButton")).Left >= Rect(ribbon).Right - 1, $"{width}: Save sits right of the ribbon groups");
    Check(Rect(Find<Control>("SongControls")).Top >= Rect(Find<Control>("GlobalVolumePanel")).Bottom, $"{width}: song controls beneath global volume");
    Check(Find<Control>("PlayButton").Bounds.Width > Find<Control>("StopButton").Bounds.Width && Find<Control>("PlayButton").Bounds.Height > Find<Control>("StopButton").Bounds.Height, $"{width}: Play is slightly larger");
    Check(w.FindControl<Control>("WorkflowFooter") is null, $"{width}: persistent banner removed");
    foreach (var button in w.GetLogicalDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Content is AppIcon))
    {
        var icon = (AppIcon)button.Content!;
        Check(icon.TryFindResource("Icon" + icon.Glyph, out var shape) && shape is Geometry, $"{width}: {button.Name} has a real icon geometry");
        Check(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)) && ToolTip.GetTip(button) is not null, $"{width}: {button.Name} has an accessible name and tooltip");
    }
    foreach (var glyph in new[] { "FolderOpen", "Save", "CirclePlus", "Info", "Settings", "Undo2", "Redo2", "MousePointer2", "Hand", "Pencil", "Eraser", "VolumeX", "Magnet", "ChevronDown", "Triplet", "Headphones", "PanelLeft", "ListFilter", "Columns3", "SlidersHorizontal", "Search", "Scan", "PanelRight", "ChevronUp", "Check", "Play", "SkipBack", "Square", "Repeat2", "LocateFixed" })
        Check(AppIcon.HasStudioAsset(glyph), $"{width}: {glyph} has its embedded approved Studio icon");
    var global = Rect(Find<Control>("GlobalVolumeSlider"));
    var song = Rect(Find<Control>("SongVolumeSlider"));
    Check(Math.Abs(global.X - song.X) < 1 && global.Width == 100 && song.Width == 100, $"{width}: volume sliders have equal widths and aligned tracks");
    var bottom = Rect(Find<Control>("StatusBar"));
    foreach (var id in new[] { "ReturnOnStopButton", "FollowPlaybackButton", "SpeedPanel" })
        Check(bottom.Contains(Rect(Find<Control>(id))), $"{width}: {id} remains in the bottom bar");
    Check(Find<Control>("LocatorsButton").IsVisible == width >= MainWindow.LocatorsMinWidth, $"{width}: the L/R button shows from {MainWindow.LocatorsMinWidth} px");
    var positions = new[] { "StartButton", "StopButton", "PlayButton", "LoopButton", "LocatorsButton", "ReturnOnStopButton", "FollowPlaybackButton", "SpeedPanel", "Lcd", "MixControls" }.Select(Find<Control>).Where(c => c.IsVisible).Select(Rect).ToArray();
    for (var i = 1; i < positions.Length; i++) Check(positions[i - 1].Right <= positions[i].Left + 1, $"{width}: transport controls {i - 1} and {i} do not overlap ({positions[i - 1].Right:0} vs {positions[i].Left:0})");
    Save($"editor-dark-{width}x{height}.png");
    Click("ThemeToggle");
    Check(!UiTheme.Current.DarkMode && UiTheme.Current.Get("title.bg") == "#555555", $"{width}: Not-as-dark mode restores original chrome colours");
    Check(ReferenceEquals(state, w.ArrangeArea.Model!.Session.State), $"{width}: theme switch preserves editor state");
    Save($"editor-not-as-dark-{width}x{height}.png");
    Click("ThemeToggle");
    Check(UiTheme.Current.DarkMode && UiTheme.Current.Get("title.bg") == "#1b1b1e", $"{width}: Dark mode restores Studio colours");
    foreach (var id in new[] { "InspectorToggle", "LaneDetailsToggle", "UsedLanesToggle", "ObjectLinesToggle", "VolumeToggle" })
    {
        var rect = Rect(Find<Control>(id));
        Check(Find<Control>(id).IsEffectivelyVisible && rect.X >= 0 && rect.Right <= width + 1 && rect.Bottom <= height + 1, $"{width}: Ribbon shows {id} inside the window ({rect})");
    }
    if (width >= MainWindow.LocatorsMinWidth)
    {
        Click("LocatorsButton");
        foreach (var id in new[] { "LeftLocatorBox", "RightLocatorBox", "GoLeftButton", "GoRightButton" })
            Check(Find<Control>(id).IsEffectivelyVisible && Rect(Find<Control>(id)).Y >= 0, $"{width}: L/R shows {id}");
        CloseFlyout("LocatorsButton");
    }
}
w.Width = 1920; w.Height = 1080; Pump();
// The Selection card: help text with nothing selected, the editing fields with a hit selected.
Check(Find<InspectorPanel>("Inspector").GetLogicalDescendants().Contains(Find<Control>("VolumeValueBox")), "Selection fields live in the inspector");
Check(!Find<Control>("SelectionFields").IsEffectivelyVisible && Find<Control>("SelectionHelp").IsEffectivelyVisible, "Nothing selected: the Selection card shows its help");
w.ArrangeArea.Edits.Select([state.Hits.First()]); Pump();
Check(Find<Control>("VolumeValueBox").IsEffectivelyVisible && Find<Control>("StartBox").IsEffectivelyVisible && Find<TextBlock>("InfoSelection").Text == "1 hit", "A selected hit shows start and volume fields in the Selection card");
Save("editor-selection-card.png");
w.ArrangeArea.Edits.Select([]); Pump();
// The Save badge: hidden with no problems; with problems, Save lists them instead of saving.
Check(!Find<Control>("SaveBadge").IsVisible, "No problems: no badge on Save");
var updateSaveCheck = typeof(MainWindow).GetMethod("UpdateSaveCheck", BindingFlags.Instance | BindingFlags.NonPublic)!;
updateSaveCheck.Invoke(w, [Array.Empty<Conflict>(), 2]); Pump();
Check(Find<Control>("SaveBadge").IsVisible && Find<TextBlock>("SaveBadgeText").Text == "2", "Two hits with no object: the badge on Save says 2");
Check(Rect(Find<Control>("RibbonGroups")).Right <= Rect(Find<Control>("SaveButton")).Left + 1 && Rect(Find<Control>("SaveButton")).Right <= w.Bounds.Width + 1, "With the badge, Save and the ribbon still fit side by side");
var savedBefore = File.ReadAllBytes(map);
Click("SaveButton");
Check(Find<Control>("SaveCheckPanel").IsEffectivelyVisible && Find<Control>("SaveContinueButton").IsEffectivelyVisible, "With problems, Save opens the check list");
Check(File.ReadAllBytes(map).SequenceEqual(savedBefore), "Opening the check list does not save");
Save("editor-save-check.png");
FlyoutBase.GetAttachedFlyout(Find<Button>("SaveButton"))!.Hide(); Pump();
updateSaveCheck.Invoke(w, [Array.Empty<Conflict>(), 0]); Pump();
Check(!Find<Control>("SaveBadge").IsVisible, "Problems gone: the badge hides again");
// Exercise actual pointer capture, not a direct width setter.
var lanes = w.ArrangeArea;
var oldWidth = ArrangeView.GridLeft;
var edge = lanes.TranslatePoint(new Point(oldWidth, lanes.HeaderHeight + 30), w)!.Value;
w.MouseMove(edge); w.MouseDown(edge, MouseButton.Left); w.MouseMove(edge + new Vector(80, 0)); w.MouseUp(edge + new Vector(80, 0), MouseButton.Left); Pump();
Check(Math.Abs(ArrangeView.GridLeft - oldWidth - 80) < 1, "Dragging the boundary resizes the lane column");
Check(Find<Control>("HScrollRow").Margin.Left == ArrangeView.GridLeft && Find<ScrollViewer>("VolumeControlScroller").Width == ArrangeView.GridLeft, "Timeline scrollbar and volume controller follow lane width");
Check(UiTheme.Current.Get("list.width") == UiTheme.Parts.SelectMany(part => part.Settings).Single(s => s.Key == "list.width").Default, "Dragging the lane column leaves the theme's default width alone");
// A press just inside the grid is a grid click, not a resize.
var resized = ArrangeView.GridLeft;
var inGrid = lanes.TranslatePoint(new Point(resized + 3, lanes.HeaderHeight + 30), w)!.Value;
w.MouseMove(inGrid); w.MouseDown(inGrid, MouseButton.Left); w.MouseMove(inGrid + new Vector(60, 0)); w.MouseUp(inGrid + new Vector(60, 0), MouseButton.Left); Pump();
Check(Math.Abs(ArrangeView.GridLeft - resized) < 0.5, "A press 3 px into the grid doesn't resize the lane column");
Save("editor-lanes-resized.png");
Click("FollowPlaybackButton");
Check(Find<MenuItem>("FollowItem").IsChecked == (Find<ToggleButton>("FollowPlaybackButton").IsChecked == true), "Follow button and menu remain bound");
Click("ReturnOnStopButton");
Check(Find<MenuItem>("ReturnOnStopItem").IsChecked == (Find<ToggleButton>("ReturnOnStopButton").IsChecked == true), "Return button and menu remain bound");
Find<ComboBox>("SnapModeBox").SelectedIndex = 1; Pump();
Check(lanes.Edits.SnapMode == SnapMode.GridRelative, "Visible grid selector changes the editing snap mode");
Click("SongMuteButton"); Check(lanes.SongMuted, "Song mute still drives playback state");
Click("LaneDetailsToggle"); Check(lanes.ShowIndexes, "Lane info still toggles sample indexes");
Click("ObjectLinesToggle"); Check(!lanes.ShowObjectLines && !Find<MenuItem>("ObjectLinesItem").IsChecked, "Object-line toggle still toggles drawn lines and its View menu item");
Click("UsedLanesToggle");
Check(lanes.Model!.UsedLanesOnly && Find<MenuItem>("UsedLanesItem").IsChecked, "Used lanes ribbon button filters lanes and synchronizes the View menu");
Click("UsedLanesToggle");
Check(!lanes.Model.UsedLanesOnly, "Used lanes ribbon button restores all lanes");
Click("InspectorToggle");
Check(!Find<Control>("Inspector").IsVisible && !Find<MenuItem>("InspectorPanelItem").IsChecked, "Inspector ribbon button hides the inspector and synchronizes the View menu");
Click("InspectorToggle");
Check(Find<Control>("Inspector").IsVisible, "Inspector ribbon button restores the inspector");
Click("VolumeToggle");
Check(!Find<Control>("BottomPanel").IsVisible, "Controller can still be hidden");
Click("VolumeToggle");
Check(Find<Control>("BottomPanel").IsVisible, "Controller can still be restored");
Check(ReferenceEquals(state, lanes.Model!.Session.State), "Layout and view changes preserve map edits and undo state");
typeof(MainWindow).GetField("workflowWarning", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(w, "Sample could not be read. Replace it before saving.");
typeof(MainWindow).GetMethod("RefreshWorkflowFeedback", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, null);
Check(Find<Control>("WorkflowStatusButton").IsVisible && Find<AppIcon>("WorkflowStatusIcon").Glyph == "TriangleAlert", "Warnings remain visible through a warning icon");
Click("WorkflowStatusButton");
Check(w.OwnedWindows.Any(d => d.Title == "Operation details"), "Operation details remain accessible");
w.OwnedWindows.Last().Close();
Check(Preferences.Load(Path.Combine(output, "missing-prefs.json")).SelectionBarCollapsed == false, "Existing preferences expand details by default");
var prefsPath = Path.Combine(output, "ui-preferences.json");
new Preferences { SelectionBarCollapsed = true, LaneListWidth = 430, DarkMode = false }.Save(prefsPath);
var prefs = Preferences.Load(prefsPath);
Check(prefs.SelectionBarCollapsed && prefs.LaneListWidth == 430 && !prefs.DarkMode, "Collapse and lane width preferences round-trip");
var speedButtons = Find<WrapPanel>("SpeedPanel").Children.OfType<ToggleButton>().ToArray();
Check(speedButtons.Length == 6, "All six playback speeds have separate visible buttons");
speedButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
var speedChoices = Find<StackPanel>("SpeedMenuPanel").Children.OfType<ToggleButton>().ToArray();
Check(speedChoices.Length == 6 && speedChoices[1].IsChecked == true && speedButtons[1].IsChecked == true && speedButtons.Count(button => button.IsChecked == true) == 1, "Separate speed buttons synchronize the Playback menu's speed row and keep one active speed");
speedChoices[4].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(speedButtons[4].IsChecked == true && speedChoices.Count(c => c.IsChecked == true) == 1, "The Playback menu's speed row synchronizes the visible speed buttons");
// Menu layout D: icons on every item, switches for on/off settings, choice rows instead of submenus.
var menu = Find<Menu>("HeaderMenu");
var items = menu.GetLogicalDescendants().OfType<MenuItem>().Where(i => i.Parent != menu).ToList();
Check(items.Count > 40 && items.All(i => i.Icon is AppIcon), $"Every Hitsound menu item has an icon ({items.Count} items)");
Check(items.Where(i => i.Name != "RecentItem").All(i => i.ItemCount == 0), "No submenus left in the Hitsound menus (Recent maps lists maps)");
Check(items.Where(i => i.ToggleType == MenuItemToggleType.CheckBox).All(i => i.Classes.Contains("switch")), "Every on/off menu setting shows a switch");
Check(menu.Items.OfType<MenuItem>().All(i => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(i))) && items.Where(i => i.Classes.Contains("switch")).All(i => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(i))), "Menu headings and switches retain accessible text names");
Check(items.All(i => i.ToggleType != MenuItemToggleType.Radio), "No radio items: choices are rows of buttons");
Find<ToggleButton>("PerObjectChoice").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(Find<ToggleButton>("PerObjectChoice").IsChecked == true && Find<ToggleButton>("GreenLinesChoice").IsChecked == false, "Export style row: Per-object is chosen and Green lines is not");
Find<ToggleButton>("GreenLinesChoice").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(Find<ToggleButton>("GreenLinesChoice").IsChecked == true && Find<ToggleButton>("PerObjectChoice").IsChecked == false, "Export style row: back to Green lines");
Find<ToggleButton>("StartLeftChoice").IsChecked = true; Pump();
Check(Find<ToggleButton>("StartCursorChoice").IsChecked == false, "Start playing from: one choice at a time");
Find<ToggleButton>("StartLeftChoice").IsChecked = false; Pump();
Check(Find<ToggleButton>("StartLeftChoice").IsChecked == true, "Start playing from: a choice can't be switched off to leave none");
Find<ToggleButton>("StartCursorChoice").IsChecked = true; Pump();
var fileMenu = Find<MenuItem>("FileMenu");
fileMenu.Open(); Pump();
Click("PerObjectChoice");
Check(fileMenu.IsSubMenuOpen && Find<ToggleButton>("PerObjectChoice").IsChecked == true, "Pointer click chooses Per-object and keeps File open");
Click("GreenLinesChoice");
Check(fileMenu.IsSubMenuOpen && Find<ToggleButton>("GreenLinesChoice").IsChecked == true, "Pointer click restores Green lines and keeps File open");
Save("menu-file-open.png");
var missingWarnings = Find<MenuItem>("MissingWarningsItem");
bool warningsBefore = missingWarnings.IsChecked;
ClickControl(missingWarnings);
Check(missingWarnings.IsChecked != warningsBefore, "Pointer click toggles a styled menu switch");
fileMenu.Close(); Pump();
Find<MenuItem>("PlaybackMenu").Open(); Pump();
Click("StartLeftChoice");
Check(Find<ToggleButton>("StartLeftChoice").IsChecked == true && Find<ToggleButton>("StartCursorChoice").IsChecked == false, "Pointer click chooses the left locator");
Click("StartLeftChoice");
Check(Find<ToggleButton>("StartLeftChoice").IsChecked == true, "Pointer clicking the active start choice keeps it selected");
Click("StartCursorChoice");
ClickControl(speedChoices[1]);
Check(Find<MenuItem>("PlaybackMenu").IsSubMenuOpen && speedChoices[1].IsChecked == true && speedButtons[1].IsChecked == true, "Pointer click changes speed in the open Playback menu");
Save("menu-playback-open.png");
Find<MenuItem>("PlaybackMenu").Close(); Pump();
// Zoom ends on the scrollbars.
double zoomBefore = lanes.Axis.Zoom;
Click("TimeZoomInButton");
Check(lanes.Axis.Zoom > zoomBefore, "The + at the horizontal scrollbar zooms in on time");
Click("TimeZoomOutButton");
Check(Math.Abs(lanes.Axis.Zoom - zoomBefore) < zoomBefore * 0.01, "The − at the horizontal scrollbar zooms back out");
double rowsBefore = lanes.RowHeight;
Click("RowZoomInButton");
Check(lanes.RowHeight > rowsBefore && Math.Abs(Find<Slider>("RowHeightSlider").Value - lanes.RowHeight) < 0.5, "The + at the vertical scrollbar makes lanes taller and the slider follows");
Find<Slider>("RowHeightSlider").Value = 40; Pump();
Check(Math.Abs(lanes.RowHeight - 40) < 0.5, "The lane-height slider sets the lane height");
Click("RowZoomOutButton");
Check(lanes.RowHeight < 40, "The − at the vertical scrollbar makes lanes shorter");
Save("editor-zoom-ends.png");
var customized = UiTheme.FromJson("{\"panel.bg\":\"#121314\",\"row.height\":\"42\"}");
var json = customized.ToJson();
customized.SetDarkMode(true);
Check(customized.Get("panel.bg") == "#121314" && customized.Number("row.height") == 42, "Dark mode preserves custom colours and dimensions");
customized.SetDarkMode(false);
Check(customized.ToJson() == json, "Switching themes does not rewrite the custom theme file");
w.Close();
log.WriteLine($"PASS: {checks} UI checks");
Console.WriteLine($"PASS: {checks} UI checks; screenshots in {output}");

void Check(bool condition, string message)
{
    log.WriteLine($"{(condition ? "PASS" : "FAIL")}: {message}");
    if (!condition) { Save("failure.png"); throw new Exception(message); }
    checks++;
}
T Find<T>(string name) where T : Control => w.FindControl<T>(name) ?? throw new Exception("Missing control: " + name);
Rect Rect(Control c) => new(c.TranslatePoint(default, w)!.Value, c.Bounds.Size);
void Click(string name)
{
    ClickControl(Find<Button>(name));
}
void ClickControl(Control b)
{
    var p = Rect(b).Center;
    w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left); Pump();
}
void CloseFlyout(string name) { Find<Button>(name).Flyout!.Hide(); Pump(); }
void Pump() { for (var i = 0; i < 6; i++) Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
void Save(string name) { Pump(); w.CaptureRenderedFrame()?.Save(Path.Combine(output, name)); }

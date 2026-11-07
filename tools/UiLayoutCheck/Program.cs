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
    string[] controls = ["SnapToggle", "SnapModeBox", "TripletSnapToggle", "AuditionPlacementButton", "InspectorToggle", "LaneDetailsToggle", "UsedLanesToggle", "ObjectLinesToggle", "VolumeToggle", "FitButton", "ZoomSlider", "LeftLocatorBox", "RightLocatorBox", "ReturnOnStopButton", "FollowPlaybackButton", "SpeedPanel", "SongVolumeSlider", "SongMuteButton", "SongOffsetBox"];
    foreach (var id in controls)
    {
        var c = Find<Control>(id);
        var rect = Rect(c);
        Check(c.IsEffectivelyVisible && rect.Width > 0 && rect.Height > 0 && rect.X >= 0 && rect.Right <= width + 1 && rect.Bottom <= height + 1, $"{width}: {id} visible and inside window ({rect})");
    }
    Check(Math.Abs(Rect(Find<Control>("Lcd")).Center.X - Rect(Find<Control>("StatusBar")).Center.X) < 1, $"{width}: time display centered");
    Check(Find<Control>("StatusBar").Bounds.Height == 56, $"{width}: original transport height remains 56 pixels");
    Check(Find<AppIcon>("SelectionDisclosureIcon").Glyph == "ChevronUp", $"{width}: expanded disclosure points up to collapse");
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
    var positions = new[] { "StartButton", "StopButton", "PlayButton", "LoopButton", "ReturnOnStopButton", "FollowPlaybackButton", "SpeedPanel", "Lcd", "MixControls" }.Select(id => Rect(Find<Control>(id))).ToArray();
    for (var i = 1; i < positions.Length; i++) Check(positions[i - 1].Right <= positions[i].Left + 1, $"{width}: transport controls {i - 1} and {i} do not overlap");
    Save($"editor-dark-{width}x{height}.png");
    Click("ThemeToggle");
    Check(!UiTheme.Current.DarkMode && UiTheme.Current.Get("title.bg") == "#555555", $"{width}: Not-as-dark mode restores original chrome colours");
    Check(ReferenceEquals(state, w.ArrangeArea.Model!.Session.State), $"{width}: theme switch preserves editor state");
    Save($"editor-not-as-dark-{width}x{height}.png");
    Click("ThemeToggle");
    Check(UiTheme.Current.DarkMode && UiTheme.Current.Get("title.bg") == "#1b1b1e", $"{width}: Dark mode restores Studio colours");
    var expanded = Find<Control>("EditBar").Bounds.Height;
    Click("SelectionDisclosure");
    Check(Find<AppIcon>("SelectionDisclosureIcon").Glyph == "ChevronDown", $"{width}: collapsed disclosure points down to expand");
    Check(!Find<Control>("InfoBarLeft").IsVisible && Find<Control>("EditBar").Bounds.Height < expanded, $"{width}: disclosure hides details and reclaims height");
    Click("SelectionDisclosure");
    Check(Find<Control>("InfoBarLeft").IsVisible, $"{width}: selection details expand again");
}
w.Width = 1920; w.Height = 1080; Pump();
// Exercise actual pointer capture, not a direct width setter.
var lanes = w.ArrangeArea;
var oldWidth = ArrangeView.GridLeft;
var edge = lanes.TranslatePoint(new Point(oldWidth, lanes.HeaderHeight + 30), w)!.Value;
w.MouseMove(edge); w.MouseDown(edge, MouseButton.Left); w.MouseMove(edge + new Vector(80, 0)); w.MouseUp(edge + new Vector(80, 0), MouseButton.Left); Pump();
Check(Math.Abs(ArrangeView.GridLeft - oldWidth - 80) < 1, "Dragging the boundary resizes the lane column");
Check(Find<ScrollBar>("HScroll").Margin.Left == ArrangeView.GridLeft && Find<ScrollViewer>("VolumeControlScroller").Width == ArrangeView.GridLeft, "Timeline scrollbar and volume controller follow lane width");
Save("editor-lanes-resized.png");
Click("FollowPlaybackButton");
Check(Find<MenuItem>("FollowItem").IsChecked == (Find<ToggleButton>("FollowPlaybackButton").IsChecked == true), "Follow button and menu remain bound");
Click("ReturnOnStopButton");
Check(Find<MenuItem>("ReturnOnStopItem").IsChecked == (Find<ToggleButton>("ReturnOnStopButton").IsChecked == true), "Return button and menu remain bound");
Find<ComboBox>("SnapModeBox").SelectedIndex = 1; Pump();
Check(lanes.Edits.SnapMode == SnapMode.GridRelative, "Visible grid selector changes the editing snap mode");
Click("SongMuteButton"); Check(lanes.SongMuted, "Song mute still drives playback state");
Click("LaneDetailsToggle"); Check(lanes.ShowIndexes, "Lane info still toggles sample indexes");
Click("ObjectLinesToggle"); Check(!lanes.ShowObjectLines, "Object-line button still toggles drawn lines");
Find<ToggleButton>("VolumeToggle").IsChecked = false; Pump();
Check(!Find<Control>("BottomPanel").IsVisible, "Controller can still be hidden");
Find<ToggleButton>("VolumeToggle").IsChecked = true; Pump();
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
Check(Find<MenuItem>("Speed50Item").IsChecked && speedButtons[1].IsChecked == true && speedButtons.Count(button => button.IsChecked == true) == 1, "Separate speed buttons synchronize the Playback menu and keep one active speed");
Find<MenuItem>("Speed125Item").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
Check(speedButtons[4].IsChecked == true, "Playback menu synchronizes the visible speed buttons");
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
    var b = Find<Button>(name);
    var p = Rect(b).Center;
    w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left); Pump();
}
void Pump() { for (var i = 0; i < 6; i++) Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
void Save(string name) { Pump(); w.CaptureRenderedFrame()?.Save(Path.Combine(output, name)); }

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
foreach (var (width, height) in new[] { (1920, 1080), (1600, 900), (1040, 720), (900, 600) })
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
    // UI refresh (2026-11-13): Save is in the title bar, left of the window buttons; the panel switches end the ribbon.
    Check(Rect(Find<Control>("SaveButton")).Bottom <= Rect(Find<Control>("ControlBar")).Top + 1 && Rect(Find<Control>("SaveButton")).Right <= Rect(Find<Control>("WindowBar")).Left + 1, $"{width}: Save sits in the title bar, left of the window buttons");
    Check(Rect(Find<Control>("WindowBar")).Right >= width - 1 && Rect(Find<Control>("WindowBar")).Top <= 1, $"{width}: the window buttons sit in the title bar's top right corner");
    Check(Rect(Find<Control>("RibbonPanels")).Left >= Rect(ribbon).Right - 1 && Rect(Find<Control>("BrowserToggle")).Right <= Rect(Find<Control>("InspectorToggle")).Left + 1, $"{width}: Files and Inspector end the ribbon, in the panels' order");
    // Wide windows (1500 px and up) put Global and Song on one line; narrower ones stack them (fidelity pass, 2026-11-13).
    if (width >= 1500) Check(Rect(Find<Control>("SongControls")).Left >= Rect(Find<Control>("GlobalVolumePanel")).Right - 1 && Math.Abs(Rect(Find<Control>("SongControls")).Center.Y - Rect(Find<Control>("GlobalVolumePanel")).Center.Y) < 2, $"{width}: Global and Song share one line");
    else Check(Rect(Find<Control>("SongControls")).Top >= Rect(Find<Control>("GlobalVolumePanel")).Bottom, $"{width}: song controls beneath global volume");
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
    Check((width >= 1500 || Math.Abs(global.X - song.X) < 1) && global.Width == 100 && song.Width == 100, $"{width}: volume sliders have equal widths (and aligned tracks when stacked)");
    var bottom = Rect(Find<Control>("StatusBar"));
    foreach (var id in new[] { "ReturnOnStopButton", "FollowPlaybackButton", "SpeedPanel" })
        Check(bottom.Contains(Rect(Find<Control>(id))), $"{width}: {id} remains in the bottom bar");
    Check(Find<Control>("LocatorsButton").IsVisible == width >= MainWindow.LocatorsMinWidth, $"{width}: the L/R button shows from {MainWindow.LocatorsMinWidth} px");
    var positions = new[] { "StartButton", "StopButton", "PlayButton", "LoopButton", "LocatorsButton", "ReturnOnStopButton", "FollowPlaybackButton", "SpeedPanel", "Lcd", "MixControls" }.Select(Find<Control>).Where(c => c.IsVisible).Select(Rect).ToArray();
    for (var i = 1; i < positions.Length; i++) Check(positions[i - 1].Right <= positions[i].Left + 1, $"{width}: transport controls {i - 1} and {i} do not overlap ({positions[i - 1].Right:0} vs {positions[i].Left:0})");
    Save($"editor-dark-{width}x{height}.png");
    // The five Default Color Schemes (Preferences, Colours) replace the old dark-mode button.
    Check(w.FindControl<Control>("ThemeToggle") is null, $"{width}: the dark-mode button is gone (schemes are in Preferences, Colours)");
    string[] schemeFiles = ["grey", "dark", "blue-grey", "teal", "slate"];
    for (int scheme = 0; scheme < UiTheme.SchemeCount; scheme++)
    {
        ThemeColors.Apply(UiTheme.Current, new UserColors { Scheme = scheme }, darkModeFallback: true); Pump();
        Save($"editor-scheme-{schemeFiles[scheme]}-{width}x{height}.png");
    }
    ThemeColors.Apply(UiTheme.Current, new UserColors { Scheme = 0 }, darkModeFallback: true); Pump();
    Check(!UiTheme.Current.DarkMode && UiTheme.Current.Get("title.bg") == "#555555", $"{width}: the Grey scheme restores the original chrome colours");
    Check(ReferenceEquals(state, w.ArrangeArea.Model!.Session.State), $"{width}: switching schemes preserves editor state");
    ThemeColors.Apply(UiTheme.Current, new UserColors { Scheme = 1 }, darkModeFallback: true); Pump();
    Check(UiTheme.Current.DarkMode && UiTheme.Current.Get("title.bg") == "#1b1b1e", $"{width}: the Dark scheme restores Studio colours");
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
Check(Rect(Find<Control>("HelpButton")).Right <= Rect(Find<Control>("SaveButton")).Left + 1 && Rect(Find<Control>("SaveButton")).Right <= Rect(Find<Control>("WindowBar")).Left + 1, "With the badge, Save still fits in the title bar between Help and the window buttons");
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
// UI refresh (2026-11-13): folded, the volume panel keeps its header, which carries the arrow that unfolds it.
Check(!Find<Control>("VolumeBody").IsVisible && Find<Control>("VolumeToggle").IsEffectivelyVisible && Find<Control>("BottomPanel").Bounds.Height < 40, "The volume panel folds to its header");
Click("VolumeToggle");
Check(Find<Control>("VolumeBody").IsVisible && Find<Control>("BottomPanel").Bounds.Height >= 80, "The volume panel unfolds again");
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

// Preferences, Colours: tick a row, move a slider (the editor recolours at once), Cancel puts it back, OK keeps it.
Window OpenColours()
{
    _ = typeof(MainWindow).GetMethod("ShowPreferences", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, null);
    Pump();
    var dialog = w.OwnedWindows.Last(x => x.Title == "Preferences");
    dialog.GetLogicalDescendants().OfType<TabControl>().First().SelectedIndex = 2; Pump();
    return dialog;
}
T Named<T>(Window window, string name) where T : Control =>
    window.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => AutomationProperties.GetName(c) == name) ?? throw new Exception("Missing " + name);
// The window has no saved scheme, so Preferences opens on the default one (Slate): show that, as the app does at start-up.
ThemeColors.Apply(UiTheme.Current, new UserColors(), darkModeFallback: true); Pump();
string accentBefore = UiTheme.Current.Get("button.active"), barBefore = UiTheme.Current.Get("line.bar");
var colourDialog = OpenColours();
Check(Named<CheckBox>(colourDialog, "Grid Colors Grid Lines").IsEnabled == false, "Grid Lines stays greyed out until Grid Background is ticked");
var accentCheck = Named<CheckBox>(colourDialog, "Custom Colors Accent Color");
accentCheck.IsChecked = true; Pump();
Check(UiTheme.Current.Get("button.active") == accentBefore, "ticking Accent Color starts from the colour already shown");
var accentSwatch = Named<Button>(colourDialog, "Custom Colors Accent Color colour");
var accentFlyout = (Flyout)accentSwatch.Flyout!;
accentFlyout.ShowAt(accentSwatch); Pump();
var accentSliders = ((Control)accentFlyout.Content!).GetLogicalDescendants().OfType<ColorSlider>().ToList();
Check(accentSliders.Select(AutomationProperties.GetName).SequenceEqual(["Hue", "Saturation", "Brightness"]), "Accent Color opens hue, saturation and brightness sliders");
accentSliders[0].Value = 30; Pump();
Check(UiTheme.Current.Get("button.active") != accentBefore && Hsb.FromColor(UiTheme.Current.Color("button.active")).H is > 25 and < 35, "moving the hue slider recolours the editor at once");
colourDialog.CaptureRenderedFrame()?.Save(Path.Combine(output, "preferences-colours.png"));
Named<CheckBox>(colourDialog, "Grid Colors Background").IsChecked = true; Pump();
var gridFlyout = (Flyout)Named<Button>(colourDialog, "Grid Colors Background colour").Flyout!;
accentFlyout.Hide(); gridFlyout.ShowAt(Named<Button>(colourDialog, "Grid Colors Background colour")); Pump();
Check(((Control)gridFlyout.Content!).GetLogicalDescendants().OfType<ColorSlider>().Select(AutomationProperties.GetName).SequenceEqual(["Saturation", "Brightness"]),
    "Grid Background opens saturation and brightness only, as Cubase's two-slider rows");
Check(Named<CheckBox>(colourDialog, "Grid Colors Grid Lines").IsEnabled, "ticking Grid Background enables Grid Lines");
gridFlyout.Hide(); Pump();
colourDialog.GetLogicalDescendants().OfType<Button>().First(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(UiTheme.Current.Get("button.active") == accentBefore && UiTheme.Current.Get("line.bar") == barBefore, "Cancel puts the previous colours back");
colourDialog = OpenColours();
Named<Button>(colourDialog, "Teal dark colour scheme").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Named<CheckBox>(colourDialog, "Ruler Colors Active Cycle").IsChecked = true; Pump();
var rulerFlyout = (Flyout)Named<Button>(colourDialog, "Ruler Colors Active Cycle colour").Flyout!;
rulerFlyout.ShowAt(Named<Button>(colourDialog, "Ruler Colors Active Cycle colour")); Pump();
((Control)rulerFlyout.Content!).GetLogicalDescendants().OfType<ColorSlider>().First().Value = 263; Pump();
rulerFlyout.Hide(); Pump();
Check(UiTheme.Current.Scheme == 3, "a Default Color Scheme swatch switches the scheme at once");
colourDialog.GetLogicalDescendants().OfType<Button>().First(b => Equals(b.Content, "OK")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
var keptPrefs = (Preferences)typeof(MainWindow).GetField("prefs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
Check(keptPrefs.Colors.Scheme == 3 && keptPrefs.Colors.Rows.ContainsKey(ThemeColors.RulerActive) && UiTheme.Current.Scheme == 3
    && Hsb.FromColor(UiTheme.Current.Color("cycle.active")).H is > 258 and < 268, "OK keeps the scheme and the Active Cycle colour");
Save("editor-after-colours.png");
ThemeColors.Apply(UiTheme.Current, new UserColors { Scheme = 1 }, darkModeFallback: true); Pump();
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

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using OsuHitsound.App;
using OsuHitsound.Lanes;
using System.IO.Compression;

string artifacts=Path.GetFullPath(args[0]);Directory.CreateDirectory(artifacts);
UiTheme.Current=UiTheme.Load(Path.Combine(AppContext.BaseDirectory,"ui-theme.json"));
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().SetupWithoutStarting();
int checks=0;
var w=Window(); var start=w.FindControl<StartScreen>("Start")!; start.IsVisible=true;Pump();
foreach(var size in new[]{(1920,1030),(1040,720),(900,500)})
{
    w.Width=size.Item1;w.Height=size.Item2;Pump();
    var scroller=start.FindControl<ScrollViewer>("StartScroller")!;
    scroller.Offset=default;Pump();
    Check(Within(start.FindControl<Button>("OpenPanel")!,start),$"Open visible at {size}");
    scroller.Offset=new Vector(0,scroller.Extent.Height);Pump();
    Check(Within(start.FindControl<Button>("PreferencesLink")!,start),$"Preferences reachable at {size}");
    Check(Within(start.FindControl<Button>("KeysLink")!,start),$"Shortcuts reachable at {size}");
    w.CaptureRenderedFrame()!.Save(Path.Combine(artifacts,$"start-{size.Item1}x{size.Item2}.png"));
}
w.Width=1600;w.Height=900;Pump();
await Open(w,Path.Combine(artifacts,"missing.osu"));
Check(start.IsVisible,"failed open keeps start retry controls");
var message=start.FindControl<TextBlock>("Message")!;
Check(message.Text?.Contains("Couldn't open") == true && message.IsEffectivelyVisible,"failed open visible on start screen");
var scroll=start.FindControl<ScrollViewer>("StartScroller")!; scroll.Offset=new Vector(0,scroll.Extent.Height);Pump();
Check(Within(message,start),"failure message reachable by scrolling");
start.FindControl<Button>("SamplesPanel")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Pump();
Check(!start.IsVisible && w.FindControl<Border>("BrowserHost")!.IsVisible,"Edit samples opens browser workspace");
var area=w.ArrangeArea;var model=area.Model!;var row=model.Rows.OfType<LaneRow>().First();
var p=area.TranslatePoint(new Point(140,area.HeaderHeight+area.RowHeight/2),w)!.Value;
w.MouseDown(p,MouseButton.Left);w.MouseUp(p,MouseButton.Left);Pump();
var inspector=w.FindControl<InspectorPanel>("Inspector")!;
Check(model.ActiveLane==row.Lane,"no-map lane selection works");
Check(inspector.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text==model.NameOf(row.Lane)),"Active lane names preview lane");
Check(inspector.GetLogicalDescendants().OfType<Button>().Any(b=>Equals(b.Content,"Preview") && b.IsEnabled),"default lane audition available before a map");
Check(inspector.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("Open a beatmap to change") == true),"sample assignment explains open-map requirement");
Check(!inspector.GetLogicalDescendants().OfType<ComboBox>().First().IsEnabled,"no-map workspace cannot accidentally edit samples");
Check(!model.Rows.OfType<GroupRow>().Any(),"sample-set folders absent");
w.Close();Pump();

string source=Directory.CreateDirectory(Path.Combine(artifacts,"source")).FullName;
string osu=Path.Combine(source,"test.osu");
File.WriteAllText(osu,"osu file format v14\n[General]\nSampleSet:Soft\n[Metadata]\nTitle:Fix validation\nVersion:Normal\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n256,192,1000,1,8,0:0:0:100:\n256,192,1500,1,8,0:0:0:100:\n");
string archive=Path.Combine(artifacts,"valid.osz");ZipFile.CreateFromDirectory(source,archive);
string songs=Directory.CreateDirectory(Path.Combine(artifacts,"songs")).FullName;
w=Window();start=w.FindControl<StartScreen>("Start")!;start.IsVisible=true;
typeof(MainWindow).GetField("prefs",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(w,new Preferences{SongsFolder=songs});
await Open(w,archive);
Check(!start.IsVisible,"public Open imports OSZ and hides start");
Check(w.ArrangeArea.Model!.Session.State.Hits.Count==4,"OSZ opens actual difficulty, not empty archive bytes");
Check(Directory.EnumerateFiles(songs,"*.osu",SearchOption.AllDirectories).Any(),"OSZ extracted inside disposable Songs folder");
var session=w.ArrangeArea.Model.Session;area=w.ArrangeArea;model=area.Model!;
area.Edits.Select(session.State.Hits);var box=w.FindControl<TextBox>("VolumeValueBox")!;
box.Text="37";box.Focus();PressEnter(w);
Check(session.State.Hits.All(h=>session.VolumeOf(h)==37),"Enter applies all-lane numeric volume");
Check(session.StepCount==1,"Enter adds exactly one undo step");
session.Undo();
var lane=session.State.Hits.First().Lane;model.SelectLane(lane);w.FindControl<ComboBox>("VolumeScopeBox")!.SelectedIndex=1;
box.Text="25";box.Focus();PressEnter(w);
Check(session.State.Hits.Where(h=>h.Lane==lane).All(h=>session.VolumeOf(h)==25),"Enter applies Selected lanes");
Check(session.State.Hits.Where(h=>h.Lane!=lane).All(h=>session.VolumeOf(h)==100),"Enter preserves other layers");
session.Undo();w.Close();Pump();

w=Window();start=w.FindControl<StartScreen>("Start")!;start.IsVisible=true;
typeof(MainWindow).GetField("prefs",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(w,new Preferences{SongsFolder=songs});
string invalid=Path.Combine(artifacts,"invalid.osz");File.WriteAllText(invalid,"not a zip");await Open(w,invalid);
Check(start.IsVisible && start.FindControl<TextBlock>("Message")!.Text?.Contains("Couldn't import") == true,"invalid OSZ error visible on start");
start.SetRecent(new[]{osu});var recent=start.FindControl<Button>("RecentPanel")!;
recent.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Pump();
var menu=(MenuFlyout)recent.Flyout!;Check(menu.Items.Count==1,"recent chooser populated");
((MenuItem)menu.Items[0]!).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));Wait(()=>!start.IsVisible);
Check(w.ArrangeArea.Model!.Session.State.Hits.Count==4,"recent item opens a difficulty");w.Close();Pump();
Console.WriteLine($"PASS: {checks} start-screen and flat-lane safety checks");

MainWindow Window(){var window=new MainWindow(null,startAudio:false,showInspector:false,usePreferences:false){Width=1600,Height=900};window.Show();Wait(()=>window.Loading.IsCompleted);return window;}
async Task Open(MainWindow window,string path){var task=window.Open(path);Wait(()=>task.IsCompleted);await task;Pump();}
void PressEnter(MainWindow window){window.KeyPress(Key.Enter,RawInputModifiers.None);window.KeyRelease(Key.Enter,RawInputModifiers.None);Pump();}
bool Within(Control control,Control parent){var p=control.TranslatePoint(default,parent);return p is { } v && v.Y>=-1 && v.Y+control.Bounds.Height<=parent.Bounds.Height+1;}
void Pump(){for(int i=0;i<8;i++)Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();}
void Wait(Func<bool> ready){var end=DateTime.UtcNow.AddSeconds(20);while(!ready() && DateTime.UtcNow<end){Pump();Thread.Sleep(5);}Pump();if(!ready())throw new Exception("UI timed out");}
void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);checks++;Console.WriteLine("PASS: "+name);}

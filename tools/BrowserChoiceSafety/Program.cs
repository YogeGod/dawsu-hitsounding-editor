using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using OsuHitsound.App;

string artifacts=Path.GetFullPath(args[0]);Directory.CreateDirectory(artifacts);
UiTheme.Current=UiTheme.Load(Path.Combine(AppContext.BaseDirectory,"ui-theme.json"));
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().SetupWithoutStarting();
int checks=0;
var w=Window();
var settingsTask=Dialogs.EditPreferences(w,new Preferences(),_=>[],()=>false);Pump();
var settings=w.OwnedWindows.Single(x=>x.Title=="Preferences");
var chooser=settings.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(c=>AutomationProperties.GetName(c)=="File browser style");
Check(chooser.SelectedIndex==0,"existing preferences default to folder tree");
chooser.SelectedIndex=1;Click(settings,"OK");Wait(()=>settingsTask.IsCompleted);
var chosen=(await settingsTask)!;Check(chosen.ClassicBrowser,"Preferences returns Classic on OK");
string prefFile=Path.Combine(artifacts,"preferences.json");chosen.Save(prefFile);
var loaded=Preferences.Load(prefFile);Check(loaded.ClassicBrowser,"Classic choice survives saving and loading");
SetStyle(true);
var inspector=w.FindControl<InspectorPanel>("Inspector")!;var browser=inspector.Browser;
var host=w.FindControl<Border>("BrowserHost")!;var toggle=w.FindControl<MenuItem>("BrowserPanelItem")!;
Check(!host.IsVisible && inspector.GetLogicalDescendants().Contains(browser),"Classic is hosted only inside the inspector");
var entries=(ListBox)typeof(FileBrowserPanel).GetField("entries",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(browser)!;
var location=browser.GetLogicalDescendants().OfType<TextBox>().Single();
Check(entries.Height==180 && Buttons(browser).Any(b=>Equals(b.Content,"Up")&&b.IsVisible),"Classic has compact list and Up navigation");
string library=Directory.CreateDirectory(Path.Combine(artifacts,"library")).FullName;
string kit=Directory.CreateDirectory(Path.Combine(library,"Kit")).FullName;
File.WriteAllBytes(Path.Combine(library,"top.wav"),[1]);File.WriteAllBytes(Path.Combine(kit,"child.wav"),[1]);
browser.SetRoots(library,library);Wait(()=>location.Text==library && entries.ItemCount==2);
Check(entries.Items.Cast<object>().Select(x=>x.ToString()).Order().SequenceEqual(new[]{"Kit","top.wav"}),"Classic lists only current-folder contents");
int previews=0,assignments=0;browser.PreviewRequested+=_=>previews++;browser.SampleRequested+=_=>assignments++;
entries.SelectedItem=entries.Items.Cast<object>().Single(x=>x.ToString()=="top.wav");Pump();
Check(previews==1&&assignments==0,"Classic selection previews without replacing");
browser.SetAssignmentTarget("Kick",true);Click(browser,"Add to selected lane");Check(assignments==1,"Classic explicit assignment works");
entries.SelectedItem=entries.Items.Cast<object>().Single(x=>x.ToString()=="Kit");Pump();Check(entries.ContainerFromIndex(entries.SelectedIndex)!.Focus(),"Classic folder row accepts keyboard focus");PressEnter(w);
Wait(()=>location.Text==kit&&entries.ItemCount==1);Check(entries.Items[0]!.ToString()=="child.wav","Classic Enter opens a folder");
Click(browser,"Up");Wait(()=>location.Text==library&&entries.ItemCount==2);Check(true,"Classic Up returns to parent folder");
toggle.IsChecked=false;Pump();Check(!browser.IsEffectivelyVisible,"View hides Classic browser");
toggle.IsChecked=true;Pump();Check(browser.IsEffectivelyVisible,"View restores Classic browser");
w.CaptureRenderedFrame()!.Save(Path.Combine(artifacts,"classic.png"));
SetStyle(false);Wait(()=>entries.ItemCount>2);
Check(ReferenceEquals(host.Child,browser)&&!inspector.GetLogicalDescendants().Contains(browser),"switching to tree reparents the same browser once");
Check(host.IsVisible&&double.IsNaN(entries.Height),"tree uses full sidebar height");
Check(location.Text==library,"switching styles keeps current folder");
Check(Buttons(browser).Any(b=>Equals(b.Content,"Up")&&!b.IsVisible),"tree hides Classic Up action");
var rootGrid=w.FindControl<Grid>("Root")!;
toggle.IsChecked=false;Pump();Check(!host.IsVisible&&rootGrid.ColumnDefinitions[0].ActualWidth==0,"hiding tree releases sidebar space");
toggle.IsChecked=true;Pump();Check(host.IsVisible,"View restores tree browser");
SetStyle(true);Wait(()=>entries.ItemCount==2);
Check(inspector.GetLogicalDescendants().Contains(browser)&&!host.IsVisible,"repeated switching restores Classic without duplicate parents");
foreach(var size in new[]{(1040,720),(900,500)})
{
    w.Width=size.Item1;w.Height=size.Item2;Pump();
    typeof(MainWindow).GetMethod("ShowBrowser",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(w,[true]);Pump();
    Check(browser.IsEffectivelyVisible&&rootGrid.ColumnDefinitions[0].ActualWidth==0,$"Classic can open without a sidebar at {size}");
}
w.Close();Pump();
w=Window();SetStyle(loaded.ClassicBrowser);
Check(w.FindControl<InspectorPanel>("Inspector")!.GetLogicalDescendants().Contains(w.FindControl<InspectorPanel>("Inspector")!.Browser),"saved choice restores Classic in a new window");
w.Close();Pump();Console.WriteLine($"PASS: {checks} browser-choice checks");

MainWindow Window(){var result=new MainWindow(null,startAudio:false,usePreferences:false){Width=1600,Height=900};result.Show();Pump();return result;}
void SetStyle(bool classic){typeof(MainWindow).GetField("prefs",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(w,new Preferences{ClassicBrowser=classic});typeof(MainWindow).GetMethod("ApplyBrowserStyle",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(w,null);Pump();}
IEnumerable<Button> Buttons(Control c)=>c.GetLogicalDescendants().OfType<Button>().Distinct();
void Click(Control c,string text){Buttons(c).Single(b=>Equals(b.Content,text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
void PressEnter(MainWindow window){window.KeyPress(Key.Enter,RawInputModifiers.None);window.KeyRelease(Key.Enter,RawInputModifiers.None);Pump();}
void Pump(){for(int i=0;i<8;i++)Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();}
void Wait(Func<bool> ready){var end=DateTime.UtcNow.AddSeconds(20);while(!ready()&&DateTime.UtcNow<end){Pump();Thread.Sleep(5);}Pump();if(!ready())throw new Exception("UI timed out");}
void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);checks++;Console.WriteLine("PASS: "+name);}
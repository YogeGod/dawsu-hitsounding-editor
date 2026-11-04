using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OsuHitsound.App;
using OsuHitsound.Lanes;

/// <summary>Exercises the real controls and renders the changed states without a sound device or user files.</summary>
internal static class TrustReview
{
    public static int Run(string output)
    {
        string temp = Directory.CreateTempSubdirectory("dawsu-ui-review-").FullName;
        MainWindow? window = null;
        try
        {
            string path = Path.Combine(temp, "Review - Sample arrangement (dawsu) [Hitsounds].osu");
            var objects = string.Join("\r\n", Enumerable.Range(0, 48).Select(i => $"256,192,{1000 + i * 250},1,{(i % 4 == 2 ? 8 : 0)},0:0:0:0:"));
            string original = "osu file format v14\r\n\r\n[General]\r\nSampleSet: Soft\r\n\r\n[Metadata]\r\nTitle:Sample arrangement\r\nArtist:Review\r\nCreator:dawsu\r\nVersion:Hitsounds\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n[TimingPoints]\r\n0,500,4,2,0,80,1,0\r\n\r\n[HitObjects]\r\n" + objects + "\r\n";
            File.WriteAllText(path, original);
            window = new MainWindow(path, startAudio: false, usePreferences: false) { Width = 1600, Height = 900 };
            window.Show();
            Until(() => window.Loading.IsCompleted);
            Check(!window.Loading.IsFaulted, "Map loads");
            var view = window.ArrangeArea;
            var model = view.Model!;
            var session = model.Session;
            var clap = model.LaneRows.First(r => r.Lane.Name == "soft-hitclap").Lane;
            var hit = session.HitAt(clap, 1000);
            session.Add([hit]);
            view.Edits.Select([hit]);
            Check(window.SaveForVerification().GetAwaiter().GetResult(), "Valid Save succeeds");
            Check(ReferenceEquals(session, view.Model!.Session), "Save retains session");
            Check(view.Selection.Contains(hit), "Save retains selection");
            Check(session.CanUndo && !session.IsEdited, "Save retains undo and records savepoint");
            session.Undo();
            Check(session.IsEdited, "Undo after Save becomes unsaved");
            session.Redo();
            Check(!session.IsEdited, "Redo to saved state becomes clean");

            byte[] before = File.ReadAllBytes(path);
            session.Remove(session.State.Hits.Where(h => h.Anchor == session.Moments[0].Key && h.Lane.Name.Contains("hitnormal")).ToList());
            session.Add([session.HitAt(clap, 1125)]);
            var intended = session.State;
            Check(!window.SaveForVerification().GetAwaiter().GetResult(), "Incomplete Save is blocked");
            Check(before.SequenceEqual(File.ReadAllBytes(path)), "Blocked Save leaves map unchanged");
            Check(ReferenceEquals(intended, session.State) && session.CanUndo, "Blocked Save preserves edits and undo");
            var issues = window.OwnedWindows.OfType<SaveIssuesWindow>().Single();
            Pump();
            Capture(issues, "02-save-issues.png");
            issues.Close();
            view.Axis.Left = 500;
            view.Axis.ZoomAt(0.35, 0);
            view.FocusLane(clap);
            Pump();
            Capture(window, "01-editor-needs-attention.png");

            // Every primary control can receive keyboard focus. Native Space activates a tool instead of transport.
            var draw = window.FindControl<Avalonia.Controls.Primitives.ToggleButton>("DrawTool")!;
            Check(draw.Focus(), "Draw tool accepts focus");
            window.KeyPress(Key.Space);
            window.KeyRelease(Key.Space);
            Check(view.Tool == Tool.Draw, "Space activates the focused tool");
            var snap = window.FindControl<ComboBox>("SnapModeBox")!;
            Check(snap.Focus(NavigationMethod.Tab), "Snap mode accepts keyboard focus");
            Pump();
            Capture(window, "04-keyboard-focus.png");

            var peer = ControlAutomationPeer.CreatePeerForElement(view);
            Check(peer.GetChildren().Count > 0, "Grid exposes rows to automation");
            Check(peer.GetChildren().Any(p => p.GetChildren().Any(c => c.GetProvider<IToggleProvider>() is not null)), "Lane mute and solo expose toggle providers");

            // Closing twice must never silently discard. Cancel is focused and default.
            window.Close();
            Pump();
            var prompt = window.OwnedWindows.Single(w => w.Title == "Unsaved edits");
            var cancel = prompt.FindControl<Button>("CloseCancel")!;
            Check(cancel.IsDefault && cancel.IsFocused, "Close defaults to Cancel");
            Capture(prompt, "03-unsaved-close-dialog.png");
            window.Close();
            Check(window.OwnedWindows.Count(w => w.Title == "Unsaved edits") == 1, "Repeated Close does not duplicate or bypass prompt");
            Click(cancel);
            Pump();
            Check(window.IsVisible && ReferenceEquals(intended, session.State), "Cancel preserves window and edits");
            window.Close();
            Pump();
            prompt = window.OwnedWindows.Single(w => w.Title == "Unsaved edits");
            Click(prompt.FindControl<Button>("CloseSave")!);
            Pump();
            Check(window.IsVisible && before.SequenceEqual(File.ReadAllBytes(path)), "Blocked Save from close leaves window and file intact");
            foreach (var owned in window.OwnedWindows.ToList()) owned.Close();
            window.Close();
            Pump();
            prompt = window.OwnedWindows.Single(w => w.Title == "Unsaved edits");
            Click(prompt.FindControl<Button>("CloseDiscard")!);
            Pump();
            Check(!window.IsVisible, "Explicit Discard closes");
            Console.WriteLine("PASS: save fidelity, preserved history/selection, close decisions, keyboard controls and lane automation.");
            return 0;
        }
        finally
        {
            // The fixture and all writes are isolated from user maps and editor preferences.
            if (window?.IsVisible == true)
            {
                foreach (var owned in window.OwnedWindows.ToList()) owned.Close();
                window.ArrangeArea.Model?.Session.MarkSaved();
                window.Close();
            }
            Directory.Delete(temp, recursive: true);
        }

        void Capture(Window target, string name)
        {
            Pump();
            var frame = target.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
            using (frame) frame.Save(Path.Combine(output, name));
            Console.WriteLine("Screenshot: " + name);
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static void Until(Func<bool> done)
    {
        var limit = DateTime.UtcNow.AddSeconds(30);
        while (!done()) { if (DateTime.UtcNow > limit) throw new TimeoutException(); Pump(); Thread.Sleep(10); }
        Pump();
    }
    private static void Pump() { for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
}

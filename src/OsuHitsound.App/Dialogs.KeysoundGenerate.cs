using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OsuHitsound.Audio;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

internal static partial class Dialogs
{
    /// <summary>
    /// Before Generate renders: what each included track exports and skips (unsynced notes, or synced to a changed
    /// target), and when a note's tail counts as finished. Returns the render limits, or null to go back.
    /// </summary>
    public static Task<RenderLimits?> KeysoundGenerate(Window owner, IReadOnlyList<KeyGenerateTrack> tracks) =>
        Show<RenderLimits?>(owner, "Generate keysounds", (body, close) =>
        {
            int synced = tracks.Sum(t => t.Synced.Count), skipped = tracks.Sum(t => t.Unsynced + t.Stale);
            body.Children.Add(new TextBlock
            {
                Text = $"Renders each synced note to its own WAV and writes a [Keysounds] copy of the reference into the project's output folder, " +
                       $"replacing its hitsounds. The source map isn't touched. {synced} note{(synced == 1 ? "" : "s")} will be exported" +
                       (skipped > 0 ? $"; {skipped} unsynced note{(skipped == 1 ? "" : "s")} will be skipped." : "."),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 560,
            });

            var list = new StackPanel { Spacing = 2 };
            foreach (var t in tracks)
            {
                string text = $"{t.Track.Name} ({t.Track.Instrument.DisplayName}): {t.Synced.Count} synced";
                if (t.Unsynced > 0) text += $", {t.Unsynced} unsynced skipped";
                if (t.Stale > 0) text += $", {t.Stale} with a changed target skipped";
                list.Children.Add(new TextBlock { Text = text, Foreground = t.Unsynced + t.Stale > 0 ? Paint.Brush("#ff8a5c") : null });
            }

            body.Children.Add(list);

            NumericUpDown Number(double value, double min, double max) => new()
            {
                Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, Increment = 1, FormatString = "0", Width = 110,
            };

            var defaults = new RenderLimits();
            var threshold = Number(defaults.ThresholdDb, -120, -30);
            var quiet = Number(defaults.QuietMs, 10, 5000);
            var maxTail = Number(defaults.MaxTailMs / 1000, 1, 600);
            body.Children.Add(new TextBlock { Text = "A note's tail ends when it stays quieter than the level below for the time below:", Margin = new Thickness(0, 8, 0, 0) });
            body.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Level (dB)", VerticalAlignment = VerticalAlignment.Center }, threshold,
                    new TextBlock { Text = "for (ms)", VerticalAlignment = VerticalAlignment.Center }, quiet,
                    new TextBlock { Text = "at most (s)", VerticalAlignment = VerticalAlignment.Center }, maxTail,
                },
            });

            return (() => close(new RenderLimits((double)(threshold.Value ?? -80), (double)(quiet.Value ?? 250), (double)(maxTail.Value ?? 10) * 1000)), "Generate");
        }, cancelText: "Go back");

    /// <summary>A small window showing Generate's progress, with Cancel.</summary>
    public sealed class Progress
    {
        private readonly Window window;
        private readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap, Width = 420 };
        private readonly ProgressBar bar = new() { Minimum = 0, Maximum = 1, Width = 420, Height = 8 };
        private readonly CancellationTokenSource cancel = new();
        private bool done;

        public Progress(Window owner, string title)
        {
            var stop = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80 };
            stop.Click += (_, _) => { cancel.Cancel(); text.Text = "Cancelling…"; };
            window = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
                Background = Paint.Brush(UiTheme.Current.Color("title.bg")),
                Content = new StackPanel { Spacing = 10, Margin = new Thickness(16), Children = { text, bar, stop } },
            };
            window.Classes.Add("editorDialog");
            window.Closing += (_, e) => { if (!done) { e.Cancel = true; cancel.Cancel(); } }; // closing cancels instead
            window.Show(owner);
        }

        public CancellationToken Token => cancel.Token;

        public void Report(string message, double fraction)
        {
            if (cancel.IsCancellationRequested) return;
            text.Text = message;
            bar.Value = Math.Clamp(fraction, 0, 1);
        }

        public void Close()
        {
            done = true;
            window.Close();
        }
    }
}

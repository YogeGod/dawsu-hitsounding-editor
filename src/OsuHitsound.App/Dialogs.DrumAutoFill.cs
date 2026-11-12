using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal static partial class Dialogs
{
    public static Task<DrumAutoFillOptions?> DrumAutoFill(Window owner, IReadOnlyList<Lane> lanes,
        Func<Lane, string> nameOf, string? lastKick, string? lastSnare, TimeRange? range) =>
        Show<DrumAutoFillOptions>(owner, "Drum hit detection", (body, close) =>
        {
            var choices = new[] { "Skip" }.Concat(lanes.Select(l => nameOf(l) + " · " + l.Name)).ToArray();
            int Initial(string? saved, string word, string fallback)
            {
                int index = lanes.ToList().FindIndex(l => ProjectStore.KeyOf(l) == saved);
                if (index < 0) index = lanes.ToList().FindIndex(l => nameOf(l).Contains(word, StringComparison.OrdinalIgnoreCase));
                if (index < 0) index = lanes.ToList().FindIndex(l => l.Sample?.Sound == fallback);
                return index + 1;
            }
            var kick = new ComboBox { Name = "AutoFillKick", ItemsSource = choices, SelectedIndex = Initial(lastKick, "kick", "hitnormal"), Width = 510 };
            var snare = new ComboBox { Name = "AutoFillSnare", ItemsSource = choices, SelectedIndex = Initial(lastSnare, "snare", "hitclap"), Width = 510 };
            var selected = new CheckBox { Name = "AutoFillRange", Content = range is null ? "Selected range (use the Range tool first)" : DrumRangeLabel(range),
                IsEnabled = range is not null, IsChecked = range is not null };
            var sensitivity = new ComboBox { Name = "AutoFillSensitivity", ItemsSource = new[] { "Conservative — fewer candidates", "Normal", "Sensitive — more candidates" }, SelectedIndex = 0, Width = 510 };
            var tolerance = new NumericUpDown { Name = "AutoFillTolerance", Value = 30, Minimum = 0, Maximum = 100, Increment = 5, Width = 110 };
            var unmatched = new CheckBox { Name = "AutoFillUnmatched", Content = "Include hits with no nearby gameplay object (for Generate)", IsChecked = false };
            var stem = new TextBox { Name = "AutoFillStem", Watermark = "Use the map's song", IsReadOnly = true, Width = 510 };
            var browse = new Button { Content = "Choose drum stem…" };
            browse.Click += async (_, _) =>
            {
                var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                { Title = "Choose an aligned drum stem", AllowMultiple = false, FileTypeFilter = [new("Audio") { Patterns = ["*.wav", "*.mp3", "*.ogg", "*.flac"] }] });
                if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) stem.Text = path;
            };
            var reset = new Button { Content = "Use map song" };
            reset.Click += (_, _) => stem.Text = null;
            var fileButtons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { browse, reset } };
            var problem = new TextBlock { Foreground = Brushes.OrangeRed };
            body.Children.AddRange([
                new TextBlock { Text = "Detect kick and snare on this PC, then review before adding hits. Toms are excluded.", TextWrapping = TextWrapping.Wrap, MaxWidth = 510 },
                new TextBlock { Text = "Uses the ADTOF drum model by Zehren, Alunno and Bientinesi (CC BY-NC-SA 4.0).", FontSize = 11, Opacity = .7, TextWrapping = TextWrapping.Wrap, MaxWidth = 510 },
                new TextBlock { Text = "Kick destination" }, kick,
                new TextBlock { Text = "Snare destination" }, snare,
                selected, new TextBlock { Text = "Sensitivity" }, sensitivity,
                new TextBlock { Text = "Attach to gameplay objects within (ms)" }, tolerance, unmatched,
                new TextBlock { Text = "Audio source — a stem must start at the same time as the song" }, stem, fileButtons, problem
            ]);
            return (() =>
            {
                if (kick.SelectedIndex <= 0 && snare.SelectedIndex <= 0) { problem.Text = "Choose at least one destination lane."; return; }
                close(new(kick.SelectedIndex > 0 ? lanes[kick.SelectedIndex - 1] : null,
                    snare.SelectedIndex > 0 ? lanes[snare.SelectedIndex - 1] : null,
                    selected.IsChecked == true, sensitivity.SelectedIndex switch { 1 => 1.0, 2 => .75, _ => 1.25 },
                    (double)(tolerance.Value ?? 30), unmatched.IsChecked == true, string.IsNullOrWhiteSpace(stem.Text) ? null : stem.Text));
            }, "Analyse");
        }, scroll: true);

    // Times always use a decimal point, like osu! and the rest of dawsu!, whatever the PC's locale.
    public static string DrumRangeLabel(TimeRange range) =>
        FormattableString.Invariant($"Selected range: {range.Start / 1000:F2}–{range.End / 1000:F2} seconds");

    public static string DrumHitLabel(double timeMs) => FormattableString.Invariant($"{timeMs / 1000:F3} s");

    public static Task<EditHit[]?> ReviewDrumAutoFill(Window owner, DrumAutoFillPlan plan, Func<Lane, string> nameOf, Action listen) =>
        Show<EditHit[]>(owner, "Review Drum hit detection", (body, close) =>
        {
            body.Children.Add(new TextBlock { Text = $"{plan.Hits.Count} new hits · {plan.Duplicates} duplicates skipped · {plan.Unmatched} without a nearby object\nUncheck any hit to leave it out. Existing hits and volumes stay. Apply is one Undo step; Save is separate.", TextWrapping = TextWrapping.Wrap, MaxWidth = 570 });
            var checks = plan.Hits.Select(h => new CheckBox { IsChecked = true,
                Content = DrumHitLabel(h.Time) + $" · {nameOf(h.Lane)} · {(h.Anchor is null ? "no object" : "on object")}" }).ToArray();
            var list = new StackPanel { Spacing = 3 };
            foreach (var check in checks) list.Children.Add(check);
            body.Children.Add(new ScrollViewer { Content = list, Height = 270, Width = 570 });
            var audition = new Button { Content = "Listen: song left / detected cues right" };
            audition.Click += (_, _) => listen();
            body.Children.Add(audition);
            return (() => close(plan.Hits.Where((_, i) => checks[i].IsChecked == true).ToArray()), "Apply selected hits");
        }, scroll: true);
}

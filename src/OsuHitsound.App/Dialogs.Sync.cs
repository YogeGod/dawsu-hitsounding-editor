using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OsuHitsound.Lanes;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

internal static partial class Dialogs
{
    private static readonly (HitPosition Position, string Label)[] SyncPositions =
    [
        (HitPosition.Circle, "Circles"), (HitPosition.SliderHead, "Slider heads"), (HitPosition.SliderRepeat, "Slider repeats"),
        (HitPosition.SliderTail, "Slider tails"), (HitPosition.SpinnerEnd, "Spinner ends"),
    ];

    private static string SyncTime(double ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(@"m\:ss\.fff");

    private static string SyncLabel(KeySyncTarget t, double noteTime) =>
        $"{SyncPositions.First(p => p.Position == t.Position).Label.TrimEnd('s').ToLowerInvariant()} #{t.ObjectIndex + 1} at {SyncTime(t.Time)} ({(t.Time - noteTime).ToString("+0.#;-0.#;0", System.Globalization.CultureInfo.InvariantCulture)} ms)";

    /// <summary>
    /// Sync to gameplay objects: choose the scope, the largest shift and the target kinds, review the proposed moves
    /// (picking a target where two are equally close), then apply. Returns the moves, or null on Cancel.
    /// </summary>
    public static Task<List<KeySyncMove>?> Sync(Window owner, KeysoundDocument doc, IReadOnlyCollection<KeyNote> selected) =>
        Show<List<KeySyncMove>?>(owner, "Sync to gameplay objects", (body, close) =>
        {
            var state = doc.Session.State;
            body.Children.Add(new TextBlock
            {
                Text = "Moves note starts onto the reference's circles, slider nodes and spinner ends. Pitch, length and velocity stay. " +
                       "Generate exports only synced notes. Undo puts the notes back.",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 620,
            });

            var selectedScope = new RadioButton { Content = $"Selected notes ({selected.Count})", GroupName = "syncScope", IsEnabled = selected.Count > 0, IsChecked = selected.Count > 0 };
            var allScope = new RadioButton { Content = "All tracks included in Generate", GroupName = "syncScope", IsChecked = selected.Count == 0 };
            body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { selectedScope, allScope } });

            var shift = new NumericUpDown { Minimum = 1, Maximum = 500, Increment = 1, FormatString = "0", Value = (decimal)KeySyncOptions.DefaultMaxShift, Width = 120 };
            var resync = new CheckBox { Content = "Also re-sync notes that are already synced" };
            body.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { new TextBlock { Text = "Largest move (ms)", VerticalAlignment = VerticalAlignment.Center }, shift, resync },
            });

            var kinds = SyncPositions.Select(p => (p.Position, Box: new CheckBox { Content = p.Label, IsChecked = true })).ToList();
            var kindRow = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var (_, box) in kinds) { box.Margin = new Avalonia.Thickness(0, 0, 12, 0); kindRow.Children.Add(box); }
            body.Children.Add(kindRow);

            var summary = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 620 };
            var rows = new StackPanel { Spacing = 2 };
            body.Children.Add(summary);
            body.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 340, Width = 640, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

            List<KeySyncProposal> proposals = [];
            var choices = new Dictionary<Guid, KeySyncTarget>();
            const int MaxRows = 400; // a whole project can propose thousands; the summary still counts them all

            void Refresh()
            {
                var options = new KeySyncOptions
                {
                    MaxShift = (double)(shift.Value ?? (decimal)KeySyncOptions.DefaultMaxShift),
                    Positions = kinds.Where(k => k.Box.IsChecked == true).Select(k => k.Position).ToHashSet(),
                    ResyncAll = resync.IsChecked == true,
                };
                var ids = selected.Select(n => n.Id).ToHashSet();
                var notes = selectedScope.IsChecked == true
                    ? state.Tracks.SelectMany(t => t.Notes.Where(n => ids.Contains(n.Id)).Select(n => (t.Id, n)))
                    : state.Tracks.Where(t => t.IncludeInGenerate).SelectMany(t => KeyEditing.InTimeOrder(t.Notes).Select(n => (t.Id, n)));
                proposals = KeySync.Preview(notes, doc.Targets, doc.Tempo, options);
                choices.Clear();
                rows.Children.Clear();
                foreach (var p in proposals.Take(MaxRows))
                {
                    string track = state.Track(p.Track)?.Name ?? "";
                    string head = $"{track} · {NoteEditor.PitchName(p.Note.Pitch)} at {SyncTime(p.NoteTime)}";
                    Control detail;
                    if (!p.InRange) detail = new TextBlock { Text = $"no target within {options.MaxShift:0} ms: stays unsynced", Opacity = 0.7 };
                    else if (!p.Ambiguous) detail = new TextBlock { Text = "→ " + SyncLabel(p.Candidates[0], p.NoteTime) };
                    else
                    {
                        var tied = p.Candidates.Where(c => Math.Abs(p.Distance(c) - p.Distance(p.Candidates[0])) < KeySync.TieTolerance).ToList();
                        var pick = new ComboBox { ItemsSource = (string[])["Choose a target…", .. tied.Select(c => SyncLabel(c, p.NoteTime))], SelectedIndex = 0, MinWidth = 300 };
                        var note = p.Note.Id;
                        pick.SelectionChanged += (_, _) =>
                        {
                            if (pick.SelectedIndex > 0) choices[note] = tied[pick.SelectedIndex - 1]; else choices.Remove(note);
                            Count();
                        };
                        detail = pick;
                    }

                    rows.Children.Add(new DockPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = head, Width = 250, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, [DockPanel.DockProperty] = Dock.Left },
                            detail,
                        },
                    });
                }

                if (proposals.Count > MaxRows) rows.Children.Add(new TextBlock { Text = $"…and {proposals.Count - MaxRows} more.", Opacity = 0.7 });
                Count();
            }

            void Count()
            {
                int moving = KeySync.Moves(proposals, doc.Tempo, choices).Count;
                int tied = proposals.Count(p => p.Ambiguous && !choices.ContainsKey(p.Note.Id));
                int outside = proposals.Count(p => !p.InRange);
                summary.Text = proposals.Count == 0
                    ? "Nothing to sync: every note in scope is already synced."
                    : $"{moving} note{(moving == 1 ? "" : "s")} will sync." +
                      (tied > 0 ? $" {tied} sit between two equally close targets: choose one, or they stay unsynced." : "") +
                      (outside > 0 ? $" {outside} have no target in range and stay unsynced." : "");
            }

            selectedScope.IsCheckedChanged += (_, _) => Refresh();
            shift.ValueChanged += (_, _) => Refresh();
            resync.IsCheckedChanged += (_, _) => Refresh();
            foreach (var (_, box) in kinds) box.IsCheckedChanged += (_, _) => Refresh();
            Refresh();
            return (() => close(KeySync.Moves(proposals, doc.Tempo, choices)), "Apply Sync");
        });
}

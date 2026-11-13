using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>Small modal dialogs: confirm, a text prompt, and picking one item from a list.</summary>
internal static partial class Dialogs
{
    public static async Task<bool> Confirm(Window owner, string title, string text, string ok = "OK", string cancel = "Cancel") =>
        await Show<bool>(owner, title, (body, close) =>
        {
            body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
            return (() => close(true), ok);
        }, cancel);

    public static async Task<string?> Prompt(Window owner, string title, string label, string initial) =>
        await Show<string?>(owner, title, (body, close) =>
        {
            var box = new TextBox { Text = initial, Width = 360 };
            body.Children.Add(new TextBlock { Text = label });
            body.Children.Add(box);
            box.AttachedToVisualTree += (_, _) => { box.Focus(); box.SelectAll(); };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) close(box.Text ?? ""); };
            return (() => close(box.Text ?? ""), "OK");
        });

    public static async Task<string?> Choose(Window owner, string title, string label, IReadOnlyList<string> items, string? initial = null, string accept = "Add") =>
        await Show<string?>(owner, title, (body, close) =>
        {
            // Typing jumps to the entries that start with what was typed (K → Kontakt), as in file dialogs.
            var list = new ListBox { ItemsSource = items, Height = 260, Width = 400, SelectedItem = initial };
            TypeAhead.Attach(list);
            list.AttachedToVisualTree += (_, _) =>
            {
                if (list.SelectedItem is { } s) list.ScrollIntoView(s);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => list.Focus()); // so typing a letter works at once
            };
            list.KeyDown += (_, e) => { if (e.Key == Key.Enter && list.SelectedItem is string s) close(s); };
            list.DoubleTapped += (_, _) => { if (list.SelectedItem is string s) close(s); };
            body.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
            body.Children.Add(list);
            return (() => { if (list.SelectedItem is string s) close(s); }, accept);
        });

    /// <summary>
    /// The time signature dialog: one change per line as "bar n/d" (e.g. "33 7/8"). Returns the changes, or null on Cancel.
    /// </summary>
    public static async Task<List<TimeSignature>?> TimeSignatures(Window owner, IReadOnlyList<TimeSignature> current) =>
        await Show<List<TimeSignature>>(owner, "Time signatures", (body, close) =>
        {
            var box = new TextBox
            {
                Text = string.Join("\n", current.Select(t => $"{t.Bar} {t.Numerator}/{t.Denominator}")),
                AcceptsReturn = true, Width = 300, Height = 180, Watermark = "33 7/8",
            };
            var problem = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
            body.Children.AddRange([
                new TextBlock { Text = "One change per line: bar, then beats/beat unit (e.g. 33 7/8).", TextWrapping = TextWrapping.Wrap, MaxWidth = 300 },
                box,
                new TextBlock { Text = "Only changes how bars are drawn in the editor. The map's timing is never touched.", TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Opacity = 0.7 },
                problem,
            ]);
            return (() =>
            {
                var changes = new List<TimeSignature>();
                foreach (var line in (box.Text ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split([' ', '/'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3 && int.TryParse(parts[0], out int bar) && int.TryParse(parts[1], out int n) && int.TryParse(parts[2], out int den)
                        && ArrangeModel.IsValid(new TimeSignature(bar, n, den)))
                        changes.Add(new TimeSignature(bar, n, den));
                    else
                    {
                        problem.Text = $"\"{line}\" isn't a change: bar 1 or later, 1–32 beats, and a beat unit of 1, 2, 4, 8 or 16.";
                        return;
                    }
                }

                close(changes);
            }, "OK");
        });

    /// <summary>The Repeat dialog: how many copies, and the gap between them in the selected snap steps.</summary>
    public static async Task<(int Copies, int Gap)?> Repeat(Window owner, int gap, SnapSpacing spacing) =>
        await Show<(int, int)?>(owner, "Repeat", (body, close) =>
        {
            var copies = new TextBox { Text = "1", Width = 80 };
            var steps = new TextBox { Text = gap.ToString(CultureInfo.InvariantCulture), Width = 80 };
            var problem = new TextBlock { Foreground = Brushes.OrangeRed };
            body.Children.AddRange([
                new TextBlock { Text = "Copies (1–64)" }, copies,
                new TextBlock { Text = $"Gap in snap steps of {spacing} (from one copy's start to the next)" }, steps,
                new TextBlock { Text = "The gap is counted on the grid, so it follows tempo changes.", TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Opacity = 0.7 },
                problem,
            ]);
            copies.AttachedToVisualTree += (_, _) => { copies.Focus(); copies.SelectAll(); };
            return (() =>
            {
                if (OsuNumber.TryParseInt(copies.Text ?? "", out int n) && n is >= 1 and <= HitEditor.MaxRepeatCopies
                    && OsuNumber.TryParseInt(steps.Text ?? "", out int g) && g is >= 1 and <= HitEditor.MaxRepeatGap)
                    close((n, g));
                else problem.Text = $"Copies must be 1–{HitEditor.MaxRepeatCopies} and the gap 1–{HitEditor.MaxRepeatGap} steps.";
            }, "Repeat");
        });

    /// <summary>The Range tool's Edit range dialog: what to do and the offset (ms, defaulting to the range's length).</summary>
    public static async Task<(RangeOperation Operation, double Offset)?> EditRange(Window owner, double length) =>
        await Show<(RangeOperation, double)?>(owner, "Edit range", (body, close) =>
        {
            var operation = new ComboBox { ItemsSource = Enum.GetValues<RangeOperation>(), SelectedIndex = 0, Width = 200 };
            var offset = new TextBox { Text = length.ToString("0.###", CultureInfo.InvariantCulture), Width = 200 };
            var problem = new TextBlock { Foreground = Brushes.OrangeRed };
            operation.SelectionChanged += (_, _) => offset.IsEnabled = operation.SelectedItem is not RangeOperation.Delete;
            body.Children.AddRange([
                new TextBlock { Text = "Operation" }, operation,
                new TextBlock { Text = "Offset (ms)" }, offset,
                new TextBlock { Text = "The range end is excluded. Gameplay objects are never moved.", TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Opacity = 0.7 },
                problem,
            ]);
            return (() =>
            {
                var op = operation.SelectedItem as RangeOperation? ?? RangeOperation.Copy;
                if (op == RangeOperation.Delete) close((op, 0));
                else if (OsuNumber.TryParseDouble(offset.Text ?? "", out double ms) && double.IsFinite(ms)) close((op, ms));
                else problem.Text = "The offset must be a number of milliseconds.";
            }, "OK");
        });

    /// <summary>
    /// Map → Copy hitsounds: the source difficulty, the targets (the mapset's other difficulties) and Mapping Tools'
    /// Hitsound Copier options. Returns the choice, or null on Cancel.
    /// </summary>
    public static async Task<(string Source, IReadOnlyList<string> Targets, HitsoundCopyOptions Options)?> CopyHitsounds(
        Window owner, IReadOnlyList<(string Path, string Name)> difficulties, string openMap)
    {
        Button? copyButton = null;
        Action updateCopyScope = () => { };
        return await Show<(string, IReadOnlyList<string>, HitsoundCopyOptions)?>(owner, "Copy hitsounds", (body, close) =>
        {
            var defaults = new HitsoundCopyOptions();
            var source = new ComboBox
            {
                ItemsSource = difficulties.Select(d => d.Name).ToList(), Width = 360,
                SelectedIndex = Math.Max(0, difficulties.ToList().FindIndex(d => string.Equals(d.Path, openMap, StringComparison.OrdinalIgnoreCase))),
            };
            var targets = new StackPanel { Spacing = 2 };
            var boxes = new List<(CheckBox Box, string Path)>();
            var targetChoices = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var mode = new ComboBox { ItemsSource = new[] { "Overwrite everything", "Overwrite only defined" }, SelectedIndex = 0, Width = 360 };
            var scope = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 420, FontWeight = FontWeight.SemiBold };
            void UpdateScope()
            {
                int count = boxes.Count(b => b.Box.IsChecked == true);
                if (copyButton is not null)
                {
                    copyButton.IsEnabled = count > 0;
                    copyButton.Content = count == 0 ? "Select target difficulties"
                        : $"{(mode.SelectedIndex == 0 ? "Replace hitsounds in" : "Copy into")} {count} difficult{(count == 1 ? "y" : "ies")}";
                }
                scope.Text = count == 0 ? "Choose the difficulties that should receive hitsounds. Nothing is selected yet."
                    : mode.SelectedIndex == 0
                    ? $"Replace hitsounds in {count} selected difficult{(count == 1 ? "y" : "ies")}. Their existing hitsounds are cleared first."
                    : $"Copy defined hit points into {count} selected difficult{(count == 1 ? "y" : "ies")}. Other hit points stay unchanged.";
            }
            updateCopyScope = UpdateScope;
            var selectAll = new Button { Content = "Select all" };
            var clearTargets = new Button { Content = "Clear selection" };
            selectAll.Click += (_, _) => { foreach (var (box, _) in boxes) box.IsChecked = true; };
            clearTargets.Click += (_, _) => { foreach (var (box, _) in boxes) box.IsChecked = false; };
            var targetActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            targetActions.Children.AddRange([selectAll, clearTargets]);
            void FillTargets()
            {
                foreach (var (box, path) in boxes) targetChoices[path] = box.IsChecked == true;
                targets.Children.Clear();
                boxes.Clear();
                foreach (var (path, name) in difficulties.Where((_, i) => i != source.SelectedIndex))
                {
                    var box = new CheckBox { Content = name, IsChecked = targetChoices.GetValueOrDefault(path, false) };
                    box.IsCheckedChanged += (_, _) => UpdateScope();
                    boxes.Add((box, path));
                    targets.Children.Add(box);
                }

                if (boxes.Count == 0) targets.Children.Add(new TextBlock { Text = "This mapset has no other difficulty to copy into.", Opacity = 0.7 });
                UpdateScope();
            }

            source.SelectionChanged += (_, _) => FillTargets();
            FillTargets();

            mode.SelectionChanged += (_, _) => UpdateScope();
            var leniency = new TextBox { Text = defaults.Leniency.ToString(CultureInfo.InvariantCulture), Width = 80 };
            CheckBox Option(string text, bool on, string tip)
            {
                var box = new CheckBox { Content = text, IsChecked = on };
                ToolTip.SetTip(box, tip);
                return box;
            }

            var hitsounds = Option("Copy hitsounds", defaults.CopyHitsounds, "What each circle and slider node plays: the sounds and their sample sets.");
            var bodySounds = Option("Copy slider-body sounds", defaults.CopyBodyHitsounds, "A matched slider takes the source slider's body sounds: its slide sample set and whistle loop.");
            var sets = Option("Copy sample sets and indexes", defaults.CopySampleSets, "Off: the targets keep their own sample sets and custom indexes.");
            var volumes = Option("Copy volumes", defaults.CopyVolumes, "Off: the targets keep their own volumes.");
            var keep5 = Option("Always keep 5 % volumes", defaults.AlwaysPreserve5Volume, "Hit points the target has at 5 % (muted on purpose) stay at 5 %.");
            var noSilent = Option("Never create silent hitnormals", defaults.NeverSilentHitnormals,
                "A target hit point keeps its own sound instead of a silent custom hitnormal (or a mix of only silent samples), " +
                "including points the source has nothing for. 5 % muted ends are a separate setting.");

            // Storyboard samples: copied from the source's [Events], skipped where a hit already plays them (or any hit).
            var storyboard = Option("Copy storyboarded samples", defaults.CopyStoryboardedSamples, "The source's own storyboard Sample lines go into the targets (Overwrite everything replaces theirs).");
            var satisfied = Option("Skip samples a hitsound already plays", defaults.IgnoreHitsoundSatisfiedSamples, "Not copied when a target hit point within the leniency plays the same file.");
            var whenever = Option("Skip samples at any hitsound", defaults.IgnoreWheneverHitsound, "Not copied when there's any target hit point within the leniency.");
            var storyboardOptions = new StackPanel { Spacing = 2, Margin = new Thickness(22, 0, 0, 0), IsEnabled = storyboard.IsChecked == true };
            storyboardOptions.Children.AddRange([satisfied, whenever]);
            storyboard.IsCheckedChanged += (_, _) => storyboardOptions.IsEnabled = storyboard.IsChecked == true;

            // Copy to slider ticks / slides (Overwrite only defined): new mixed samples from the start index up.
            var ticks = Option("Copy to slider ticks", defaults.CopyToSliderTicks, "A source hit with no target hit point goes onto a slider tick within the leniency, as a new mixed sample.");
            var slides = Option("Copy to slider slides", defaults.CopyToSliderSlides, "A source hit with no hit point or tick goes onto the slide of a slider there, as a new mixed sample.");
            var startIndex = new TextBox { Text = defaults.StartIndex.ToString(CultureInfo.InvariantCulture), Width = 80 };
            var startRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            startRow.Children.AddRange([new TextBlock { Text = "Start index", VerticalAlignment = VerticalAlignment.Center }, startIndex]);
            ToolTip.SetTip(startRow, "The first custom index the new samples use; taken ones are skipped.");
            var onSliders = new StackPanel { Spacing = 2 };
            onSliders.Children.AddRange([ticks, slides, startRow]);
            void OnlyDefinedOptions() => onSliders.IsEnabled = mode.SelectedIndex == 1;
            mode.SelectionChanged += (_, _) => OnlyDefinedOptions();
            OnlyDefinedOptions();

            // Mute slider ends (Mapping Tools' filter): snaps in priority order, the muted ones, length, index and set.
            var mute = Option("Mute slider ends", defaults.MuteSliderEnds, "Target slider ends the source has nothing for, with no additions, play a hitnormal at 5 % when they pass the filter below.");
            static string List(IEnumerable<int> divisors) => string.Join(", ", divisors);
            var snaps = new TextBox { Text = List(defaults.BeatDivisors), Width = 200 };
            var mutedSnaps = new TextBox { Text = List(defaults.MutedDivisors), Width = 200 };
            var minLength = new TextBox { Text = defaults.MinLength.ToString(CultureInfo.InvariantCulture), Width = 80 };
            var mutedIndex = new TextBox { Text = "", Width = 80, Watermark = "keep" };
            (SampleSet Set, string Name)[] mutedSets = [(SampleSet.None, "Keep"), (SampleSet.Normal, "Normal"), (SampleSet.Soft, "Soft"), (SampleSet.Drum, "Drum")];
            var mutedSet = new ComboBox { ItemsSource = mutedSets.Select(s => s.Name).ToList(), SelectedIndex = 0, Width = 120 };
            var muteGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(22, 2, 0, 0) };
            void Row(int r, string label, Control control, string tip)
            {
                var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 10, 2) };
                ToolTip.SetTip(text, tip);
                Grid.SetRow(text, r);
                Grid.SetRow(control, r);
                Grid.SetColumn(control, 1);
                muteGrid.Children.AddRange([text, control]);
            }

            Row(0, "Snaps (1/n), first wins", snaps, "An end snaps to the nearest of these; the first one (in this order) its time falls on decides.");
            Row(1, "Muted snaps", mutedSnaps, "Ends whose deciding snap is one of these are muted.");
            Row(2, "Minimum length (beats)", minLength, "Shorter sliders keep their end.");
            Row(3, "Muted index", mutedIndex, "The custom index muted ends play from; blank keeps theirs.");
            Row(4, "Muted sample set", mutedSet, "The sample set muted ends play in.");
            muteGrid.IsEnabled = mute.IsChecked == true;
            mute.IsCheckedChanged += (_, _) => muteGrid.IsEnabled = mute.IsChecked == true;

            static int[]? Divisors(string? text)
            {
                var parts = (text ?? "").Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
                var list = new List<int>();
                foreach (var p in parts)
                    if (OsuNumber.TryParseInt(p, out int d) && d is >= 1 and <= 64) list.Add(d);
                    else return null;
                return list.Count > 0 ? [.. list] : null;
            }

            var problem = new TextBlock { Foreground = Brushes.OrangeRed };
            var leniencyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            leniencyRow.Children.AddRange([leniency, new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center }]);

            var advanced = new StackPanel { Spacing = 8 };
            advanced.Children.AddRange([
                new TextBlock { Text = "Temporal leniency", FontWeight = FontWeight.SemiBold }, leniencyRow,
                bodySounds, onSliders, storyboard, storyboardOptions, mute, muteGrid,
            ]);
            body.Children.AddRange([
                new TextBlock { Text = "Copy from", FontWeight = FontWeight.SemiBold }, source,
                new TextBlock { Text = "Copy into", FontWeight = FontWeight.SemiBold },
                targetActions, new ScrollViewer { Content = targets, MaxHeight = 180 },
                new TextBlock { Text = "Mode", FontWeight = FontWeight.SemiBold }, mode,
                new TextBlock { Text = "Everything: the targets' hitsounds are cleared first. Only defined: only the hit points the source has a hit for change.", TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Opacity = 0.7 },
                scope,
                hitsounds, sets, volumes, keep5, noSilent,
                new Expander { Header = "Advanced: timing, sliders and storyboard", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch },
                new TextBlock { Text = "Nothing is written yet: the next step shows what changes in each difficulty. Each one is saved with a backup, like Save .osu. Same options as Mapping Tools' Hitsound Copier.", TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Opacity = 0.7 },
                problem,
            ]);
            return (() =>
            {
                var chosen = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Path).ToList();
                if (chosen.Count == 0) problem.Text = "Tick at least one difficulty to copy into.";
                else if (!OsuNumber.TryParseDouble(leniency.Text ?? "", out double ms) || !double.IsFinite(ms) || ms < 0) problem.Text = "The leniency must be a number of milliseconds, 0 or more.";
                else if (Divisors(snaps.Text) is not { } beatDivisors || Divisors(mutedSnaps.Text) is not { } muted) problem.Text = "Snaps are whole numbers 1–64 (1 = a beat, 4 = 1/4), separated by commas.";
                else if (!OsuNumber.TryParseDouble(minLength.Text ?? "", out double beats) || !double.IsFinite(beats) || beats < 0) problem.Text = "The minimum length is a number of beats, 0 or more.";
                else if (!string.IsNullOrWhiteSpace(mutedIndex.Text) && !(OsuNumber.TryParseInt(mutedIndex.Text, out int ix) && ix >= 0)) problem.Text = "The muted index is a whole number, 0 or more (or blank).";
                else if (!OsuNumber.TryParseInt(startIndex.Text ?? "", out int start) || start < 2) problem.Text = "The start index is a whole number, 2 or more.";
                else
                    close((difficulties[Math.Max(0, source.SelectedIndex)].Path, chosen, defaults with
                    {
                        Mode = mode.SelectedIndex == 1 ? CopyMode.OverwriteOnlyDefined : CopyMode.OverwriteEverything,
                        Leniency = ms,
                        CopyHitsounds = hitsounds.IsChecked == true,
                        CopyBodyHitsounds = bodySounds.IsChecked == true,
                        CopySampleSets = sets.IsChecked == true,
                        CopyVolumes = volumes.IsChecked == true,
                        AlwaysPreserve5Volume = keep5.IsChecked == true,
                        NeverSilentHitnormals = noSilent.IsChecked == true,
                        CopyToSliderTicks = ticks.IsChecked == true,
                        CopyToSliderSlides = slides.IsChecked == true,
                        StartIndex = start,
                        CopyStoryboardedSamples = storyboard.IsChecked == true,
                        IgnoreHitsoundSatisfiedSamples = satisfied.IsChecked == true,
                        IgnoreWheneverHitsound = whenever.IsChecked == true,
                        MuteSliderEnds = mute.IsChecked == true,
                        BeatDivisors = beatDivisors,
                        MutedDivisors = muted,
                        MinLength = beats,
                        MutedIndex = OsuNumber.TryParseInt(mutedIndex.Text ?? "", out int index) ? index : -1,
                        MutedSampleSet = mutedSets[Math.Max(0, mutedSet.SelectedIndex)].Set,
                    }));
            }, "Copy to selected difficulties");
        }, scroll: true, configureOk: button => { copyButton = button; updateCopyScope(); });
    }

    /// <summary>
    /// The key editor's Scale by velocity dialog, laid out as Scale by volume. <paramref name="count"/> says how many notes
    /// a choice changes, for the live preview. Returns the choice, or null on Cancel.
    /// </summary>
    public static async Task<(VelocityScope Scope, int From, int To, double Percent)?> ScaleByVelocity(Window owner, bool hasSelection, Func<VelocityScope, int, int, int> count) =>
        await Show<(VelocityScope, int, int, double)?>(owner, "Scale by velocity", (body, close) =>
        {
            (VelocityScope Scope, string Label)[] scopes = [(VelocityScope.Selected, "Selected notes"), (VelocityScope.SelectedTrack, "The selected track"), (VelocityScope.AllTracks, "All tracks")];
            var scope = new ComboBox
            {
                ItemsSource = scopes.Select(x => x.Label).ToList(),
                SelectedIndex = Array.FindIndex(scopes, x => x.Scope == (hasSelection ? VelocityScope.Selected : VelocityScope.SelectedTrack)),
                Width = 240,
            };
            var from = new TextBox { Text = "1", Width = 70 };
            var to = new TextBox { Text = "127", Width = 70 };
            var percent = new TextBox { Text = "100", Width = 70 };
            var preview = new TextBlock { Opacity = 0.8 };

            bool Read(out VelocityScope s, out int low, out int high, out double p)
            {
                s = scopes[Math.Max(0, scope.SelectedIndex)].Scope;
                p = 0;
                bool ok = OsuNumber.TryParseInt(from.Text ?? "", out int a) & OsuNumber.TryParseInt(to.Text ?? "", out int b)
                          && OsuNumber.TryParseDouble(percent.Text ?? "", out p) && double.IsFinite(p) && p >= 0;
                (low, high) = (Math.Min(a, b), Math.Max(a, b));
                return ok && low >= 1 && high <= 127;
            }

            void Update() => preview.Text = Read(out var s, out int a, out int b, out _)
                ? count(s, a, b) switch { 1 => "Changes 1 note.", var n => $"Changes {n} notes." }
                : "Velocities are 1–127, and the scale is a percentage of at least 0. Scaled velocities stay 1–127.";

            foreach (var box in new[] { from, to, percent }) box.TextChanged += (_, _) => Update();
            scope.SelectionChanged += (_, _) => Update();
            var range = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            range.Children.AddRange([from, new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center }, to]);
            body.Children.AddRange([
                new TextBlock { Text = "Look at" }, scope,
                new TextBlock { Text = "Notes with a velocity from" }, range,
                new TextBlock { Text = "Scale by (%; 50 halves, 150 is half as loud again)" }, percent,
                preview,
            ]);
            Update();
            return (() =>
            {
                if (Read(out var s, out int a, out int b, out double p)) close((s, a, b, p));
            }, "Scale");
        });

    /// <summary>
    /// The Scale by volume dialog. <paramref name="count"/> says how many circles and slider ends a choice changes, for
    /// the live preview. Returns the choice, or null on Cancel.
    /// </summary>
    public static async Task<(VolumeScope Scope, int From, int To, double Percent)?> ScaleByVolume(Window owner, bool hasSelection, Func<VolumeScope, int, int, int> count) =>
        await Show<(VolumeScope, int, int, double)?>(owner, "Scale by volume", (body, close) =>
        {
            (VolumeScope Scope, string Label)[] scopes = [(VolumeScope.Selected, "Selected hits"), (VolumeScope.SelectedLanes, "Lanes of the selected hits"), (VolumeScope.All, "All hits")];
            var scope = new ComboBox
            {
                ItemsSource = scopes.Select(x => x.Label).ToList(),
                SelectedIndex = Array.FindIndex(scopes, x => x.Scope == (hasSelection ? VolumeScope.Selected : VolumeScope.All)),
                Width = 240,
            };
            var from = new TextBox { Text = "1", Width = 70 };
            var to = new TextBox { Text = "100", Width = 70 };
            var percent = new TextBox { Text = "100", Width = 70 };
            var preview = new TextBlock { Opacity = 0.8 };

            // The choice as typed, with the volume bounds in order; false while a box isn't a valid number.
            bool Read(out VolumeScope s, out int low, out int high, out double p)
            {
                s = scopes[Math.Max(0, scope.SelectedIndex)].Scope;
                p = 0;
                bool ok = OsuNumber.TryParseInt(from.Text ?? "", out int a) & OsuNumber.TryParseInt(to.Text ?? "", out int b)
                          && OsuNumber.TryParseDouble(percent.Text ?? "", out p) && double.IsFinite(p) && p >= 0;
                (low, high) = (Math.Min(a, b), Math.Max(a, b));
                return ok && low >= 0 && high <= 100;
            }

            void Update() => preview.Text = Read(out var s, out int a, out int b, out _)
                ? count(s, a, b) switch { 1 => "Changes 1 circle or slider end.", var n => $"Changes {n} circles and slider ends." }
                : "Volumes are 0–100 %, and the scale is a percentage of at least 0. Scaled volumes stay 1–100 %.";

            foreach (var box in new[] { from, to, percent }) box.TextChanged += (_, _) => Update();
            scope.SelectionChanged += (_, _) => Update();
            var range = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            range.Children.AddRange([from, new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center }, to, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center }]);
            body.Children.AddRange([
                new TextBlock { Text = "Look at" }, scope,
                new TextBlock { Text = "Circles and slider ends with a volume from" }, range,
                new TextBlock { Text = "Scale by (%; 50 halves, 150 is half as loud again)" }, percent,
                new TextBlock { Text = "A circle has one volume, so every lane on it changes, also lanes outside this choice.", TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Opacity = 0.7 },
                preview,
            ]);
            Update();
            return (() =>
            {
                if (Read(out var s, out int a, out int b, out double p)) close((s, a, b, p));
            }, "Scale");
        });

    /// <summary>The preferences form; the edited copy on OK, null on Cancel.</summary>
    public static async Task<Preferences?> EditPreferences(Window owner, Preferences prefs, Func<OutputKind, IReadOnlyList<(string Id, string Name)>> devices, Func<bool> showAsioPanel,
        Action<UserColors>? previewColours = null, Action<UserColors>? applyColours = null) =>
        await Show<Preferences>(owner, "Preferences", (body, close) =>
        {
            var general = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            TextBox PathRow(StackPanel page, string label, string? value, string note, string? filePattern)
            {
                var box = new TextBox { Text = value ?? "", Width = 380, Watermark = note };
                var browse = new Button { Content = "Browse…" };
                browse.Click += async (_, _) =>
                {
                    var storage = TopLevel.GetTopLevel(body)!.StorageProvider;
                    var picked = filePattern is null
                        ? (await storage.OpenFolderPickerAsync(new() { Title = label })).FirstOrDefault()?.TryGetLocalPath()
                        : (await storage.OpenFilePickerAsync(new() { Title = label, FileTypeFilter = [new(filePattern) { Patterns = [filePattern] }] }))
                            .FirstOrDefault()?.TryGetLocalPath();
                    if (picked is not null) box.Text = picked;
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                row.Children.AddRange([box, browse]);
                page.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold });
                page.Children.Add(row);
                return box;
            }

            var songs = PathRow(general, "osu! Songs folder (Open starts here)", prefs.SongsFolder, "Not found yet", filePattern: null);
            var hitsounds = PathRow(general, "Hitsound library folder", prefs.HitsoundFolder, "Choose your sample library", filePattern: null);
            general.Children.Add(new TextBlock { Text = "File browser", FontWeight = FontWeight.SemiBold });
            var browserStyle = new ComboBox
            {
                ItemsSource = new[] { "Folder tree · sidebar", "Classic · inside inspector" },
                SelectedIndex = prefs.ClassicBrowser ? 1 : 0, Width = 380,
            };
            Avalonia.Automation.AutomationProperties.SetName(browserStyle, "File browser style");
            general.Children.Add(browserStyle);
            var lazer = PathRow(general, "Default sounds: lazer's osu.Game.Resources.dll", prefs.LazerResources, "Blank: the usual lazer install, else the built-in sounds", "osu.Game.Resources.dll");

            // Audio output: the kind, then that kind's devices (refilled when the kind changes).
            general.Children.Add(new TextBlock { Text = "Audio output", FontWeight = FontWeight.SemiBold });
            var kinds = Enum.GetValues<OutputKind>();
            var kind = new ComboBox { ItemsSource = kinds.Select(AudioOutputs.Name).ToList(), Width = 380, SelectedIndex = Array.IndexOf(kinds, prefs.AudioOutput) };
            var device = new ComboBox { Width = 380 };
            var asioPanel = new Button { Content = "ASIO control panel…" };
            ToolTip.SetTip(asioPanel, "The ASIO driver's own settings (buffer size, outputs). Works once ASIO is the output in use.");
            IReadOnlyList<(string Id, string Name)> shown = [];
            void FillDevices(string? select)
            {
                var k = kinds[Math.Max(0, kind.SelectedIndex)];
                shown = devices(k);
                device.ItemsSource = shown.Count == 0 ? ["(none found)"] : shown.Select(d => d.Name).ToList();
                device.SelectedIndex = Math.Max(0, shown.ToList().FindIndex(d => d.Id == (select ?? "")));
                asioPanel.IsVisible = k == OutputKind.Asio;
            }

            kind.SelectionChanged += (_, _) => FillDevices(null);
            asioPanel.Click += (_, _) => showAsioPanel();
            FillDevices(prefs.OutputDevice);
            general.Children.AddRange([kind, device, asioPanel]);

            var bufferText = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
            var buffer = new Slider { Minimum = 10, Maximum = 500, Value = prefs.BufferMs, Width = 380, TickFrequency = 10, IsSnapToTickEnabled = true };
            void ShowBuffer() => bufferText.Text = $"Buffer: {buffer.Value:0} ms. Hitsounds and the song always stay in step; a small buffer makes playing, stopping and previews react sooner. Raise it only if playback crackles.";
            buffer.ValueChanged += (_, _) => ShowBuffer();
            ShowBuffer();
            general.Children.Add(bufferText);
            general.Children.Add(buffer);

            // Song offset for all maps: added to each map's own Offset (top right).
            general.Children.Add(new TextBlock { Text = "Song offset for all maps (ms)", FontWeight = FontWeight.SemiBold });
            var globalOffset = new TextBox { Text = prefs.GlobalSongOffset.ToString(CultureInfo.InvariantCulture), Width = 100 };
            general.Children.Add(globalOffset);
            general.Children.Add(new TextBlock
            {
                Text = "Maps are timed against osu!'s own audio clock, so the song is played and drawn this much earlier (negative) everywhere. Each map's Offset box adds to it. -17 suits most maps; lazer exports may need about -8 more.",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 460, Opacity = 0.7,
            });

            var keys = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            var editedKeys = KeysPage(keys, prefs, body, PathRow);

            var colours = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            colours.Children.Add(new TextBlock { Text = "User Interface – General Colors", FontWeight = FontWeight.Bold, FontSize = 15 });
            var editedColours = ColoursPage(colours, prefs.Colors, prefs.DarkMode, previewColours ?? (_ => { }), applyColours ?? (_ => { }));

            body.Children.Add(new TabControl
            {
                ItemsSource = new[]
                {
                    new TabItem { Header = "General", Content = general }, new TabItem { Header = "Keys", Content = keys },
                    new TabItem { Header = "Colours", Content = colours },
                },
            });

            return (() => close(editedKeys() with
            {
                Colors = editedColours(),
                SongsFolder = Blank(songs.Text),
                HitsoundFolder = Blank(hitsounds.Text),
                ClassicBrowser = browserStyle.SelectedIndex == 1,
                LazerResources = Blank(lazer.Text),
                AudioOutput = kinds[Math.Max(0, kind.SelectedIndex)],
                OutputDevice = shown.Count > 0 && device.SelectedIndex >= 0 && shown[device.SelectedIndex].Id is { Length: > 0 } id ? id : null,
                BufferMs = (int)buffer.Value,
                GlobalSongOffset = OsuNumber.TryParseDouble(globalOffset.Text ?? "", out double offset) && double.IsFinite(offset) ? Math.Clamp(offset, -1000, 1000) : prefs.GlobalSongOffset,
            }), "OK");
        });

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// The Keys tab, after Cubase's Key Commands window: the preset and Cubase file on top, the commands in folders with
    /// their keys on the left, and on the right the picked command's keys, where a pressed key is shown with its current
    /// owner before Assign adds it. Returns a function giving the preferences with the key settings as edited.
    /// </summary>
    private static Func<Preferences> KeysPage(StackPanel page, Preferences prefs, Control body,
        Func<StackPanel, string, string?, string, string?, TextBox> pathRow)
    {
        var edited = prefs;
        var presets = new[] { (Preset: KeyPreset.Cubase, Name: "DAW-like (with your Cubase Key Commands file, if any)"), (Preset: KeyPreset.Osu, Name: "osu! editor (wheel and arrows move the playhead; Z X C V; 1 and 2)") };
        page.Children.Add(new TextBlock { Text = "Key preset", FontWeight = FontWeight.SemiBold });
        var preset = new ComboBox { ItemsSource = presets.Select(p => p.Name).ToList(), Width = 380 };
        preset.SelectedIndex = Array.FindIndex(presets, p => p.Preset == prefs.KeyPreset);
        page.Children.Add(preset);

        var cubaseRows = new StackPanel { Spacing = 8 };
        var file = pathRow(cubaseRows, "Cubase Key Commands file", prefs.CubaseKeyFile ?? CubaseKeyFile.FindNewest(),
            "Not found: it's in %AppData%\\Steinberg\\Cubase …", "Key Commands.xml");
        var reload = new Button { Content = "Reload" };
        ((StackPanel)cubaseRows.Children[^1]).Children.Add(reload);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 700, Opacity = 0.8 };
        page.Children.AddRange([cubaseRows, status]);

        // Left: search and the command tree. Right: the picked command.
        var search = new TextBox { Watermark = "Search commands or keys…", Width = 270 };
        // All / Assigned / Customized / Unassigned; with search, starts blank each time Preferences opens.
        var filter = new ComboBox { Width = 144 };
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        searchRow.Children.AddRange([search, filter]);
        var tree = new TreeView { Height = 360, Width = 420 };
        var left = new StackPanel { Spacing = 6 };
        left.Children.AddRange([searchRow, tree]);

        var name = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 260, Text = "Pick a command" };
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 260, Opacity = 0.8 };
        var keyList = new ListBox { Height = 90, Width = 260 };
        TypeAhead.Attach(keyList, dialogWide: false); // the command tree beside it takes letters too
        var remove = new Button { Content = "Remove" };
        var clear = new Button { Content = "Clear command" };
        var resetCommand = new Button { Content = "Reset command" };
        var keyButtons = new WrapPanel { MaxWidth = 260 };
        foreach (var b in new[] { remove, clear, resetCommand }) { b.Margin = new Thickness(0, 0, 6, 6); keyButtons.Children.Add(b); }
        var capture = new TextBox { IsReadOnly = true, Width = 260, Watermark = "Click here, then press a key" };
        var assignedTo = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
        var assign = new Button { Content = "Assign", IsEnabled = false };
        var right = new StackPanel { Spacing = 6, Width = 260 };
        right.Children.AddRange([name, description, new TextBlock { Text = "Keys", FontWeight = FontWeight.SemiBold }, keyList, keyButtons,
            new TextBlock { Text = "Type in key", FontWeight = FontWeight.SemiBold }, capture, assignedTo, assign]);

        var columns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        columns.Children.AddRange([left, right]);
        var resetPreset = new Button { Content = "Reset preset…" };
        var import = new Button { Content = "Import…" };
        var export = new Button { Content = "Export…" };
        var presetButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        presetButtons.Children.AddRange([resetPreset, import, export]);
        page.Children.AddRange([columns, presetButtons]);

        Dictionary<string, List<string>> Overrides() => edited.KeyOverrides.GetValueOrDefault(edited.KeyPreset.ToString()) ?? [];
        Dictionary<string, IReadOnlyList<KeyGesture>> current = [];
        Dictionary<string, IReadOnlyList<KeyGesture>> presetKeys = []; // the key preset alone, for "customized"
        string? selectedId = null;
        HashSet<string> shown = []; // rows in the tree: kept after an edit until the filter or search changes
        bool Customized(string id) => Keymap.IsCustomized(current, presetKeys, id);
        string PresetKeysOf(string id) => presetKeys.GetValueOrDefault(id, []) is { Count: > 0 } k ? EditorCommand.Text(k) : "Unassigned";
        string[] filterNames = ["All", "Assigned", "Customized", "Unassigned"];
        string PresetName() => presets.First(p => p.Preset == edited.KeyPreset).Name.Split(' ')[0];
        KeyGesture? pending = null;
        bool HasKeys(string id) => current.GetValueOrDefault(id, []).Count > 0;
        string KeysOf(string id) => HasKeys(id) ? EditorCommand.Text(current[id]) : "Unassigned";

        void ShowCommand()
        {
            pending = null;
            capture.Text = "";
            assignedTo.Text = "";
            assign.IsEnabled = false;
            bool any = selectedId is not null;
            foreach (var c in new Control[] { keyList, remove, clear, capture }) c.IsEnabled = any;
            if (selectedId is not { } id)
            {
                resetCommand.IsEnabled = false;
                (name.Text, description.Text, keyList.ItemsSource) = ("Pick a command", "", null);
                return;
            }

            var command = EditorCommands.Get(id);
            name.Text = command.Description;
            description.Text = $"{command.Group}{(command.KeysoundGroup is { } ks ? $" · {ks}" : "")}{(Customized(id) ? $" · customized (preset: {PresetKeysOf(id)})" : "")}";
            keyList.ItemsSource = current.GetValueOrDefault(id, []).Select(EditorCommand.Name).ToList();
            resetCommand.IsEnabled = Overrides().ContainsKey(id);
        }

        bool Matches(int f, string id) => f switch { 1 => HasKeys(id), 2 => Customized(id), 3 => !HasKeys(id), _ => true };
        bool fillingFilter = false;
        void FillFilter()
        {
            // The counts change with every edit; replacing the items drops the choice, so it's put back.
            int chosen = Math.Max(0, filter.SelectedIndex);
            fillingFilter = true;
            filter.ItemsSource = filterNames.Select((n, i) => i == 0 ? n : $"{n} ({EditorCommands.Defaults.Count(c => Matches(i, c.Id))})").ToList();
            filter.SelectedIndex = chosen;
            fillingFilter = false;
        }

        /// <param name="keepShown">After an edit: rows that no longer match stay until the filter or search changes.</param>
        void FillTree(bool keepShown = false)
        {
            string term = search.Text?.Trim() ?? "";
            int f = Math.Max(0, filter.SelectedIndex);
            TreeViewItem? reselect = null;
            var commands = EditorCommands.Defaults
                .Where(c => (keepShown && shown.Contains(c.Id))
                    || (Matches(f, c.Id) && (term.Length == 0 || $"{c.Group} {c.KeysoundGroup} {c.Description} {KeysOf(c.Id)}".Contains(term, StringComparison.OrdinalIgnoreCase))))
                .ToList();
            shown = [.. commands.Select(c => c.Id)];
            tree.ItemsSource = commands
                .GroupBy(c => c.Group)
                .OrderBy(g => Areas.Order(g.Key))
                .Select(g => new TreeViewItem
                {
                    Header = g.Key,
                    IsExpanded = true,
                    ItemsSource = g.Select(c =>
                    {
                        bool customized = Customized(c.Id);
                        var row = new Grid { ColumnDefinitions = new("12,218,*") };
                        var mark = new TextBlock { Text = customized ? "●" : "", FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
                        var label = new TextBlock { Text = c.Description, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
                        var keys = new TextBlock { Text = KeysOf(c.Id), Opacity = HasKeys(c.Id) ? 1 : 0.5 };
                        Grid.SetColumn(label, 1);
                        Grid.SetColumn(keys, 2);
                        row.Children.AddRange([mark, label, keys]);
                        ToolTip.SetTip(row, customized ? $"{c.Description}\nPreset: {PresetKeysOf(c.Id)}" : c.Description);
                        var item = new TreeViewItem { Header = row, Tag = c.Id };
                        if (c.Id == selectedId) reselect = item;
                        return item;
                    }).ToList(),
                }).ToList();
            if (reselect is not null) tree.SelectedItem = reselect;
            else if (selectedId is not null)
            {
                // The picked command is filtered out: let go of it, so the panel never edits a hidden command.
                selectedId = null;
                ShowCommand();
            }
        }

        /// <param name="keepShown">True after an edit to the keys, so the rows the user is working down stay put.</param>
        void Refresh(string? message = null, bool keepShown = false)
        {
            cubaseRows.IsVisible = edited.KeyPreset == KeyPreset.Cubase;
            edited = edited with { CubaseKeyFile = Blank(file.Text) };
            (current, var problem) = Keymap.For(edited);
            presetKeys = Keymap.For(edited, withChanges: false).Keys;
            status.Text = problem ?? message ?? (edited.KeyPreset == KeyPreset.Cubase
                ? "Commands with a Cubase match take its keys; the rest keep the editor's own keys where they're free."
                : "");
            FillFilter();
            FillTree(keepShown);
            ShowCommand();
        }

        void SetOverrides(Dictionary<string, List<string>> overrides) =>
            edited = edited with { KeyOverrides = new(edited.KeyOverrides) { [edited.KeyPreset.ToString()] = overrides } };

        tree.SelectionChanged += (_, _) =>
        {
            if (tree.SelectedItem is TreeViewItem { Tag: string id })
            {
                selectedId = id;
                ShowCommand();
            }
        };
        search.TextChanged += (_, _) => FillTree();
        filter.SelectionChanged += (_, _) => { if (!fillingFilter) FillTree(); };
        preset.SelectionChanged += (_, _) =>
        {
            edited = edited with { KeyPreset = presets[Math.Max(0, preset.SelectedIndex)].Preset };
            Refresh();
        };
        file.TextChanged += (_, _) => Refresh(); // CubaseKeyFile.Read re-parses only a changed file; the tree is still rebuilt
        reload.Click += (_, _) =>
        {
            CubaseKeyFile.Forget();
            Refresh("Reloaded.");
        };

        remove.Click += (_, _) =>
        {
            if (selectedId is not { } id || keyList.SelectedIndex < 0) return;
            var gone = current[id][keyList.SelectedIndex];
            SetOverrides(new(Overrides()) { [id] = [.. current[id].Where(g => g != gone).Select(Keymap.Format)] });
            Refresh(keepShown: true);
        };
        clear.Click += (_, _) =>
        {
            if (selectedId is not { } id) return;
            SetOverrides(new(Overrides()) { [id] = [] });
            Refresh(keepShown: true);
        };
        resetCommand.Click += (_, _) =>
        {
            if (selectedId is not { } id) return;
            var overrides = new Dictionary<string, List<string>>(Overrides());
            overrides.Remove(id);
            SetOverrides(overrides);
            Refresh(keepShown: true);
        };
        resetPreset.Click += async (_, _) =>
        {
            if (Overrides().Count == 0) return;
            if (!await Confirm((Window)TopLevel.GetTopLevel(body)!, "Reset preset",
                $"Undo all {Overrides().Count} of your key changes to the {PresetName()} preset?", "Reset"))
                return;
            SetOverrides([]);
            Refresh("Your changes to this preset are undone.");
        };

        var keyFileType = new FilePickerFileType("Key file") { Patterns = [$"*.{KeyFile.Extension}"] };
        export.Click += async (_, _) =>
        {
            var target = await TopLevel.GetTopLevel(body)!.StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "Export keys",
                SuggestedFileName = $"{PresetName()} keys.{KeyFile.Extension}",
                DefaultExtension = KeyFile.Extension,
                FileTypeChoices = [keyFileType],
            });
            if (target?.TryGetLocalPath() is not { } path) return;
            try
            {
                await File.WriteAllTextAsync(path, KeyFile.Write(edited.KeyPreset.ToString(), current));
                status.Text = $"Exported the {PresetName()} keys to {Path.GetFileName(path)}.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                status.Text = $"Couldn't export the keys: {ex.Message}";
            }
        };
        import.Click += async (_, _) =>
        {
            var owner = (Window)TopLevel.GetTopLevel(body)!;
            var files = await owner.StorageProvider.OpenFilePickerAsync(new() { Title = "Import keys", FileTypeFilter = [keyFileType] });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            KeyFile.ImportResult result;
            try
            {
                result = KeyFile.Read(await File.ReadAllTextAsync(path), presetKeys);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                status.Text = $"Couldn't import {Path.GetFileName(path)}: {ex.Message}";
                return;
            }
            if (!await Confirm(owner, "Import keys", $"Replace your key changes to the {PresetName()} preset with the keys in {Path.GetFileName(path)}?", "Import"))
                return;
            SetOverrides(result.Changes);
            Refresh(result.Summary());
        };

        // Keys are caught only while this box has focus, before the dialog sees them, so Enter, Esc or Ctrl+Tab can be assigned too.
        capture.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            // Tab and Shift+Tab still move focus, so the box can be left without the mouse.
            if (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None) return;
            e.Handled = true;
            if (selectedId is not { } id || e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
                return;
            pending = new KeyGesture(e.Key, e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt));
            capture.Text = EditorCommand.Name(pending);
            if (current.GetValueOrDefault(id, []).Contains(pending))
                (assignedTo.Text, assign.IsEnabled) = ("Already a key of this command.", false);
            else
            {
                assignedTo.Text = Keymap.Owner(current, pending, id) is { } owner
                    ? $"Assigned to: {EditorCommands.Get(owner).Group} → {EditorCommands.Get(owner).Description}"
                    : "Unassigned";
                assign.IsEnabled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        assign.Click += async (_, _) =>
        {
            if (selectedId is not { } id || pending is not { } gesture) return;
            if (Keymap.Owner(current, gesture, id) is { } owner
                && !await Confirm((Window)TopLevel.GetTopLevel(body)!, "Key in use",
                    $"Move {EditorCommand.Name(gesture)} from \"{EditorCommands.Get(owner).Description}\" to \"{EditorCommands.Get(id).Description}\"?", "Move"))
                return;
            SetOverrides(Keymap.AddKey(Overrides(), current, id, gesture));
            Refresh(keepShown: true);
        };

        Refresh();
        return () => edited with { CubaseKeyFile = Blank(file.Text) };
    }

    private static Task<T?> Show<T>(Window owner, string title, Func<StackPanel, Action<T>, (Action Ok, string OkText)> build, string cancelText = "Cancel", bool scroll = false, Action<Button>? configureOk = null)
    {
        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
            Background = Paint.Brush(UiTheme.Current.Color("title.bg")),
        };
        window.Classes.Add("editorDialog");
        var result = new TaskCompletionSource<T?>();
        void Close(T value)
        {
            result.TrySetResult(value);
            window.Close();
        }

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        var (ok, okText) = build(body, Close);
        var okButton = new Button { Content = okText, IsDefault = true, MinWidth = 80 };
        okButton.Classes.Add("primary");
        configureOk?.Invoke(okButton);
        var cancel = new Button { Content = cancelText, IsCancel = true, MinWidth = 80 };
        okButton.Click += (_, _) => ok();
        cancel.Click += (_, _) => window.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.AddRange([okButton, cancel]);
        if (scroll)
        {
            double available = (owner.Screens.ScreenFromWindow(owner)?.WorkingArea.Height ?? 900) / owner.RenderScaling;
            window.MaxHeight = Math.Max(240, available - 60);
            var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(16) };
            body.Margin = new Thickness(0, 0, 0, 12);
            layout.Children.Add(new ScrollViewer { Content = body, MaxHeight = Math.Max(120, available - 180), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
            Grid.SetRow(buttons, 1);
            layout.Children.Add(buttons);
            window.Content = layout;
        }
        else
        {
            body.Children.Add(buttons);
            window.Content = body;
        }
        window.Closed += (_, _) => result.TrySetResult(default);
        _ = window.ShowDialog(owner);
        return result.Task;
    }
}

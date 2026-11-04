using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal static partial class Dialogs
{
    private static TextBlock MergeText(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 540 };

    private static async Task SaveMergePreset(Window owner, ExportMergeSettings settings, TextBlock status)
    {
        try
        {
            var file = await owner.StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "Save merge preset", SuggestedFileName = "hitsound-merge.json", DefaultExtension = "json",
                FileTypeChoices = [new("Merge preset") { Patterns = ["*.json"] }], ShowOverwritePrompt = true,
            });
            if (file?.TryGetLocalPath() is { } path) { settings.Save(path); status.Text = "Preset saved."; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or NotSupportedException)
        { status.Text = $"Could not save preset: {ex.Message}"; }
    }

    public static Task<ExportMergeSettings?> MergePriorities(Window owner, IReadOnlyList<MergeCombination> combinations, Action<MixFile>? preview = null, ExportMergeSettings? initial = null) =>
        Show<ExportMergeSettings?>(owner, "Export merge priorities", (body, close) =>
        {
            var settings = initial?.Copy() ?? new ExportMergeSettings();
            body.Children.Add(MergeText(settings.PairSamples
                ? "Priority chooses the addition sampleset for paired output. Sound types stay separate; competing finishes fuse into finish. Loaded combination rules override this order. This order is separate from your editor lanes."
                : "The highest-priority participating addition chooses the output slot for every overlap. All input sounds are kept. Loaded combination rules override this order. Slider ticks and loops are exported separately. This order is separate from your editor lanes."));
            var list = new ListBox { ItemsSource = settings.Priorities.ToList(), SelectedIndex = 0, Height = 250, Width = 440 };
            var combinationPicker = new ComboBox
            {
                ItemsSource = combinations.Select(c => string.Join(" + ", c.Layers.Select(l => l.Lane.Name))).ToList(),
                Width = 520, SelectedIndex = combinations.Count > 0 ? 0 : -1,
            };
            var auditions = new StackPanel { Spacing = 4 };
            var destinationText = MergeText("");
            var previewProblem = MergeText("");
            previewProblem.Foreground = Brushes.OrangeRed;
            var up = new Button { Content = "Move up" };
            var down = new Button { Content = "Move down" };
            void Move(int step)
            {
                int from = list.SelectedIndex, to = from + step;
                if (from < 0 || to < 0 || to >= settings.Priorities.Count) return;
                (settings.Priorities[from], settings.Priorities[to]) = (settings.Priorities[to], settings.Priorities[from]);
                list.ItemsSource = settings.Priorities.ToList(); list.SelectedIndex = to;
                RefreshCombination();
            }
            var load = new Button { Content = "Load preset…" };
            var save = new Button { Content = "Save preset…" };
            var status = MergeText("No preset loaded. Decisions are reused in this export only unless you explicitly save and load a preset.");
            load.Click += async (_, _) =>
            {
                try
                {
                    var files = await ((Window)TopLevel.GetTopLevel(body)!).StorageProvider.OpenFilePickerAsync(new()
                    { Title = "Load merge preset", FileTypeFilter = [new("Merge preset") { Patterns = ["*.json"] }] });
                    if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                    var loaded = ExportMergeSettings.Load(path);
                    loaded.PairSamples = settings.PairSamples;
                    settings = loaded;
                    list.ItemsSource = settings.Priorities.ToList(); list.SelectedIndex = 0;
                    var keys = combinations.Select(c => c.Key).ToHashSet();
                    int matching = settings.Rules.Keys.Count(keys.Contains);
                    status.Text = $"Loaded {settings.Rules.Count} rules; {matching} match this export. Unmatched rules are ignored. New combinations use the priority order.";
                    RefreshCombination();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or NotSupportedException)
                { status.Text = $"Could not load preset: {ex.Message}"; }
            };
            save.Click += async (_, _) => await SaveMergePreset((Window)TopLevel.GetTopLevel(body)!, settings, status);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            actions.Children.AddRange([up, down, load, save]);
            body.Children.AddRange([list, actions, status]);
            void RefreshCombination()
            {
                auditions.Children.Clear();
                previewProblem.Text = "";
                if (combinationPicker.SelectedIndex is not (>= 0 and var i) || i >= combinations.Count) return;
                var combination = combinations[i];
                destinationText.Text = $"Output slot: {settings.Destination(combination)}. Preview uses this combination's first occurrence.";
                if (preview is null) return;
                void AddPreview(string label, MixFile mix)
                {
                    var button = new Button { Content = label, MaxWidth = 520 };
                    button.Click += (_, _) =>
                    {
                        try { preview(mix); previewProblem.Text = ""; }
                        catch (Exception ex) { previewProblem.Text = $"Preview failed: {ex.Message}"; }
                    };
                    auditions.Children.Add(button);
                }
                foreach (var layer in combination.Layers)
                    AddPreview($"▶ {layer.Lane.Name} ({layer.Volume}%)", new MixFile("preview.wav", MixPlanner.Normalize([layer], includeMissing: true)));
                AddPreview("▶ Preview merged result", new MixFile("preview.wav", MixPlanner.Normalize(combination.Layers, includeMissing: true)));
            }
            up.Click += (_, _) => Move(-1); down.Click += (_, _) => Move(1);
            combinationPicker.SelectionChanged += (_, _) => RefreshCombination();
            body.Children.AddRange([MergeText("Optional: audition combinations before exporting"), combinationPicker, destinationText, auditions, previewProblem]);
            RefreshCombination();
            return (() => close(settings), "Continue");
        }, scroll: true);

    public static Task<bool?> ConfirmMergeGenerate(Window owner, string summary, ExportMergeSettings settings) =>
        Show<bool?>(owner, "Generate hitsound diff", (body, close) =>
        {
            body.Children.Add(MergeText(summary));
            var status = MergeText($"{settings.Rules.Count} combination rules. Save a preset to reuse these choices in another export.");
            var save = new Button { Content = "Save merge preset…" };
            save.Click += async (_, _) => await SaveMergePreset((Window)TopLevel.GetTopLevel(body)!, settings, status);
            body.Children.AddRange([save, status]);
            return (() => close(true), "Generate");
        }, "Cancel export", scroll: true);
}

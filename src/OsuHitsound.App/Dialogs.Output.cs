using Avalonia.Controls;
using Avalonia.Layout;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal static partial class Dialogs
{
    public static Task<ExportMergeSettings?> ReviewOutput(Window owner, GeneratePlan plan, ExportMergeSettings settings, Action<MixFile> preview) =>
        Show<ExportMergeSettings?>(owner, "Review output", (body, close) =>
        {
            body.Children.Add(MergeText("Distinct sounds stay separate. Finish layers fuse into finish; hitnormal stays separate. This review changes no map or sample files."));
            var style = new ComboBox { ItemsSource = new[] { "Green lines", "Per-object settings (sliders use green lines)" }, SelectedIndex = (int)settings.Style, Width = 520 };
            body.Children.Add(style);
            var priorities = new Button { Content = "Sampleset priorities / presets…" };
            body.Children.Add(priorities);
            var files = new TextBox { Text = string.Join("\n", plan.Mixes.Select(m => $"{(m.SilenceFor is not null ? "Silence" : m.CopyBytes is not null ? "Pair" : "Fuse")}: {m.FileName}").Concat(plan.Reused.Select(m => $"Reuse: {m.FileName}"))), IsReadOnly = true, AcceptsReturn = true, Height = 110, Width = 520 };
            body.Children.Add(files);
            if (plan.Notes.Count > 0) body.Children.Add(MergeText(string.Join("\n", plan.Notes.Distinct())));
            var outputs = plan.Mixes.Concat(plan.Reused.Select(m => new MixFile(m.FileName, [(new Lane(LaneSource.BeatmapFile, m.FileName), 100)]))).ToList();
            var outputPicker = new ComboBox { ItemsSource = outputs.Select(m => m.FileName).ToList(), Width = 520, SelectedIndex = outputs.Count > 0 ? 0 : -1 };
            var previewOutput = new Button { Content = "▶ Preview selected output", IsEnabled = outputs.Count > 0 };
            var outputStatus = MergeText("Output previews play each prepared sample at full volume.");
            previewOutput.Click += (_, _) =>
            {
                if (outputPicker.SelectedIndex is not (>= 0 and var i) || i >= outputs.Count) return;
                try { preview(outputs[i]); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException) { outputStatus.Text = ex.Message; }
            };
            body.Children.AddRange([outputPicker, previewOutput, outputStatus]);
            if (plan.Conflicts.Count > 0) body.Children.Add(MergeText(string.Join("\n", plan.Conflicts.Select(c => c.Reason).Distinct())));
            var combinations = plan.MergeCombinations.DistinctBy(c => c.OccurrenceKey).ToList();
            var picker = new ComboBox { ItemsSource = combinations.Select(c => $"{c.Time:0.###} ms: {string.Join(" + ", c.Layers.Select(l => l.Lane.Name))}").ToList(), Width = 520, SelectedIndex = combinations.Count > 0 ? 0 : -1 };
            var bank = new ComboBox { ItemsSource = new[] { "Automatic", "Normal", "Soft", "Drum" }, SelectedIndex = 0, Width = 220 };
            var scope = new ComboBox { ItemsSource = new[] { "This timestamp", "Matching combinations in this map" }, SelectedIndex = 0, Width = 300 };
            var handling = new ComboBox { ItemsSource = new[] { "Automatic pairing / finish fusion", "Keep original files (may block export)" }, SelectedIndex = 0, Width = 520 };
            var apply = new Button { Content = "Apply choice" };
            var reset = new Button { Content = "Reset choice" };
            var audition = new Button { Content = "▶ Preview original combination" };
            var status = MergeText("Sampleset choices preserve sound types. Updated filenames are planned on the next review or save.");
            MergeCombination? Selected() => picker.SelectedIndex is >= 0 and var i && i < combinations.Count ? combinations[i] : null;
            void Change(bool automatic)
            {
                if (Selected() is not { } combination) return;
                var rules = scope.SelectedIndex == 0 ? settings.OccurrenceRules : settings.Rules;
                string key = scope.SelectedIndex == 0 ? combination.OccurrenceKey : combination.Key;
                var modes = scope.SelectedIndex == 0 ? settings.OccurrenceKeepOriginal : settings.KeepOriginalRules;
                if (automatic) modes.Remove(key); else modes[key] = handling.SelectedIndex == 1;
                if (automatic || bank.SelectedIndex <= 0) rules.Remove(key);
                else rules[key] = HitsoundRules.SampleName((SampleSet)bank.SelectedIndex, "hitfinish");
                status.Text = automatic || bank.SelectedIndex <= 0 ? "Automatic sampleset selected." : $"{bank.SelectedItem} selected for {scope.SelectedItem}.";
                if (!automatic && handling.SelectedIndex == 1) status.Text = "Original files requested. If they cannot play together, export will stop and explain why.";
                if (automatic) status.Text = "Choice reset. Matching-combination choices still apply.";
                RefreshSelected();
            }
            apply.Click += (_, _) => Change(false);
            reset.Click += (_, _) => Change(true);
            void RefreshSelected()
            {
                if (Selected() is not { } selected) return;
                bank.SelectedIndex = settings.RuleFor(selected) is { } slot ? (int)HitsoundRules.ParseSampleName(slot)!.Value.Set : 0;
                handling.SelectedIndex = settings.KeepOriginal(selected) ? 1 : 0;
            }
            picker.SelectionChanged += (_, _) => RefreshSelected();
            RefreshSelected();
            priorities.Click += async (_, _) =>
            {
                var edited = await MergePriorities((Window)TopLevel.GetTopLevel(body)!, plan.MergeCombinations, preview, settings);
                if (edited is not null) { settings = edited; style.SelectedIndex = (int)settings.Style; RefreshSelected(); }
            };
            audition.Click += (_, _) =>
            {
                if (Selected() is not { } combination) return;
                try { preview(new MixFile("preview.wav", MixPlanner.Normalize(combination.Layers, true))); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException) { status.Text = ex.Message; }
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.AddRange([apply, reset, audition]);
            body.Children.AddRange([picker, handling, MergeText("Addition sampleset"), bank, scope, buttons, status]);
            if (combinations.Count == 0) { picker.IsEnabled = handling.IsEnabled = bank.IsEnabled = scope.IsEnabled = buttons.IsEnabled = false; status.Text = "No pairing or finish fusion is needed for these objects."; }
            return (() => { settings.Style = (ExportStyle)Math.Max(0, style.SelectedIndex); settings.Validate(); close(settings); }, "Keep settings");
        }, scroll: true);
}

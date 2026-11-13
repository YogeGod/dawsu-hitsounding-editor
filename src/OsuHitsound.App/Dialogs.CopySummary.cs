using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal static partial class Dialogs
{
    /// <summary>
    /// The change summary of a planned Copy (issue #28): one row per target with its counts, ticked when it is clean.
    /// A target with conflicts starts unticked ("Write anyway"); one that can't be written can't be ticked. Each row
    /// expands to its changed lines. Returns the ticked targets, or null on Cancel.
    /// </summary>
    public static Task<IReadOnlyList<string>?> CopySummary(Window owner, CopyBatch batch) =>
        Show<IReadOnlyList<string>>(owner, "Copy hitsounds: what will change", (body, close) =>
        {
            var boxes = new List<(CheckBox Box, string Path)>();
            body.Children.Add(new TextBlock
            {
                Text = "Nothing has been written yet. Ticked difficulties are written together: if one fails, the others are put back as they were.",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 560, Opacity = 0.8,
            });

            foreach (var target in batch.Targets)
            {
                string name = HitsoundDiff.VersionOf(target.Path) ?? Path.GetFileNameWithoutExtension(target.Path);
                var row = new StackPanel { Spacing = 2, Margin = new Thickness(0, 6, 0, 0) };
                if (target.Error is not null)
                {
                    row.Children.Add(new CheckBox { Content = $"{name}: can't be written", IsChecked = false, IsEnabled = false });
                    row.Children.Add(Detail(target.Error, Brushes.OrangeRed));
                    body.Children.Add(row);
                    continue;
                }

                var changes = target.Changes!;
                var box = new CheckBox { Content = $"{name}: {changes.Counts()}", IsChecked = target.Clean };
                boxes.Add((box, target.Path));
                row.Children.Add(box);
                if (!changes.OnlyHitsounds)
                    row.Children.Add(Detail($"{changes.OtherLines} line(s) outside hitsounds will change; check them below.", Brushes.OrangeRed));
                if (target.Conflicts.Count > 0)
                {
                    row.Children.Add(Detail($"Write anyway: {target.Conflicts.Count} moment(s) keep their old sounds. First: {target.Conflicts[0].Reason}", Brushes.Orange));
                }

                if (target.Copy is { KeptFromSilent: > 0 } copy)
                    row.Children.Add(Detail($"{copy.KeptFromSilent} hit point(s) kept their own sound instead of a silent hitnormal."));
                if (target.Storage is { Mixes.Count: > 0 } storage)
                    row.Children.Add(Detail($"New samples: {string.Join(", ", storage.Mixes.Take(4).Select(m => m.FileName))}{(storage.Mixes.Count > 4 ? ", …" : "")}"));
                if (!changes.IsEmpty)
                    row.Children.Add(new Expander
                    {
                        Header = "Changed lines", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(22, 0, 0, 0),
                        Content = new SelectableTextBlock { Text = ChangedLines(changes), FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 11, TextWrapping = TextWrapping.NoWrap },
                    });
                body.Children.Add(row);
            }

            var problem = new TextBlock { Foreground = Brushes.OrangeRed };
            body.Children.Add(problem);
            return (() =>
            {
                var ticked = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Path).ToList();
                if (ticked.Count == 0) problem.Text = "Tick at least one difficulty to write.";
                else close(ticked);
            }, "Write ticked difficulties");
        }, scroll: true);

    private static TextBlock Detail(string text, IBrush? brush = null) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 540, Margin = new Thickness(22, 0, 0, 0), Opacity = brush is null ? 0.75 : 1,
        Foreground = brush,
    };

    /// <summary>Every changed line as "- before" / "+ after" under its section, at most 300.</summary>
    internal static string ChangedLines(WriteSummary changes)
    {
        var lines = new List<string>();
        foreach (var c in changes.Changes.Take(300))
        {
            if (c.Before is { } before) lines.Add($"[{c.Section}] - {before}");
            if (c.After is { } after) lines.Add($"[{c.Section}] + {after}");
        }

        if (changes.Changes.Count > 300) lines.Add($"… and {changes.Changes.Count - 300} more.");
        return string.Join('\n', lines);
    }
}

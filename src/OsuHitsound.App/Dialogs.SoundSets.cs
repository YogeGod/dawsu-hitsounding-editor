using Avalonia.Controls;
using Avalonia.Layout;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal sealed record SoundSetChoice(string Name, IReadOnlyList<(Guid Id, Lane Lane, int Volume)> Members);

internal static partial class Dialogs
{
    public static Task<SoundSetChoice?> EditSoundSet(Window owner, IReadOnlyList<Lane> lanes, SoundSet? original = null) =>
        Show<SoundSetChoice?>(owner, original is null ? "New sound set" : "Edit sound set", (body, close) =>
        {
            var name = new TextBox { Watermark = "Set name", Text = original?.Name ?? "", MaxLength = 128, Width = 480 };
            var rows = new StackPanel { Spacing = 6 };
            var entries = new List<(Guid Id, ComboBox Sample, TextBox Volume, StackPanel Row)>();
            var all = (original?.Members.Select(m => m.Lane) ?? []).Concat(lanes).Distinct().ToList();
            void Add(Guid id, Lane? lane, int volume)
            {
                var sample = new ComboBox { ItemsSource = all.Select(l => l.Name).ToList(), Width = 310, SelectedIndex = lane is null ? 0 : all.IndexOf(lane), MaxDropDownHeight = 260 };
                var level = new TextBox { Text = volume.ToString(), Width = 55 };
                var remove = new Button { Content = "−" };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { sample, level, remove } };
                entries.Add((id, sample, level, row)); rows.Children.Add(row);
                remove.Click += (_, _) => { entries.RemoveAll(e => e.Id == id); rows.Children.Remove(row); };
            }
            foreach (var m in original?.Members ?? []) Add(m.Id, m.Lane, m.Volume);
            if (entries.Count == 0) Add(Guid.NewGuid(), all.FirstOrDefault(), 100);
            var add = new Button { Content = "+ Sound" };
            add.Click += (_, _) => { if (entries.Count < 128) Add(Guid.NewGuid(), all.FirstOrDefault(), 100); };
            var error = MergeText("");
            body.Children.AddRange([name, MergeText("Sounds and relative volumes only. Placements follow this map's copy of the set."), rows, add, error]);
            return (() =>
            {
                if (string.IsNullOrWhiteSpace(name.Text) || entries.Count == 0 || entries.Any(e => e.Sample.SelectedIndex < 0 || !int.TryParse(e.Volume.Text, out int volume) || volume is < 0 or > 100))
                { error.Text = "Enter a name, at least one sound, and volumes from 0 to 100."; return; }
                close(new(name.Text.Trim(), entries.Select(e => (e.Id, all[e.Sample.SelectedIndex], int.Parse(e.Volume.Text!))).ToList()));
            }, "Save set");
        }, scroll: true);
}

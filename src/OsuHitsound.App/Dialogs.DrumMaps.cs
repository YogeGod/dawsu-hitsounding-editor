using System.Collections.ObjectModel;
using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal sealed record DrumMapSetupChoice(string Command, Guid? Id);
internal static partial class Dialogs
{
    public static Task<DrumMapSetupChoice?> DrumMapSetup(Window owner, DrumMapCollection maps) =>
        Show<DrumMapSetupChoice?>(owner, "Drum Map Setup", (body, close) =>
        {
            body.Children.Add(MergeText("Named instrument rows over your existing sample lanes. Editing notes works the same. Presets are shared by this mapset."));
            var list = new ListBox { ItemsSource = maps.Presets.Select(p => p.Name).ToList(), MinWidth = 500, Height = 160,
                SelectedIndex = Math.Max(0, maps.Presets.FindIndex(p => p.Id == maps.Active)) };
            Guid? Selected() => list.SelectedIndex is >= 0 and var i && i < maps.Presets.Count ? maps.Presets[i].Id : null;
            var detail = MergeText("");
            void Refresh() { detail.Text = Selected() is { } id ? string.Join("\n", maps.Presets.Single(p => p.Id == id).Rows.Take(8).Select(r => r.Name + " → " + (ProjectStore.LaneOf(r.LaneKey ?? "")?.Name ?? "Unassigned"))) : "New Map creates an editing preset, not a .osu difficulty."; }
            list.SelectionChanged += (_, _) => Refresh(); Refresh();
            Button Command(string label, string command, bool needsMap = true)
            {
                var button = new Button { Content = label, IsEnabled = !needsMap || maps.Presets.Count > 0 };
                button.Click += (_, _) => { if (!needsMap || Selected() is not null) close(new(command, Selected())); }; return button;
            }
            var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
            actions.Children.AddRange([Command("New Map…", "new", false), Command("New Copy…", "copy"), Command("Rename…", "rename"), Command("Remove…", "remove"),
                Command("Edit rows…", "edit"), Command("Save…", "save"), Command("Load…", "load", false)]);
            body.Children.AddRange([list, actions, detail]);
            return (() => { if (Selected() is { } id) close(new("use", id)); }, "Use map");
        }, scroll: true);

    public static Task<DrumMapPreset?> EditDrumMap(Window owner, DrumMapPreset preset, IReadOnlyList<(Lane Lane, Lane Slot)> lanes, IReadOnlyDictionary<Lane, string>? labels = null, Guid? focusEntry = null) =>
        Show<DrumMapPreset?>(owner, "Edit drum map", (body, close) =>
        {
            var name = new TextBox { Text = preset.Name, MaxLength = 128, Width = 500, HorizontalAlignment = HorizontalAlignment.Left };
            Avalonia.Automation.AutomationProperties.SetName(name, "Drum-map name");
            body.Children.AddRange([MergeText("Map name"), name, MergeText("Instrument names and sample assignments only. Removing a row keeps its notes. Two rows assigned to one sample mirror the same hits.")]);
            var available = lanes.ToList();
            var list = new StackPanel { Spacing = 6 };
            var entries = new List<(Guid Id, TextBox Name, ComboBox Sample, StackPanel Row)>();
            var options = new ObservableCollection<string>(new[] { "Unassigned" }.Concat(available.Select(l => labels?.GetValueOrDefault(l.Lane) ?? (l.Slot.Name == l.Lane.Name ? l.Lane.Name : l.Slot.Name + " · " + l.Lane.Name))));
            void Add(DrumMapEntry entry)
            {
                var label = new TextBox { Text = entry.Name, Width = 150, MaxLength = 128 };
                var sample = new ComboBox { Tag = entry.Id, ItemsSource = options, Width = 250, MaxDropDownHeight = 240,
                    SelectedIndex = available.FindIndex(l => ProjectStore.KeyOf(l.Lane) == entry.LaneKey) + 1 };
                Avalonia.Automation.AutomationProperties.SetName(label, "Instrument name");
                Avalonia.Automation.AutomationProperties.SetName(sample, "Assigned sample for " + entry.Name);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Tag = entry.Id };
                if (focusEntry == entry.Id) sample.AttachedToVisualTree += (_, _) => { sample.Focus(); row.BringIntoView(); };
                var up = new Button { Content = "Up" }; var down = new Button { Content = "Down" }; var remove = new Button { Content = "Remove" };
                void Move(int delta)
                {
                    int from = entries.FindIndex(e => e.Id == entry.Id), to = from + delta;
                    if (to < 0 || to >= entries.Count) return;
                    var item = entries[from]; entries.RemoveAt(from); entries.Insert(to, item);
                    list.Children.Remove(row); list.Children.Insert(to, row);
                }
                up.Click += (_, _) => Move(-1); down.Click += (_, _) => Move(1);
                remove.Click += (_, _) => { entries.RemoveAll(e => e.Id == entry.Id); list.Children.Remove(row); };
                row.Children.AddRange([label, sample, up, down, remove]);
                entries.Add((entry.Id, label, sample, row)); list.Children.Add(row);
            }
            foreach (var entry in preset.Rows) Add(entry);
            var add = new Button { Content = "Add instrument" };
            add.Click += (_, _) => { if (entries.Count < 4096) Add(new(Guid.NewGuid(), "Instrument " + (entries.Count + 1), null, null)); };
            var error = MergeText("");
            var families = new[] { "normal", "soft", "drum" }.SelectMany(set => new[] { "hitnormal", "hitwhistle", "hitfinish", "hitclap" }.Select(sound => new Lane(LaneSource.Skin, set + "-" + sound))).ToList();
            var family = new ComboBox { ItemsSource = families.Select(l => l.Name).ToList(), SelectedIndex = 0, Width = 250 };
            Avalonia.Automation.AutomationProperties.SetName(family, "New layer sample family");
            var extra = new Button { Content = "Add numbered layer" };
            extra.Click += (_, _) =>
            {
                try
                {
                    if (available.Count >= 4096) { error.Text = "The sample list has reached its limit."; return; }
                    var slot = SampleSlots.Next(available.Select(l => l.Slot), families[family.SelectedIndex]);
                    var pending = slot;
                    int index = int.MaxValue;
                    while (available.Any(l => l.Lane == pending))
                        pending = new Lane(LaneSource.BeatmapFile, families[family.SelectedIndex].Name + index-- + ".wav");
                    available.Add((pending, slot)); options.Add(slot.Name + " (default sound)");
                    error.Text = $"{slot.Name} is now available to every instrument. Choose it in Assigned sample; audio is only added when you save.";
                }
                catch (Exception ex) when (ex is IOException or ArgumentException) { error.Text = ex.Message; }
            };
            var layerTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            layerTools.Children.AddRange([family, extra]);
            var headings = new Grid { ColumnDefinitions = new ColumnDefinitions("150,250,*"), ColumnSpacing = 4 };
            headings.Children.Add(MergeText("Instrument"));
            var assignedHeading = MergeText("Assigned sample"); Grid.SetColumn(assignedHeading, 1); headings.Children.Add(assignedHeading);
            body.Children.AddRange([MergeText("Add extra layers for any instrument. Missing numbered samples use the bundled family default."), layerTools,
                headings, list, add, error]);
            return (() =>
            {
                var rows = entries.Select(e => e.Sample.SelectedIndex > 0 && e.Sample.SelectedIndex <= available.Count
                    ? new DrumMapEntry(e.Id, e.Name.Text?.Trim() ?? "", ProjectStore.KeyOf(available[e.Sample.SelectedIndex - 1].Lane), ProjectStore.KeyOf(available[e.Sample.SelectedIndex - 1].Slot))
                    : new DrumMapEntry(e.Id, e.Name.Text?.Trim() ?? "", null, preset.Rows.FirstOrDefault(r => r.Id == e.Id)?.SlotKey)).ToImmutableList();
                var edited = preset with { Name = name.Text?.Trim() ?? "", Rows = rows, Audio = [] };
                try { edited.Validate(); close(edited); } catch (FormatException ex) { error.Text = ex.Message; }
            }, "Save map");
        }, scroll: true);

    public static Task<bool?> DrumMapSamples(Window owner, bool hasAudio) =>
        Show<bool?>(owner, "Load drum map", (body, close) =>
        {
            var bundled = new RadioButton { Content = "Use preset samples", GroupName = "samples", IsChecked = hasAudio, IsEnabled = hasAudio };
            var own = new RadioButton { Content = "Use my own samples", GroupName = "samples", IsChecked = !hasAudio };
            body.Children.AddRange([MergeText("Use the samples saved with this preset? Existing hits stay on their current samples."), bundled, own,
                MergeText(hasAudio ? "Preset samples are copied under safe new names. Your own samples keep matching assignments; missing numbered samples use the family default and are reviewed next." : "This preset has no bundled audio. Match it to your own samples and review missing assignments next.")]);
            return (() => close(bundled.IsChecked == true), "Continue");
        });

    public static Task<(bool Move, DrumMapOverlap? Overlap)?> DrumMapReassignment(Window owner, int hits,
        IReadOnlyList<EditHit> overlaps, bool canMove) =>
        Show<(bool Move, DrumMapOverlap? Overlap)?>(owner, "Reassign drum-map samples", (body, close) =>
        {
            var mode = new ComboBox { ItemsSource = new[] { "Change assignments only", "Move existing hits in this difficulty" }, SelectedIndex = 0, Width = 480 };
            var handling = new ComboBox { ItemsSource = new[] { "Keep both as separate layers", "Keep existing destination hits", "Replace destination hits" }, SelectedIndex = -1, PlaceholderText = "Choose overlap handling", Width = 480 };
            var error = MergeText("");
            body.Children.AddRange([MergeText($"{hits} existing hit(s) use reassigned samples. Assignment-only keeps them unchanged. Moving affects all aliases of each source sample, only in this difficulty, and supports Undo."), mode,
                MergeText(overlaps.Count == 0 ? "No destination overlaps." : $"{overlaps.Count} overlap(s):\n" + string.Join("\n", overlaps.Take(12).Select(h => $"{h.Time:0.###} ms · {h.Lane.Name}"))), handling, error]);
            handling.IsEnabled = false;
            mode.SelectionChanged += (_, _) => handling.IsEnabled = mode.SelectedIndex == 1 && overlaps.Count > 0 && canMove;
            return (() =>
            {
                if (mode.SelectedIndex == 1 && !canMove) { error.Text = "Aliases of one source were assigned to different destinations. Move one source at a time, or change assignments only."; return; }
                if (mode.SelectedIndex == 1 && overlaps.Count > 0 && handling.SelectedIndex < 0) { error.Text = "Choose how to handle the overlaps, or change assignments only."; return; }
                close((mode.SelectedIndex == 1, handling.SelectedIndex >= 0 ? (DrumMapOverlap)handling.SelectedIndex : null));
            }, "Apply");
        }, scroll: true);
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

internal static partial class Dialogs
{
    /// <summary>
    /// The MIDI import preview: which parts to import and where the file's beat 1 lands (bar and beat of the reference).
    /// Returns the chosen parts and the origin tick, or null on Cancel.
    /// </summary>
    public static Task<(List<MidiImportPart> Parts, long Origin)?> ImportMidi(Window owner, string fileName, MidiFile file,
        IReadOnlyList<MidiImportPart> parts, TempoMap tempo, double mapEnd) =>
        Show<(List<MidiImportPart>, long)?>(owner, "Import MIDI", (body, close) =>
        {
            string tempoText = file.FirstTempoBpm is { } bpm ? $"{bpm:0.##} BPM in the file; " : "";
            body.Children.Add(new TextBlock
            {
                Text = $"{fileName}: {parts.Count} track{(parts.Count == 1 ? "" : "s")} (format {file.Format}, {file.TicksPerBeat} ticks per beat). " +
                       $"{tempoText}notes keep their beats and follow this beatmap's red lines. Each track gets the bundled piano.",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 560,
            });

            var checks = new List<(CheckBox Box, MidiImportPart Part)>();
            var list = new StackPanel { Spacing = 4 };
            foreach (var part in parts)
            {
                var box = new CheckBox { IsChecked = part.Notes.Count > 0, IsEnabled = part.Notes.Count > 0 };
                var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                label.Children.Add(new TextBlock { Text = part.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                label.Children.Add(new TextBlock { Text = $"{part.Notes.Count} notes · channel {part.Channel + 1}", Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center });
                if (part.Percussion)
                    label.Children.Add(new TextBlock
                    {
                        Text = "Percussion: plays on the piano until a drum instrument is assigned",
                        Foreground = Paint.Brush("#ff8a5c"),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                box.Content = label;
                checks.Add((box, part));
                list.Children.Add(box);
            }

            body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 280, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

            var bar = new NumericUpDown { Minimum = -999, Maximum = 99999, Value = 1, Increment = 1, FormatString = "0", Width = 120 };
            var beat = new NumericUpDown { Minimum = 1, Maximum = 32, Value = 1, Increment = 1, FormatString = "0", Width = 100 };
            var place = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            place.Children.Add(new TextBlock { Text = "The file's first beat lands on bar", VerticalAlignment = VerticalAlignment.Center });
            place.Children.Add(bar);
            place.Children.Add(new TextBlock { Text = "beat", VerticalAlignment = VerticalAlignment.Center });
            place.Children.Add(beat);
            body.Children.Add(place);

            var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 560, Opacity = 0.85 };
            body.Children.Add(summary);

            long Origin() => (long)Math.Round(tempo.TickOfBarBeat((int)(bar.Value ?? 1), (int)(beat.Value ?? 1)));
            List<MidiImportPart> Chosen() => [.. checks.Where(c => c.Box.IsChecked == true).Select(c => c.Part)];

            void Update()
            {
                var chosen = Chosen();
                var notes = chosen.SelectMany(p => p.Notes).ToList();
                if (notes.Count == 0)
                {
                    summary.Text = "Choose at least one track with notes.";
                    return;
                }

                long origin = Origin();
                double first = tempo.TimeAt(MidiImport.ToProjectTick(notes.Min(n => n.Tick), file.TicksPerBeat, origin));
                double last = tempo.TimeAt(MidiImport.ToProjectTick(notes.Max(n => n.Tick + n.Length), file.TicksPerBeat, origin));
                string At(double t) => tempo.BarBeatAt(t) is var (b, bt) ? $"bar {b}.{bt} ({TimeSpan.FromMilliseconds(Math.Max(0, t)):m\\:ss\\.fff})" : "";
                summary.Text = $"{notes.Count} notes from {At(first)} to {At(last)}." +
                    (last > mapEnd + 1000 ? $" That runs past the beatmap's last object at {At(mapEnd)}." : "") +
                    (first < 0 ? " Some notes start before the song does." : "");
            }

            foreach (var (box, _) in checks) box.IsCheckedChanged += (_, _) => Update();
            bar.ValueChanged += (_, _) => Update();
            beat.ValueChanged += (_, _) => Update();
            Update();
            return (() => { if (Chosen() is { Count: > 0 } chosen) close((chosen, Origin())); }, "Import");
        }, scroll: true);
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>What the inspector's cards say, kept apart from the controls so it can be tested on its own.</summary>
internal static class InspectorText
{
    /// <summary>The Selection card's summary: nothing, one hit (time, bar.beat, lane, volume) or several (count, span, volumes).</summary>
    public static string Selection(EditSession? session, ArrangeModel? model, IReadOnlyCollection<EditHit> selected, Func<double, string> time)
    {
        if (session is null || selected.Count == 0) return "Nothing selected";
        string Bar(double t) => model is { } m && m.BarBeatTick(t) is var (bar, beat, tick) ? $"{bar}.{beat}.{tick}" : "";
        var volumes = selected.Where(h => h.Anchor is not null).Select(session.VolumeOf).ToList();
        if (selected.Count == 1)
        {
            var hit = selected.First();
            string volume = hit.Anchor is null ? "no object yet" : $"{session.VolumeOf(hit)} %";
            return $"{time(hit.Time)}  ({Bar(hit.Time)})\n{hit.Lane.Name}\nVolume: {volume}";
        }

        double start = selected.Min(h => h.Time), end = selected.Max(h => h.Time);
        string range = volumes.Count == 0 ? "no objects yet" : volumes.Min() == volumes.Max() ? $"{volumes[0]} %" : $"{volumes.Min()}–{volumes.Max()} %";
        return $"{selected.Count} hits\n{time(start)} → {time(end)}\nVolume: {range}";
    }

    /// <summary>
    /// The Recent edits list, newest first: each entry is the position just after a step (0 = as opened), its text and
    /// whether it is the current state. Undone steps (redo still possible) say so. At most <paramref name="max"/> steps.
    /// </summary>
    public static IReadOnlyList<(int Position, string Text, bool Current)> RecentEdits(EditSession session, int max = 50)
    {
        var steps = session.Steps;
        var list = new List<(int, string, bool)>();
        for (int k = steps.Count; k >= Math.Max(1, steps.Count - max + 1); k--)
            list.Add((k, k > session.Position ? $"{steps[k - 1]} (undone)" : steps[k - 1], k == session.Position));
        if (steps.Count <= max) list.Add((0, "As opened", session.Position == 0));
        return list;
    }
}

/// <summary>
/// The inspector on the left (docs/spec-wave2.md, part 6): cards that fold (click the title) and reorder (drag the
/// grip). The window wires the cards' actions to the editor and calls <see cref="Refresh"/> after edits.
/// </summary>
internal sealed class InspectorPanel : Border
{
    public const string SelectionCard = "selection", BookmarksCard = "bookmarks", RecentCard = "recent";

    private readonly StackPanel cards = new() { Spacing = 6, Margin = new Thickness(6) };
    private readonly Dictionary<string, (Border Card, Control Body)> byId = [];
    private readonly TextBlock selectionText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox shiftBox = new() { Width = 70, Watermark = "ms" };
    private readonly TextBox volumeBox = new() { Width = 70, Watermark = "1–100" };
    private readonly ListBox bookmarkList = new() { MaxHeight = 220 };
    private readonly ListBox recentList = new() { MaxHeight = 320 };
    private List<double> bookmarkTimes = [];
    private List<int> recentPositions = [];
    private bool filling;

    // What the lists were last filled from: they are rebuilt only when it changes (not on every drag step).
    private (EditSession? Session, int Position, int Steps, object? Bookmarks, ArrangeModel? Model) listsFrom;

    /// <summary>Card id dragged by its grip, while reordering.</summary>
    private string? dragging;

    public event Action<double>? SeekRequested;
    public event Action<double>? ShiftTimeRequested;
    public event Action<int>? VolumeRequested;
    public event Action? AddBookmarkRequested;
    public event Action<double>? RemoveBookmarkRequested;
    public event Action<int>? GoToStepRequested;

    /// <summary>A card was folded, unfolded or moved: the window keeps the layout in the preferences.</summary>
    public event Action? LayoutChanged;

    public InspectorPanel()
    {
        Child = new ScrollViewer { Content = cards, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        var shift = new Button { Content = "Shift" };
        shift.Click += (_, _) => { if (OsuNumber.TryParseDouble(shiftBox.Text ?? "", out double ms) && double.IsFinite(ms) && ms != 0) ShiftTimeRequested?.Invoke(ms); };
        var setVolume = new Button { Content = "Set" };
        setVolume.Click += (_, _) => { if (OsuNumber.TryParseInt(volumeBox.Text ?? "", out int v) && v is >= 1 and <= 100) VolumeRequested?.Invoke(v); };
        AddCard(SelectionCard, "Selection", Stack(selectionText, Row("Shift time", shiftBox, shift), Row("Volume", volumeBox, setVolume)));

        var add = new Button { Content = "+ At playhead" };
        add.Click += (_, _) => AddBookmarkRequested?.Invoke();
        var remove = new Button { Content = "Remove" };
        remove.Click += (_, _) => { if (bookmarkList.SelectedIndex is >= 0 and var i && i < bookmarkTimes.Count) RemoveBookmarkRequested?.Invoke(bookmarkTimes[i]); };
        bookmarkList.SelectionChanged += (_, _) => { if (!filling && bookmarkList.SelectedIndex is >= 0 and var i && i < bookmarkTimes.Count) SeekRequested?.Invoke(bookmarkTimes[i]); };
        AddCard(BookmarksCard, "Bookmarks", Stack(bookmarkList, Row(null, add, remove)));

        recentList.SelectionChanged += (_, _) => { if (!filling && recentList.SelectedIndex is >= 0 and var i && i < recentPositions.Count) GoToStepRequested?.Invoke(recentPositions[i]); };
        AddCard(RecentCard, "Recent edits", Stack(recentList, new TextBlock { Text = "Click an edit to go back (or forward) to just after it.", TextWrapping = TextWrapping.Wrap, Opacity = 0.6, FontSize = 11 }));
    }

    /// <summary>Card ids, top to bottom.</summary>
    public IReadOnlyList<string> Order => [.. cards.Children.Select(c => (string)c.Tag!)];

    /// <summary>Ids of the folded cards.</summary>
    public IReadOnlyList<string> Folded => [.. byId.Where(c => !c.Value.Body.IsVisible).Select(c => c.Key)];

    /// <summary>Puts the cards in this order (unknown ids are ignored, missing ones keep their place at the end) and folds these.</summary>
    public void SetLayout(IEnumerable<string> order, IEnumerable<string> folded)
    {
        var wanted = order.Where(byId.ContainsKey).Distinct().ToList();
        var rest = Order.Where(id => !wanted.Contains(id)).ToList();
        cards.Children.Clear();
        foreach (var id in wanted.Concat(rest)) cards.Children.Add(byId[id].Card);
        var fold = folded.ToHashSet();
        foreach (var (id, (_, body)) in byId) body.IsVisible = !fold.Contains(id);
    }

    /// <summary>Fills the cards from the editor's state.</summary>
    public void Refresh(EditSession? session, ArrangeModel? model, IReadOnlyCollection<EditHit> selected, Func<double, string> time)
    {
        filling = true;
        try
        {
            selectionText.Text = InspectorText.Selection(session, model, selected, time);
            var from = (session, session?.Position ?? 0, session?.StepCount ?? 0, (object?)session?.State.Bookmarks, model);
            if (from == listsFrom) return;
            listsFrom = from;
            bookmarkTimes = session is null ? [] : [.. session.BookmarkTimes];
            bookmarkList.ItemsSource = bookmarkTimes.Select(t => model is { } m && m.BarBeatTick(t) is var (bar, beat, _) ? $"{bar}.{beat}   {time(t)}" : time(t)).ToList();
            var recent = session is null ? [] : InspectorText.RecentEdits(session);
            recentPositions = [.. recent.Select(r => r.Position)];
            recentList.ItemsSource = recent.Select(r => r.Current ? $"▶ {r.Text}" : $"   {r.Text}").ToList();
            recentList.SelectedIndex = -1;
        }
        finally
        {
            filling = false;
        }
    }

    private void AddCard(string id, string title, Control body)
    {
        var grip = new TextBlock { Text = "⠿", Cursor = new Cursor(StandardCursorType.SizeNorthSouth), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
        ToolTip.SetTip(grip, "Drag to move this card");
        var header = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, FontWeight = FontWeight.SemiBold };
        header.Click += (_, _) => { body.IsVisible = !body.IsVisible; LayoutChanged?.Invoke(); };
        var top = new DockPanel();
        DockPanel.SetDock(grip, Dock.Right);
        top.Children.AddRange([grip, header]);
        var card = new Border
        {
            Tag = id,
            Background = new SolidColorBrush(Color.Parse("#232830")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3a414c")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            Child = new StackPanel { Spacing = 4, Children = { top, body } },
        };

        grip.PointerPressed += (_, e) => { dragging = id; e.Pointer.Capture(grip); e.Handled = true; };
        grip.PointerMoved += (_, e) => { if (dragging == id) MoveDragged(e.GetPosition(cards).Y); };
        grip.PointerReleased += (_, e) =>
        {
            if (dragging != id) return;
            dragging = null;
            e.Pointer.Capture(null);
            LayoutChanged?.Invoke();
        };

        byId[id] = (card, body);
        cards.Children.Add(card);
    }

    /// <summary>While dragging a grip: puts the dragged card where the pointer is among the others.</summary>
    private void MoveDragged(double y)
    {
        if (dragging is null) return;
        var card = byId[dragging].Card;
        int from = cards.Children.IndexOf(card);
        int to = cards.Children.Count(c => c != card && c.Bounds.Top + c.Bounds.Height / 2 < y);
        if (to == from) return;
        cards.Children.RemoveAt(from);
        cards.Children.Insert(to, card);
    }

    private static StackPanel Stack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.AddRange(children);
        return panel;
    }

    private static StackPanel Row(string? label, params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (label is not null) row.Children.Add(new TextBlock { Text = label, Width = 64, VerticalAlignment = VerticalAlignment.Center });
        row.Children.AddRange(children);
        return row;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using OsuHitsound.Format;
using OsuHitsound.Lanes;
using IconPath = Avalonia.Controls.Shapes.Path;

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
    public const string SelectionCard = "selection", BookmarksCard = "bookmarks", RecentCard = "recent", ChannelCard = "channel";

    private readonly TextBlock channelName = new() { Text = "No active lane", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock laneHelp = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly Button useSelectedLane = new() { Content = "Use selected hit's lane", IsVisible = false };
    private Lane? selectedLane;
    private readonly TextBlock sampleFile = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.9 };
    private readonly ComboBox channelSample = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 280, Foreground = Brushes.Gainsboro };
    private readonly Button replaceSample = new() { Content = "Browse samples…" };
    private readonly Button previewSample = new() { Content = "Preview" };
    public FileBrowserPanel Browser { get; } = new();
    private readonly Border browserSlot = new();
    public SoundSetPanel SoundSets { get; } = new();
    private Lane? channelLane;
    private List<Lane> channelChoices = [];
    private (ArrangeModel? Model, int Count, object? Order) channelFrom;
    public event Action<Lane, Lane>? ChannelSampleRequested;
    public event Action<Lane>? BrowseChannelSampleRequested;
    public event Action<Lane>? PreviewChannelRequested;
    public event Action<Lane>? ActivateLaneRequested;

    private readonly StackPanel cards = new() { Spacing = 4, Margin = new Thickness(12, 4) };
    private readonly Dictionary<string, (Border Card, Control Body, IconPath Chevron)> byId = [];
    private readonly TextBox bookmarkName = new() { Watermark = "Optional bookmark label", MaxLength = 120 };
    private readonly ListBox bookmarkList = new() { MaxHeight = 220 };
    private readonly ListBox recentList = new() { MaxHeight = 320 };
    private List<double> bookmarkTimes = [];
    private List<int> recentPositions = [];
    private bool filling;
    private EditSession? currentSession;

    // What the lists were last filled from: they are rebuilt only when it changes (not on every drag step).
    private (EditSession? Session, int Position, int Steps, object? Bookmarks, ArrangeModel? Model) listsFrom;

    /// <summary>Card id dragged by its grip, while reordering.</summary>
    private string? dragging;

    public event Action<double>? SeekRequested;
    public event Action<double, string>? RenameBookmarkRequested;
    public event Action? AddBookmarkRequested;
    public event Action<double>? RemoveBookmarkRequested;
    public event Action<int>? GoToStepRequested;

    /// <summary>The ‹ in the pane title: the window hides the inspector.</summary>
    public event Action? CloseRequested;

    /// <summary>A card was folded, unfolded or moved: the window keeps the layout in the preferences.</summary>
    public event Action? LayoutChanged;

    public InspectorPanel()
    {
        var close = new Button { Content = "‹", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 0) };
        ToolTip.SetTip(close, "Hide the inspector");
        close.Click += (_, _) => CloseRequested?.Invoke();
        var title = new DockPanel { Background = new SolidColorBrush(Color.Parse("#303037")), Height = 32 };
        DockPanel.SetDock(close, Dock.Right);
        title.Children.AddRange([close, new TextBlock { Text = "Inspector", FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#bebecb")), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) }]);
        var scroller = new ScrollViewer { Content = cards, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var pane = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        pane.Children.AddRange([title, scroller]);
        Child = pane;

        channelSample.SelectionChanged += (_, _) =>
        {
            if (!filling && channelLane is { } lane && channelSample.SelectedIndex is >= 0 and var i && i < channelChoices.Count && channelChoices[i] != lane)
                ChannelSampleRequested?.Invoke(lane, channelChoices[i]);
        };
        replaceSample.Click += (_, _) => { if (channelLane is { } lane) BrowseChannelSampleRequested?.Invoke(lane); };
        previewSample.Click += (_, _) => { if (channelLane is { } lane) PreviewChannelRequested?.Invoke(lane); };
        useSelectedLane.Click += (_, _) => { if (selectedLane is { } lane) ActivateLaneRequested?.Invoke(lane); };
        Avalonia.Automation.AutomationProperties.SetName(channelSample, "Sample for active lane");
        AddCard(ChannelCard, "Active lane", Stack(channelName, laneHelp, useSelectedLane, sampleFile, channelSample, Row(null, replaceSample, previewSample)));
        AddCard("browser", "File browser", browserSlot);
        byId["browser"].Card.IsVisible = false;

        AddCard("soundSets", "Sound sets", SoundSets);

        var add = new Button { Content = "+ At playhead" };
        add.Click += (_, _) => AddBookmarkRequested?.Invoke();
        var remove = new Button { Content = "Remove" };
        remove.Click += (_, _) => { if (bookmarkList.SelectedIndex is >= 0 and var i && i < bookmarkTimes.Count) RemoveBookmarkRequested?.Invoke(bookmarkTimes[i]); };
        bookmarkList.SelectionChanged += (_, _) => { if (!filling && bookmarkList.SelectedIndex is >= 0 and var i && i < bookmarkTimes.Count) SeekRequested?.Invoke(bookmarkTimes[i]); };
        var rename = new Button { Content = "Label", IsVisible = false };
        bookmarkName.IsVisible = false;
        void Rename() { if (bookmarkList.SelectedIndex is >= 0 and var i && i < bookmarkTimes.Count) RenameBookmarkRequested?.Invoke(bookmarkTimes[i], bookmarkName.Text ?? ""); }
        rename.Click += (_, _) => Rename();
        bookmarkName.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Rename(); e.Handled = true; } };
        bookmarkList.SelectionChanged += (_, _) => { bool has = bookmarkList.SelectedIndex is >= 0 and var i && i < bookmarkTimes.Count; bookmarkName.IsVisible = rename.IsVisible = has; if (has) bookmarkName.Text = currentSession?.BookmarkLabel(bookmarkTimes[bookmarkList.SelectedIndex]); };
        AddCard(BookmarksCard, "Bookmarks", Stack(bookmarkList, Row(null, add, remove), bookmarkName, rename));

        recentList.SelectionChanged += (_, _) => { if (!filling && recentList.SelectedIndex is >= 0 and var i && i < recentPositions.Count) GoToStepRequested?.Invoke(recentPositions[i]); };
        AddCard(RecentCard, "History", Stack(recentList, new TextBlock { Text = "Click an edit to go back (or forward) to just after it.", TextWrapping = TextWrapping.Wrap, Opacity = 0.85, FontSize = 11 }));
    }

    public void SetOptionalPanels(bool history, bool soundSets)
    {
        Show(RecentCard, history);
        Show("soundSets", soundSets);
        void Show(string id, bool open)
        {
            var (card, body, chevron) = byId[id];
            if (open && !card.IsVisible) { body.IsVisible = true; chevron.RenderTransform = new RotateTransform(90); }
            card.IsVisible = open;
        }
    }

    public void SetClassicBrowser(bool classic, bool open)
    {
        browserSlot.Child = classic ? Browser : null;
        byId["browser"].Card.IsVisible = classic && open;
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
        foreach (var (id, (_, body, chevron)) in byId)
        {
            body.IsVisible = !fold.Contains(id);
            chevron.RenderTransform = new RotateTransform(body.IsVisible ? 90 : 0);
        }
    }

    /// <summary>Unfolds these cards and moves them to the top, in this order.</summary>
    public void Reveal(params string[] ids)
    {
        SetLayout(ids.Concat(Order), Folded.Except(ids));
        LayoutChanged?.Invoke();
    }

    /// <summary>Fills the cards from the editor's state.</summary>
    public void Refresh(EditSession? session, ArrangeModel? model, IReadOnlyCollection<EditHit> selected, Func<double, string> time, bool canEdit = true)
    {
        filling = true;
        try
        {
            var active = model?.ActiveLane;
            Browser.SetAssignmentTarget(active is null ? null : model!.NameOf(active), canEdit && active is not null && model!.CanReplaceSample(active),
                canEdit ? null : "Open a beatmap to assign samples. You can still preview files.");
            var selectionLanes = selected.Select(h => h.Lane).Distinct().Take(2).ToList();
            selectedLane = selectionLanes.Count == 1 ? selectionLanes[0] : null;
            useSelectedLane.IsVisible = selectedLane is not null && selectedLane != active;
            useSelectedLane.Content = selected.Count == 1 ? "Use selected hit's lane" : "Use selected hits' lane";
            var source = (model, model?.LaneRows.Count ?? 0, (object?)session?.State.LaneOrder);
            bool changed = source != channelFrom;
            if (changed)
            {
                channelFrom = source;
                channelChoices = model is null ? [] : model.LaneRows.Select(r => r.Lane).Where(ArrangeModel.CanChooseChannelSample).Distinct().ToList();
                channelSample.ItemsSource = channelChoices.Select(l => l.Source == LaneSource.Skin ? $"Default: {l.Name}" : l.Name).ToList();
            }
            if (active != channelLane || changed)
            {
                channelLane = active;
                channelSample.SelectedIndex = active is null ? -1 : channelChoices.IndexOf(active);
            }
            channelName.Text = active is null ? "No active lane" : model!.NameOf(active);
            laneHelp.Text = !canEdit ? "Click a lane to preview its sample. Open a beatmap to change it."
                : active is null ? "Click a lane name to choose its sample. Selecting hits is separate."
                : model!.CanReplaceSample(active) ? "Changing this sample updates every hit in this lane."
                : "This lane is read-only; you can preview its sample.";
            sampleFile.Text = active is null ? "" : active.Source == LaneSource.Skin ? "Sample: default hitsound" : $"Sample: {active.Name}";
            if (active is not null && model!.SlotOf(active) != active)
                sampleFile.Text = $"Lane: {model.SlotOf(active).Name}\nSample: {active.Name}";
            previewSample.IsEnabled = active is not null;
            channelSample.IsEnabled = canEdit && active is not null && model!.CanReplaceSample(active);
            replaceSample.IsEnabled = active is not null && (!canEdit || model!.CanReplaceSample(active));
            currentSession = session;
            var from = (session, session?.Position ?? 0, session?.StepCount ?? 0, (object?)session?.State.Bookmarks, model);
            if (from == listsFrom) return;
            listsFrom = from;
            var oldBookmarkTimes = bookmarkTimes;
            bookmarkTimes = session is null ? [] : [.. session.BookmarkTimes];
            double? selectedTime = bookmarkList.SelectedIndex is >= 0 and var index && index < oldBookmarkTimes.Count ? oldBookmarkTimes[index] : null;
            bookmarkList.ItemsSource = bookmarkTimes.Select(t => (session?.BookmarkLabel(t) is { Length: > 0 } label ? label + " · " : "") + (model is { } m && m.BarBeatTick(t) is var (bar, beat, _) ? $"{bar}.{beat}   {time(t)}" : time(t))).ToList();
            bookmarkList.SelectedIndex = selectedTime is { } selectedBookmark ? bookmarkTimes.IndexOf(selectedBookmark) : -1;
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
        var grip = new IconPath
        {
            Data = Geometry.Parse("M 0,0 h 2 v 2 h -2 Z M 4,0 h 2 v 2 h -2 Z M 0,4 h 2 v 2 h -2 Z M 4,4 h 2 v 2 h -2 Z M 0,8 h 2 v 2 h -2 Z M 4,8 h 2 v 2 h -2 Z"),
            Fill = Brushes.SlateGray, Width = 8, Height = 12, Stretch = Stretch.Uniform,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0),
        };
        ToolTip.SetTip(grip, "Drag to move this card");
        var chevron = new IconPath { Data = Geometry.Parse("M 0,0 L 4,4 L 0,8"), Stroke = Brushes.LightSlateGray, StrokeThickness = 1.5, Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, RenderTransform = new RotateTransform(90) };
        var header = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, Padding = new Thickness(6, 4), Foreground = Brushes.Gainsboro, FontWeight = FontWeight.SemiBold, HorizontalContentAlignment = HorizontalAlignment.Left };
        header.Classes.Add("flat");
        ToolTip.SetTip(header, $"Expand or collapse {title}");
        header.Click += (_, _) => { body.IsVisible = !body.IsVisible; chevron.RenderTransform = new RotateTransform(body.IsVisible ? 90 : 0); LayoutChanged?.Invoke(); };
        var top = new DockPanel();
        DockPanel.SetDock(grip, Dock.Right);
        DockPanel.SetDock(chevron, Dock.Left);
        top.Children.AddRange([grip, chevron, header]);
        var card = new Border
        {
            Tag = id,
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.Parse("#3a414c")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 4, 0, 12),
            Child = new StackPanel { Spacing = 6, Children = { top, body } },
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

        byId[id] = (card, body, chevron);
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

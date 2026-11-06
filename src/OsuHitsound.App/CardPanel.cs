using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using IconPath = Avalonia.Controls.Shapes.Path;

namespace OsuHitsound.App;

/// <summary>
/// A side pane of foldable, reorderable cards under a title with a ‹ hide button: Hitsound mode's inspector and Keysound
/// mode's track panel share it, so both look and behave the same. Click a card's title to fold it; drag its grip to move
/// it. <see cref="Order"/> and <see cref="Folded"/> are what the window keeps in the preferences.
/// </summary>
internal class CardPanel : Border
{
    private readonly StackPanel cards = new() { Spacing = 4, Margin = new Thickness(12, 4) };
    protected readonly Dictionary<string, (Border Card, Control Body, IconPath Chevron)> byId = [];

    /// <summary>Card id dragged by its grip, while reordering.</summary>
    private string? dragging;

    /// <summary>The ‹ in the pane title: the window hides the pane.</summary>
    public event Action? CloseRequested;

    /// <summary>A card was folded, unfolded or moved: the window keeps the layout in the preferences.</summary>
    public event Action? LayoutChanged;

    public CardPanel(string title)
    {
        var close = new Button { Content = "‹", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 0) };
        ToolTip.SetTip(close, $"Hide the {title.ToLowerInvariant()}");
        close.Click += (_, _) => CloseRequested?.Invoke();
        var bar = new DockPanel { Height = 32 };
        bar.Bind(Panel.BackgroundProperty, bar.GetResourceObservable("PaneTitle"));
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.AddRange([close, new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) }]);
        var scroller = new ScrollViewer { Content = cards, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var pane = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        pane.Children.AddRange([bar, scroller]);
        Child = pane;
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

    /// <summary>Adds a card at the bottom: a title that folds it, a grip that moves it, and its body.</summary>
    public void AddCard(string id, string title, Control body)
    {
        var grip = new IconPath
        {
            Data = Geometry.Parse("M 0,0 h 2 v 2 h -2 Z M 4,0 h 2 v 2 h -2 Z M 0,4 h 2 v 2 h -2 Z M 4,4 h 2 v 2 h -2 Z M 0,8 h 2 v 2 h -2 Z M 4,8 h 2 v 2 h -2 Z"),
            Fill = Brushes.SlateGray, Width = 8, Height = 12, Stretch = Stretch.Uniform,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0),
        };
        ToolTip.SetTip(grip, "Drag to move this card");
        var chevron = new IconPath { Data = Geometry.Parse("M 0,0 L 4,4 L 0,8"), Stroke = Brushes.LightSlateGray, StrokeThickness = 1.5, Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, RenderTransform = new RotateTransform(90) };
        var header = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, Padding = new Thickness(6, 4), FontWeight = FontWeight.SemiBold, HorizontalContentAlignment = HorizontalAlignment.Left };
        header.Classes.Add("flat");
        header.Bind(Button.ForegroundProperty, header.GetResourceObservable("ButtonText"));
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
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 4, 0, 12),
            Child = new StackPanel { Spacing = 6, Children = { top, body } },
        };
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("ChromeBorder"));

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

    protected static StackPanel Stack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.AddRange(children);
        return panel;
    }

    protected static StackPanel Row(string? label, params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (label is not null) row.Children.Add(new TextBlock { Text = label, Width = 64, VerticalAlignment = VerticalAlignment.Center });
        row.Children.AddRange(children);
        return row;
    }
}

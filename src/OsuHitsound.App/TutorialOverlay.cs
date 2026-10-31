using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace OsuHitsound.App;

/// <summary>One stop of the tutorial: what to say, and where on the window to point (null: no spotlight, centred).</summary>
internal sealed record TutorialStep(string Title, string Text, Func<Rect?> Target);

/// <summary>
/// The step-by-step tutorial: dims the window except a spotlight around one control, points at it with an arrow and
/// explains it in a bubble with Back / Next / Skip (←/→/Enter and Esc too). Laid over the whole window; while it runs it
/// takes the clicks, so the editor underneath can't be changed by accident.
/// </summary>
internal sealed class TutorialOverlay : Panel
{
    private readonly SpotlightLayer layer = new();
    private readonly Border bubble;
    private readonly TextBlock title = new() { FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock text = new() { FontSize = 13, Foreground = new SolidColorBrush(Color.Parse("#dedee7")), TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
    private readonly TextBlock counter = new() { FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#9a9aa9")), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button back = new() { Content = "Back" }, next = new() { Content = "Next" }, skip = new() { Content = "Skip tour" };
    private IReadOnlyList<TutorialStep> steps = [];
    private int index;

    public const double BubbleWidth = 340;

    /// <summary>The tour ended: finished, skipped or closed.</summary>
    public event Action? Finished;

    public TutorialOverlay()
    {
        IsVisible = false;
        Focusable = true;
        Background = Brushes.Transparent; // takes every click while shown
        next.Classes.Add("primary");
        back.Click += (_, _) => Go(index - 1);
        next.Click += (_, _) => Go(index + 1);
        skip.Click += (_, _) => Close();
        var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(counter, Dock.Left);
        DockPanel.SetDock(next, Dock.Right);
        DockPanel.SetDock(back, Dock.Right);
        DockPanel.SetDock(skip, Dock.Right);
        next.Margin = new Thickness(6, 0, 0, 0);
        back.Margin = new Thickness(6, 0, 0, 0);
        buttons.Children.AddRange([counter, next, back, skip]);
        bubble = new Border
        {
            Width = BubbleWidth,
            Background = new SolidColorBrush(Color.Parse("#2c2c33")),
            BorderBrush = new SolidColorBrush(Color.Parse("#0a84ff")),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14),
            BoxShadow = BoxShadows.Parse("0 6 24 0 #a0000000"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel { Spacing = 6, Children = { title, text, buttons } },
        };
        Children.AddRange([layer, bubble]);
        bubble.SizeChanged += (_, _) => Place();
        SizeChanged += (_, _) => Place();
    }

    public bool IsRunning => IsVisible;

    public void Start(IReadOnlyList<TutorialStep> tour)
    {
        if (tour.Count == 0) return;
        steps = tour;
        IsVisible = true;
        Go(0);
        Focus();
    }

    /// <summary>Goes straight to a step (0-based), for the snapshot tool.</summary>
    public void ShowStep(int step) => Go(Math.Min(step, steps.Count - 1));

    public void Close()
    {
        if (!IsVisible) return;
        IsVisible = false;
        Finished?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right or Key.Enter or Key.Space: Go(index + 1); break;
            case Key.Left or Key.Back: Go(index - 1); break;
            case Key.Escape: Close(); break;
        }

        e.Handled = true; // every key: none may reach the editor's shortcuts under the tour
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus(); // a click on the dimmed window does nothing but keeps the keys working
        e.Handled = true;
    }

    private void Go(int to)
    {
        if (to >= steps.Count)
        {
            Close();
            return;
        }

        index = Math.Max(0, to);
        var step = steps[index];
        title.Text = step.Title;
        text.Text = step.Text;
        counter.Text = $"{index + 1} / {steps.Count}";
        back.IsEnabled = index > 0;
        next.Content = index == steps.Count - 1 ? "Finish" : "Next";
        Place();
    }

    /// <summary>Spotlights the step's target and puts the bubble beside it (below, above, right or left, whichever fits).</summary>
    private void Place()
    {
        if (!IsVisible || steps.Count == 0) return;
        var size = Bounds.Size;
        var target = steps[index].Target() is { Width: > 0, Height: > 0 } t ? t.Inflate(6).Intersect(new Rect(size)) : (Rect?)null;
        double w = BubbleWidth, h = bubble.DesiredSize.Height > 0 ? bubble.DesiredSize.Height : 160, gap = 34;
        Point at;
        if (target is not { } r) at = new Point((size.Width - w) / 2, (size.Height - h) / 2);
        else if (r.Bottom + gap + h <= size.Height - 8) at = new Point(r.Center.X - w / 2, r.Bottom + gap);
        else if (r.Top - gap - h >= 8) at = new Point(r.Center.X - w / 2, r.Top - gap - h);
        else if (r.Right + gap + w <= size.Width - 8) at = new Point(r.Right + gap, r.Center.Y - h / 2);
        else at = new Point(r.Left - gap - w, r.Center.Y - h / 2);
        at = new Point(Math.Clamp(at.X, 8, Math.Max(8, size.Width - w - 8)), Math.Clamp(at.Y, 8, Math.Max(8, size.Height - h - 8)));
        bubble.Margin = new Thickness(at.X, at.Y, 0, 0);
        layer.Show(target, new Rect(at, new Size(w, h)));
    }

    /// <summary>The dimmed window with a clear spotlight, its outline, and an arrow from the bubble to it.</summary>
    private sealed class SpotlightLayer : Control
    {
        private static readonly IBrush Dim = new SolidColorBrush(Color.Parse("#000000"), 0.62);
        private static readonly IPen Outline = new Pen(new SolidColorBrush(Color.Parse("#0a84ff")), 2.5);
        private static readonly IPen ArrowPen = new Pen(new SolidColorBrush(Color.Parse("#ffd23f")), 3) { LineCap = PenLineCap.Round };
        private static readonly IBrush ArrowFill = new SolidColorBrush(Color.Parse("#ffd23f"));
        private Rect? spot;
        private Rect bubble;

        public SpotlightLayer() => IsHitTestVisible = false;

        public void Show(Rect? target, Rect bubbleRect)
        {
            (spot, bubble) = (target, bubbleRect);
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            var all = new Rect(Bounds.Size);
            if (spot is not { } s)
            {
                ctx.FillRectangle(Dim, all);
                return;
            }

            // Dim everything around the spotlight (four bands), then outline it.
            ctx.FillRectangle(Dim, new Rect(0, 0, all.Width, s.Top));
            ctx.FillRectangle(Dim, new Rect(0, s.Bottom, all.Width, Math.Max(0, all.Height - s.Bottom)));
            ctx.FillRectangle(Dim, new Rect(0, s.Top, s.Left, s.Height));
            ctx.FillRectangle(Dim, new Rect(s.Right, s.Top, Math.Max(0, all.Width - s.Right), s.Height));
            ctx.DrawRectangle(null, Outline, s, 6, 6);

            // The arrow: from the bubble's nearest edge to the spotlight's nearest edge, with a head at the spotlight.
            var from = Nearest(bubble, s.Center);
            var to = Nearest(s, from);
            var d = to - from;
            double len = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len < 12) return;
            var u = new Point(d.X / len, d.Y / len);
            var tip = to - u * 3;
            var baseCentre = tip - u * 14;
            var normal = new Point(-u.Y, u.X);
            ctx.DrawLine(ArrowPen, from, baseCentre);
            var head = new StreamGeometry();
            using (var g = head.Open())
            {
                g.BeginFigure(tip, true);
                g.LineTo(baseCentre + normal * 8);
                g.LineTo(baseCentre - normal * 8);
                g.EndFigure(true);
            }

            ctx.DrawGeometry(ArrowFill, null, head);
        }

        // The point of a rectangle's border nearest to p.
        private static Point Nearest(Rect r, Point p) => new(Math.Clamp(p.X, r.Left, r.Right), Math.Clamp(p.Y, r.Top, r.Bottom));
    }
}

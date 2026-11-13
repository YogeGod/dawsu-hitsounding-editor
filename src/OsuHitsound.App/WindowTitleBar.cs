using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace OsuHitsound.App;

/// <summary>
/// Minimise, maximise and close for a window whose title bar is merged into its menu bar (the main window, UI refresh
/// 2026-11-13). The bar itself handles dragging (see <see cref="Attach"/>).
/// </summary>
public sealed class WindowButtons : StackPanel
{
    private readonly AppIcon maximiseIcon = new() { Glyph = "Square", Size = 13 };
    private readonly Button maximise;
    private Window? window;

    public WindowButtons()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Stretch;
        var minimise = Make("Minimise", new AppIcon { Glyph = "Minus", Size = 15 }, () => window!.WindowState = WindowState.Minimized);
        maximise = Make("Maximise", maximiseIcon, ToggleMaximised);
        var close = Make("Close", new AppIcon { Glyph = "X", Size = 15 }, () => window!.Close());
        close.Classes.Add("close");
        Children.AddRange([minimise, maximise, close]);
        this.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("HeaderText"));
    }

    private static Button Make(string name, Control icon, Action click)
    {
        // Fill the bar's height, whatever the old window bar's height token says.
        var button = new Button { Content = icon, Focusable = false, Height = double.NaN, VerticalAlignment = VerticalAlignment.Stretch };
        button.Classes.Add("windowButton");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Makes a bar move the window when dragged and maximise it when double-clicked (its buttons and menus keep their clicks).</summary>
    public void Attach(Control bar) => bar.PointerPressed += (_, e) =>
    {
        if (window is null || !StartsWindowDrag(window, e) || !e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) ToggleMaximised();
        else window.BeginMoveDrag(e);
        e.Handled = true;
    };

    /// <summary>
    /// Whether a press that reached the bar should move (or maximise) the window. Presses inside an open menu's pop-up
    /// also bubble up to the bar, since the pop-up belongs to a menu in it. Dragging the window then would swallow
    /// the item's click, so an item would need two clicks. Only presses on the window itself count: not ones in a
    /// pop-up window (Windows) or in a pop-up drawn inside the window (overlay pop-ups).
    /// </summary>
    internal static bool StartsWindowDrag(Window window, PointerPressedEventArgs e) =>
        !e.Handled && e.Source is Visual source && ReferenceEquals(TopLevel.GetTopLevel(source), window)
        && !source.GetSelfAndVisualAncestors().Any(v => v is Avalonia.Controls.Primitives.OverlayPopupHost);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;
        window.PropertyChanged += OnWindowPropertyChanged;
        ShowState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (window is not null) window.PropertyChanged -= OnWindowPropertyChanged;
        window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) ShowState();
    }

    private void ShowState()
    {
        bool maximised = window?.WindowState == WindowState.Maximized;
        maximiseIcon.Glyph = maximised ? "Copy" : "Square";
        ToolTip.SetTip(maximise, maximised ? "Restore" : "Maximise");
    }

    private void ToggleMaximised() =>
        window!.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}

/// <summary>
/// The window's own title bar (the design's light strip): the window title on the left, minimise, maximise and close
/// on the right. Dragging it moves the window, double-clicking maximises; Windows still resizes from the edges.
/// The window opts in with ExtendClientAreaToDecorationsHint and NoChrome, and keeps its content inside the screen
/// when maximised (see MainWindow.OnPropertyChanged).
/// </summary>
public sealed class WindowTitleBar : Border
{
    private readonly TextBlock title = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly AppIcon maximiseIcon = new() { Glyph = "Square", Size = 13 };
    private readonly Button maximise;
    private Window? window;
    private IDisposable? titleBinding;

    public WindowTitleBar()
    {
        Classes.Add("windowBar");
        var minimise = Button("Minimise", new AppIcon { Glyph = "Minus", Size = 15 }, () => window!.WindowState = WindowState.Minimized);
        maximise = Button("Maximise", maximiseIcon, ToggleMaximised);
        var close = Button("Close", new AppIcon { Glyph = "X", Size = 15 }, () => window!.Close());
        close.Classes.Add("close");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { minimise, maximise, close } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(buttons, 1);
        grid.Children.AddRange([title, buttons]);
        Child = grid;

        this.Bind(BackgroundProperty, this.GetResourceObservable("WindowBg"));
        this.Bind(HeightProperty, this.GetResourceObservable("WindowBarHeight"));
        title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("WindowText"));
        title.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("WindowFontSize"));
        buttons.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("WindowText"));
    }

    private static Button Button(string name, Control icon, Action click)
    {
        var button = new Button { Content = icon, Focusable = false };
        button.Classes.Add("windowButton");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;
        titleBinding = title.Bind(TextBlock.TextProperty, window.GetObservable(Window.TitleProperty));
        window.PropertyChanged += OnWindowPropertyChanged;
        ShowState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (window is not null) window.PropertyChanged -= OnWindowPropertyChanged;
        titleBinding?.Dispose();
        titleBinding = null;
        window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) ShowState();
    }

    private void ShowState()
    {
        bool maximised = window?.WindowState == WindowState.Maximized;
        maximiseIcon.Glyph = maximised ? "Copy" : "Square";
        ToolTip.SetTip(maximise, maximised ? "Restore" : "Maximise");
    }

    private void ToggleMaximised() =>
        window!.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (window is null || e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) ToggleMaximised();
        else window.BeginMoveDrag(e);
        e.Handled = true;
    }
}

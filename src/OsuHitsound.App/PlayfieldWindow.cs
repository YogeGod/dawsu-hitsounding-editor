using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace OsuHitsound.App;

/// <summary>
/// The map preview's window: a <see cref="PlayfieldView"/> that floats over the editor (or sits on another screen) and
/// can be resized freely, under a bar that picks the skin. Keys pressed here go to the editor, so Space still plays and
/// the arrows still seek.
/// </summary>
internal sealed class PlayfieldWindow : Window
{
    private readonly ComboBox skinBox;
    private bool filling;

    public PlayfieldWindow(Action<KeyEventArgs> editorKey)
    {
        Title = "Map preview";
        Width = 480;
        Height = 420;
        MinWidth = 300;
        MinHeight = 240;
        ShowInTaskbar = false;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        skinBox = new ComboBox { MinWidth = 180, MaxDropDownHeight = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(skinBox, "The skin the objects are drawn with: osu!lazer's own, or one from osu!'s Skins folder");
        Avalonia.Automation.AutomationProperties.SetName(skinBox, "Skin");
        skinBox.SelectionChanged += (_, _) => { if (!filling && skinBox.SelectedItem is SkinChoice choice) SkinChosen?.Invoke(choice); };
        var folderButton = new Button { Content = "Skins folder…", Margin = new Thickness(6, 0, 0, 0) };
        ToolTip.SetTip(folderButton, "Choose osu!'s Skins folder (the one inside your osu! folder)");
        folderButton.Click += (_, _) => SkinsFolderRequested?.Invoke();

        var label = new TextBlock { Text = "Skin", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var bar = new DockPanel { Margin = new Thickness(8, 6) };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(folderButton, Dock.Right);
        bar.Children.Add(label);
        bar.Children.Add(folderButton);
        bar.Children.Add(skinBox);

        var layout = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        layout.Children.Add(bar);
        layout.Children.Add(View);
        Content = layout;
        // Capturing: a key pressed while the skin list has focus is still the editor's, unless the list is open.
        AddHandler(KeyDownEvent, (_, e) => { if (!skinBox.IsDropDownOpen) editorKey(e); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public PlayfieldView View { get; } = new();

    /// <summary>The user picked a skin in the list.</summary>
    public event Action<SkinChoice>? SkinChosen;

    /// <summary>The user asked to choose osu!'s Skins folder.</summary>
    public event Action? SkinsFolderRequested;

    /// <summary>Fills the skin list and selects one (the first when the wanted one is gone), without raising <see cref="SkinChosen"/>.</summary>
    public SkinChoice ShowSkins(IReadOnlyList<SkinChoice> skins, string? selectedId)
    {
        filling = true;
        skinBox.ItemsSource = skins;
        var selected = skins.FirstOrDefault(s => s.Id == selectedId) ?? skins[0];
        skinBox.SelectedItem = selected;
        filling = false;
        return selected;
    }

    /// <summary>True while the window is closing because the editor is: the preview then opens again next time.</summary>
    public bool ClosedWithEditor { get; private set; }

    /// <summary>Where the window was when it last closed or was asked: its position and size.</summary>
    public (PixelPoint Position, Size Size) Placement => (Position, ClientSize);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ClosedWithEditor = e.CloseReason is WindowCloseReason.OwnerWindowClosing or WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
        base.OnClosing(e);
    }
}

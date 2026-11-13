using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace OsuHitsound.App;

/// <summary>
/// The map preview's window: a <see cref="PlayfieldView"/> that floats over the editor (or sits on another screen) and
/// can be resized freely. Keys pressed here go to the editor, so Space still plays and the arrows still seek.
/// </summary>
internal sealed class PlayfieldWindow : Window
{
    public PlayfieldWindow(Action<KeyEventArgs> editorKey)
    {
        Title = "Map preview";
        Width = 480;
        Height = 384;
        MinWidth = 240;
        MinHeight = 200;
        ShowInTaskbar = false;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Content = View;
        KeyDown += (_, e) => editorKey(e);
    }

    public PlayfieldView View { get; } = new();

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

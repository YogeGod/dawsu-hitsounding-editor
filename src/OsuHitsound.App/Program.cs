using Avalonia;

namespace OsuHitsound.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        UiTheme.Current = UiTheme.Load(UiTheme.DefaultPath());
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

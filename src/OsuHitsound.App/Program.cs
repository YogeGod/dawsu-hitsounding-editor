using Avalonia;

namespace OsuHitsound.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // The installer's hooks (install, update, uninstall) run here and exit; a normal start goes on.
        Velopack.VelopackApp.Build().Run();
        UiTheme.Current = UiTheme.Load(UiTheme.DefaultPath());
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

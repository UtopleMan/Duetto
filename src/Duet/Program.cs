using Avalonia;

namespace Duet;

internal static class Program
{
    public static AppOptions Options { get; private set; } = new();

    [STAThread]
    public static int Main(string[] args)
    {
        Options = AppOptions.Parse(args);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

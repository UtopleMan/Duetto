using Avalonia;
using Avalonia.Headless;

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

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>();
        // Smoke mode renders headlessly so it works on locked screens and CI.
        return Options.Smoke
            ? builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            : builder.UsePlatformDetect().LogToTrace();
    }
}

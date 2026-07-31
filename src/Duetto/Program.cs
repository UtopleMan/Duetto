using Avalonia;
using Avalonia.Headless;

namespace Duetto;

internal static class Program
{
    public static AppOptions Options { get; private set; } = new();

    [STAThread]
    public static int Main(string[] args)
    {
        Options = AppOptions.Parse(args);
        // Best-effort: install the `duetto` shell launcher on PATH so the app can be started
        // from a terminal. Skipped for headless smoke/screenshot/CI runs; never blocks startup.
        if (!Options.Headless)
            _ = Task.Run(CliInstall.EnsureBestEffort);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>();
        // Smoke/screenshot modes render headlessly so they work on locked screens and CI.
        return Options.Headless
            ? builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            : builder.UsePlatformDetect().LogToTrace();
    }
}

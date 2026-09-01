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
        if (!Options.Headless)
            _ = Task.Run(CliInstall.EnsureBestEffort);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>();
        return Options.Headless
            ? builder.UseSkia().UseHarfBuzz().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            : builder.UsePlatformDetect().LogToTrace();
    }
}

using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Duetto.Tests.Ui.TestAppBuilder))]

namespace Duetto.Tests.Ui;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Duetto.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

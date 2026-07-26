using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Duet.Tests.Ui.TestAppBuilder))]

namespace Duet.Tests.Ui;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Duet.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

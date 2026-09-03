using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Duetto.Core.Remote;
using Duetto.Core.State;
using Duetto.Views;

namespace Duetto;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    private void ApplyTheme()
    {
        var setting = Program.Options.Theme
            ?? (Program.Options.Headless
                ? AppTheme.System
                : new ThemeSettingStore(AppPaths.SettingsJsonPath).Load());

        var os = PlatformSettings?.GetColorValues().ThemeVariant ?? PlatformThemeVariant.Light;
        var (variant, paletteUri) = ThemeResolver.Resolve(setting, os);

        RequestedThemeVariant = variant;
        Resources.MergedDictionaries.Add(new ResourceInclude((Uri?)null) { Source = paletteUri });
    }

    private void OnAboutClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var about = new AboutWindow();
            if (desktop.MainWindow is { } owner)
                about.ShowDialog(owner);
            else
                about.Show();
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ApplyTheme();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            if (Program.Options.Screenshot is { } path)
            {
                window.Opened += (_, _) => DispatcherTimer.RunOnce(() =>
                {
                    var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
                    frame?.Save(path, PngBitmapEncoderOptions.Default);
                    desktop.Shutdown(frame is null ? 1 : 0);
                }, TimeSpan.FromMilliseconds(600));
            }
            else if (Program.Options.Smoke)
            {
                window.Opened += (_, _) =>
                    DispatcherTimer.RunOnce(() => desktop.Shutdown(0), TimeSpan.FromMilliseconds(400));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Threading;
using Duetto.Core.Remote;
using Duetto.Core.State;
using Duetto.Views;

namespace Duetto;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    // Restart-to-apply: resolve the saved theme (System follows the OS), set the Fluent variant,
    // and append the matching palette dictionary so it overrides the light default merged in
    // App.axaml. Runs before any window is created, so views bind the chosen palette at parse
    // time. A forced --theme (screenshot runs) wins over settings.json.
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
                    frame?.Save(path);
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

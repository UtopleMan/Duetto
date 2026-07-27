using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Duetto.Views;

namespace Duetto;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

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

using Duetto.Core.State;

namespace Duetto;

public enum ChromeKind
{
    Win,
    Mac,
    Gnome,
}

public sealed class AppOptions
{
    public ChromeKind Chrome { get; init; } = DefaultChrome();
    public bool Smoke { get; init; }

    public string? Screenshot { get; init; }

    // Forces a theme, overriding settings.json — used by screenshot runs to render a given variant.
    public AppTheme? Theme { get; init; }

    // Null when absent or the path is missing / not a directory — the caller falls back to home.
    public string? Folder { get; init; }

    public bool Headless => Smoke || Screenshot is not null;

    public static AppOptions Parse(string[] args)
    {
        var chrome = DefaultChrome();
        var smoke = false;
        string? screenshot = null;
        AppTheme? theme = null;
        string? folder = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--chrome" when i + 1 < args.Length:
                    chrome = args[++i].ToLowerInvariant() switch
                    {
                        "win" => ChromeKind.Win,
                        "mac" => ChromeKind.Mac,
                        "gnome" => ChromeKind.Gnome,
                        var other => throw new ArgumentException($"Unknown chrome '{other}' (expected win|mac|gnome)"),
                    };
                    break;
                case "--smoke":
                    smoke = true;
                    break;
                case "--screenshot" when i + 1 < args.Length:
                    screenshot = args[++i];
                    break;
                case "--theme" when i + 1 < args.Length:
                    theme = args[++i].ToLowerInvariant() switch
                    {
                        "system" => AppTheme.System,
                        "light" => AppTheme.Light,
                        "dark" => AppTheme.Dark,
                        var other => throw new ArgumentException($"Unknown theme '{other}' (expected system|light|dark)"),
                    };
                    break;
                // Flag values are consumed above via ++i, so this only ever sees genuine positionals.
                default:
                    if (folder is null && !args[i].StartsWith("--", StringComparison.Ordinal))
                        folder = ResolveFolder(args[i]);
                    break;
            }
        }

        return new AppOptions
        {
            Chrome = chrome,
            Smoke = smoke,
            Screenshot = screenshot,
            Theme = theme,
            Folder = folder,
        };
    }

    private static string? ResolveFolder(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    private static ChromeKind DefaultChrome() =>
        OperatingSystem.IsWindows() ? ChromeKind.Win
        : OperatingSystem.IsMacOS() ? ChromeKind.Mac
        : ChromeKind.Gnome;
}

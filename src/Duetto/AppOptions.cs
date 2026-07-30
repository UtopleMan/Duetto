namespace Duetto;

public enum ChromeKind
{
    Win,
    Mac,
    Gnome,
}

/// <summary>Process-wide options parsed from the command line before Avalonia starts.</summary>
public sealed class AppOptions
{
    public ChromeKind Chrome { get; init; } = DefaultChrome();
    public bool Smoke { get; init; }

    /// <summary>Render one frame headlessly, save it as PNG here, exit.</summary>
    public string? Screenshot { get; init; }

    /// <summary>
    /// Absolute path of the folder the active (left) pane should open, from the first
    /// positional command-line argument. Null when absent or the path is missing / not a
    /// directory — the caller falls back to home.
    /// </summary>
    public string? Folder { get; init; }

    public bool Headless => Smoke || Screenshot is not null;

    public static AppOptions Parse(string[] args)
    {
        var chrome = DefaultChrome();
        var smoke = false;
        string? screenshot = null;
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
                // First positional (non-flag) argument: the folder to open. Flag values are
                // consumed above via ++i, so this only ever sees genuine positionals.
                default:
                    if (folder is null && !args[i].StartsWith("--", StringComparison.Ordinal))
                        folder = ResolveFolder(args[i]);
                    break;
            }
        }

        return new AppOptions { Chrome = chrome, Smoke = smoke, Screenshot = screenshot, Folder = folder };
    }

    /// <summary>
    /// Resolves a command-line path to an absolute directory. Relative paths resolve against
    /// the process working directory. Returns null when the path is not an existing directory
    /// or is syntactically invalid.
    /// </summary>
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

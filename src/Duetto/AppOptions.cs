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

    public bool Headless => Smoke || Screenshot is not null;

    public static AppOptions Parse(string[] args)
    {
        var chrome = DefaultChrome();
        var smoke = false;
        string? screenshot = null;
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
            }
        }

        return new AppOptions { Chrome = chrome, Smoke = smoke, Screenshot = screenshot };
    }

    private static ChromeKind DefaultChrome() =>
        OperatingSystem.IsWindows() ? ChromeKind.Win
        : OperatingSystem.IsMacOS() ? ChromeKind.Mac
        : ChromeKind.Gnome;
}

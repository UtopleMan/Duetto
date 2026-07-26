namespace Duet;

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

    public static AppOptions Parse(string[] args)
    {
        var chrome = DefaultChrome();
        var smoke = false;
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
            }
        }

        return new AppOptions { Chrome = chrome, Smoke = smoke };
    }

    private static ChromeKind DefaultChrome() =>
        OperatingSystem.IsWindows() ? ChromeKind.Win
        : OperatingSystem.IsMacOS() ? ChromeKind.Mac
        : ChromeKind.Gnome;
}

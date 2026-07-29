using System.Runtime.InteropServices;

namespace Duetto.Core.Remote;

/// <summary>
/// Returns per-OS paths for Duetto's configuration directory and well-known config files.
///
/// <list type="bullet">
///   <item><description>macOS — <c>~/Library/Application Support/Duetto/</c></description></item>
///   <item><description>Linux — <c>$XDG_CONFIG_HOME/duetto/</c> when the variable is set and
///     non-empty; otherwise <c>~/.config/duetto/</c></description></item>
///   <item><description>Windows — <c>%APPDATA%\Duetto\</c></description></item>
/// </list>
///
/// The directory is created on demand when <see cref="ConfigDir"/> is accessed.
/// All operations are idempotent.
/// </summary>
public static class AppPaths
{
    private const string AppName = "Duetto";

    /// <summary>
    /// Returns the Duetto configuration directory for the current user and OS, creating it
    /// if it does not exist.
    /// </summary>
    public static string ConfigDir
    {
        get
        {
            var dir = GetConfigDir();
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Path to <c>connections.json</c> inside <see cref="ConfigDir"/>.</summary>
    public static string ConnectionsJsonPath => Path.Combine(ConfigDir, "connections.json");

    /// <summary>Path to <c>hostkeys.json</c> inside <see cref="ConfigDir"/>.</summary>
    public static string HostKeysJsonPath => Path.Combine(ConfigDir, "hostkeys.json");

    // ── internal helpers ──────────────────────────────────────────────────────

    private static string GetConfigDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: ~/Library/Application Support/Duetto/
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", AppName);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux: $XDG_CONFIG_HOME/duetto/ or ~/.config/duetto/
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrEmpty(xdg))
                return Path.Combine(xdg, AppName.ToLowerInvariant());

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", AppName.ToLowerInvariant());
        }

        // Windows (and any other OS): %APPDATA%\Duetto\
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, AppName);
    }
}

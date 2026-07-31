using System.Runtime.InteropServices;

namespace Duetto.Core.Remote;

public static class AppPaths
{
    private const string AppName = "Duetto";

    // Created on demand.
    public static string ConfigDir
    {
        get
        {
            var dir = GetConfigDir();
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConnectionsJsonPath => Path.Combine(ConfigDir, "connections.json");

    public static string HostKeysJsonPath => Path.Combine(ConfigDir, "hostkeys.json");

    public static string WindowJsonPath => Path.Combine(ConfigDir, "window.json");

    public static string SessionJsonPath => Path.Combine(ConfigDir, "session.json");

    private static string GetConfigDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", AppName);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrEmpty(xdg))
                return Path.Combine(xdg, AppName.ToLowerInvariant());

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", AppName.ToLowerInvariant());
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, AppName);
    }
}

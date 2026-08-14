using Duetto.Core.Cli;

namespace Duetto;

internal static class CliInstall
{
    public static void EnsureBestEffort()
    {
        try
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
                return;

            var app = Environment.ProcessPath;
            if (string.IsNullOrEmpty(app))
                return;

            var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(Path.IsPathRooted)
                .ToList();

            var installer = new CliInstaller(
                commandExists: name => pathDirs.Any(d => IsExecutableFile(Path.Combine(d, name))),
                candidateDirs: pathDirs,
                isWritable: DirWritable,
                writeExecutable: WriteExecutable);

            installer.EnsureInstalled("duetto", app);
        }
        catch
        {
        }
    }

    private static bool IsExecutableFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool DirWritable(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
                return false;
            var probe = Path.Combine(dir, ".duetto-write-probe-" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteExecutable(string path, string content)
    {
        File.WriteAllText(path, content);
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}

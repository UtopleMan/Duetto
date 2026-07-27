using System.ComponentModel;
using System.Diagnostics;

namespace Duetto.Core.FileSystem;

public sealed record EjectResult(bool Success, string Error);

public static class VolumeEjector
{
    public delegate Task<(int ExitCode, string StdErr)> ProcessRunner(string fileName, string[] args);

    public static IReadOnlyList<(string File, string[] Args)> Commands(string mountPath, VolumePlatform platform) =>
        platform switch
        {
            VolumePlatform.Mac => [("diskutil", ["eject", mountPath])],
            VolumePlatform.Linux => [("gio", ["mount", "-u", mountPath]), ("umount", [mountPath])],
            _ => [],
        };

    public static async Task<EjectResult> EjectAsync(
        string mountPath, ProcessRunner? runner = null, VolumePlatform? platform = null)
    {
        var os = platform ?? (OperatingSystem.IsMacOS() ? VolumePlatform.Mac
            : OperatingSystem.IsWindows() ? VolumePlatform.Windows
            : VolumePlatform.Linux);
        var commands = Commands(mountPath, os);
        if (commands.Count == 0)
            return new EjectResult(false, "Eject is not supported on this platform");

        runner ??= RunProcessAsync;
        var lastError = "";
        foreach (var (file, args) in commands)
        {
            var (exitCode, stdErr) = await runner(file, args).ConfigureAwait(false);
            if (exitCode == 0)
                return new EjectResult(true, "");
            lastError = LastLine(stdErr) is { Length: > 0 } line ? line : $"{file} exited with code {exitCode}";
        }

        return new EjectResult(false, lastError);
    }

    private static string? LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } lines
            ? lines[^1]
            : null;

    private static async Task<(int ExitCode, string StdErr)> RunProcessAsync(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
                return (127, $"{fileName} failed to start");
            // Drain both pipes concurrently; reading only stderr while stdout fills
            // its pipe buffer would block the child process forever.
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await stdOutTask.ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            return (process.ExitCode, stdErr);
        }
        catch (Win32Exception e)
        {
            return (127, e.Message); // tool not installed — caller falls through to the next command
        }
        catch (IOException e)
        {
            return (127, e.Message); // missing tool can surface as IOException on Linux/macOS
        }
    }
}

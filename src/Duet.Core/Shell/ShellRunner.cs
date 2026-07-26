using System.Diagnostics;

namespace Duet.Core.Shell;

public enum ShellStream
{
    Output,
    Error,
}

public sealed record ShellLine(ShellStream Stream, string Text);

public sealed record ShellResult(int ExitCode, TimeSpan Duration);

public sealed class ShellRunner
{
    private readonly List<string> _history = [];

    public IReadOnlyList<string> History => _history;

    /// <summary>
    /// Runs a command line through the user's shell ($SHELL -c, cmd.exe /c on
    /// Windows) in <paramref name="workingDir"/>, streaming each output line to
    /// <paramref name="onLine"/> as it arrives.
    /// </summary>
    public async Task<ShellResult> RunAsync(
        string command, string workingDir, Action<ShellLine> onLine, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(command) &&
            (_history.Count == 0 || _history[^1] != command))
            _history.Add(command);

        var (shell, args) = ShellInvocation(command);
        var psi = new ProcessStartInfo
        {
            FileName = shell,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        var clock = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                onLine(new ShellLine(ShellStream.Output, e.Data));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                onLine(new ShellLine(ShellStream.Error, e.Data));
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        return new ShellResult(process.ExitCode, clock.Elapsed);
    }

    public static (string Shell, string[] Args) ShellInvocation(string command)
    {
        if (OperatingSystem.IsWindows())
            return ("cmd.exe", ["/c", command]);
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrEmpty(shell))
            shell = "/bin/sh";
        return (shell, ["-c", command]);
    }
}

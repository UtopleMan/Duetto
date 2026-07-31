namespace Duetto.Core.Cli;

/// <summary>
/// Installs a small shell launcher onto the user's PATH so the app can be started from a
/// terminal (e.g. <c>duetto .</c>). Decision logic is pure; all IO is injected so the
/// behavior is unit-testable. Callers run it best-effort and swallow failures.
/// </summary>
public sealed class CliInstaller
{
    private readonly Func<string, bool> _commandExists;
    private readonly IReadOnlyList<string> _candidateDirs;
    private readonly Func<string, bool> _isWritable;
    private readonly Action<string, string> _writeExecutable;

    /// <param name="commandExists">Whether the named command already resolves on PATH.</param>
    /// <param name="candidateDirs">Directories to consider, in priority order (typically PATH entries).</param>
    /// <param name="isWritable">Whether a launcher can be written into the given directory.</param>
    /// <param name="writeExecutable">Writes the launcher script and marks it executable.</param>
    public CliInstaller(
        Func<string, bool> commandExists,
        IReadOnlyList<string> candidateDirs,
        Func<string, bool> isWritable,
        Action<string, string> writeExecutable)
    {
        _commandExists = commandExists;
        _candidateDirs = candidateDirs;
        _isWritable = isWritable;
        _writeExecutable = writeExecutable;
    }

    /// <summary>
    /// The launcher script: exec the app's own executable, forwarding all arguments and
    /// inheriting the caller's working directory (so a relative folder argument resolves).
    /// </summary>
    public static string BuildLauncherScript(string appExecutablePath) =>
        $"#!/bin/sh\nexec \"{appExecutablePath}\" \"$@\"\n";

    /// <summary>
    /// Ensures the launcher exists. Returns the path written, or <see langword="null"/> when
    /// nothing was written — either the command already resolves on PATH, or no candidate
    /// directory is writable.
    /// </summary>
    public string? EnsureInstalled(string commandName, string appExecutablePath)
    {
        if (_commandExists(commandName))
            return null;

        foreach (var dir in _candidateDirs)
        {
            if (!_isWritable(dir))
                continue;

            var target = Path.Combine(dir, commandName);
            _writeExecutable(target, BuildLauncherScript(appExecutablePath));
            return target;
        }

        return null;
    }
}

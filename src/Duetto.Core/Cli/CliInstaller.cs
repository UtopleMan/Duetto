namespace Duetto.Core.Cli;

public sealed class CliInstaller
{
    private readonly Func<string, bool> _commandExists;
    private readonly IReadOnlyList<string> _candidateDirs;
    private readonly Func<string, bool> _isWritable;
    private readonly Action<string, string> _writeExecutable;

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

    // exec (not a subshell) so the launcher inherits the caller's working directory —
    // a relative folder argument must resolve against where the user ran the command.
    public static string BuildLauncherScript(string appExecutablePath) =>
        $"#!/bin/sh\nexec \"{appExecutablePath}\" \"$@\"\n";

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

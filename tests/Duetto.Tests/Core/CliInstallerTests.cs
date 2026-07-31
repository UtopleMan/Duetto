using Duetto.Core.Cli;
using Xunit;

namespace Duetto.Tests.Core;

public class CliInstallerTests
{
    private sealed record Write(string Path, string Content);

    [Fact]
    public void Launcher_script_execs_app_with_args()
    {
        var script = CliInstaller.BuildLauncherScript("/Applications/Duetto.app/Contents/MacOS/Duetto");
        Assert.StartsWith("#!/bin/sh", script);
        Assert.Contains("exec \"/Applications/Duetto.app/Contents/MacOS/Duetto\" \"$@\"", script);
    }

    [Fact]
    public void Already_present_command_writes_nothing()
    {
        var writes = new List<Write>();
        var installer = new CliInstaller(
            commandExists: _ => true,
            candidateDirs: ["/opt/homebrew/bin"],
            isWritable: _ => true,
            writeExecutable: (p, c) => writes.Add(new Write(p, c)));

        var result = installer.EnsureInstalled("duetto", "/app/Duetto");

        Assert.Null(result);
        Assert.Empty(writes);
    }

    [Fact]
    public void Writes_to_first_writable_dir_skipping_read_only()
    {
        var writes = new List<Write>();
        var installer = new CliInstaller(
            commandExists: _ => false,
            candidateDirs: ["/usr/bin", "/opt/homebrew/bin"],
            isWritable: dir => dir == "/opt/homebrew/bin",
            writeExecutable: (p, c) => writes.Add(new Write(p, c)));

        var result = installer.EnsureInstalled("duetto", "/app/Duetto");

        Assert.Equal("/opt/homebrew/bin/duetto", result);
        Assert.Single(writes);
        Assert.Equal("/opt/homebrew/bin/duetto", writes[0].Path);
        Assert.Equal(CliInstaller.BuildLauncherScript("/app/Duetto"), writes[0].Content);
    }

    [Fact]
    public void No_writable_dir_writes_nothing()
    {
        var writes = new List<Write>();
        var installer = new CliInstaller(
            commandExists: _ => false,
            candidateDirs: ["/usr/bin", "/sbin"],
            isWritable: _ => false,
            writeExecutable: (p, c) => writes.Add(new Write(p, c)));

        var result = installer.EnsureInstalled("duetto", "/app/Duetto");

        Assert.Null(result);
        Assert.Empty(writes);
    }
}

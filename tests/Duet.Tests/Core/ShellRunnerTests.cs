using Duet.Core.Shell;

namespace Duet.Tests.Core;

public class ShellRunnerTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public async Task Runs_in_working_directory_and_captures_output()
    {
        var runner = new ShellRunner();
        var lines = new List<ShellLine>();
        var command = OperatingSystem.IsWindows() ? "cd" : "pwd";

        var result = await runner.RunAsync(command, _tmp.Path, lines.Add);

        Assert.Equal(0, result.ExitCode);
        var pwd = Assert.Single(lines.Where(l => l.Stream == ShellStream.Output && l.Text.Length > 0));
        // macOS reports /private/var for the /var symlink — compare by unique leaf name.
        Assert.EndsWith(Path.GetFileName(_tmp.Path), pwd.Text.TrimEnd());
    }

    [Fact]
    public async Task Nonzero_exit_code_reported()
    {
        var runner = new ShellRunner();
        var result = await runner.RunAsync("exit 3", _tmp.Path, _ => { });
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task Stderr_lines_tagged_as_error()
    {
        var runner = new ShellRunner();
        var lines = new List<ShellLine>();
        await runner.RunAsync("echo oops 1>&2", _tmp.Path, l => { lock (lines) lines.Add(l); });
        Assert.Contains(lines, l => l.Stream == ShellStream.Error && l.Text.Contains("oops"));
    }

    [Fact]
    public async Task History_records_commands_without_consecutive_duplicates()
    {
        var runner = new ShellRunner();
        await runner.RunAsync("echo 1", _tmp.Path, _ => { });
        await runner.RunAsync("echo 1", _tmp.Path, _ => { });
        await runner.RunAsync("echo 2", _tmp.Path, _ => { });
        Assert.Equal(["echo 1", "echo 2"], runner.History);
    }
}

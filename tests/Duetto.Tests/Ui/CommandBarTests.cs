using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Duetto.Tests.Core;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public class CommandBarTests
{
    [AvaloniaFact]
    public async Task Echo_command_streams_output_and_reports_exit_zero()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.CommandBar.Input = "echo hello-duetto";
        await vm.CommandBar.RunAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.CommandBar.IsDrawerOpen);
        Assert.True(vm.CommandBar.HasExited);
        Assert.True(vm.CommandBar.ExitOk);
        Assert.StartsWith("exit 0 ·", vm.CommandBar.ExitText);
        Assert.Contains(vm.CommandBar.Output, l => l.Text.Contains("hello-duetto"));
        Assert.Equal("", vm.CommandBar.Input);
        Assert.Equal("echo hello-duetto", vm.CommandBar.RanCommand);
    }

    [AvaloniaFact]
    public async Task Failing_command_reports_nonzero_exit()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.CommandBar.Input = "exit 3";
        await vm.CommandBar.RunAsync();

        Assert.False(vm.CommandBar.ExitOk);
        Assert.StartsWith("exit 3 ·", vm.CommandBar.ExitText);
    }

    [AvaloniaFact]
    public async Task Command_runs_in_active_pane_directory()
    {
        using var left = new TempDir();
        using var right = new TempDir();
        left.File("left-marker.txt", "x");
        right.File("right-marker.txt", "x");
        using var vm = new MainViewModel(left.Path, right.Path);

        vm.CommandBar.Input = "ls";
        await vm.CommandBar.RunAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(vm.CommandBar.Output, l => l.Text.Contains("left-marker.txt"));

        vm.SwitchPane();
        vm.CommandBar.Input = "ls";
        await vm.CommandBar.RunAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(vm.CommandBar.Output, l => l.Text.Contains("right-marker.txt"));
    }

    [AvaloniaFact]
    public async Task History_up_recalls_previous_commands()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.CommandBar.Input = "echo first";
        await vm.CommandBar.RunAsync();
        vm.CommandBar.Input = "echo second";
        await vm.CommandBar.RunAsync();

        vm.CommandBar.HistoryUp();
        Assert.Equal("echo second", vm.CommandBar.Input);
        vm.CommandBar.HistoryUp();
        Assert.Equal("echo first", vm.CommandBar.Input);
        vm.CommandBar.HistoryDown();
        Assert.Equal("echo second", vm.CommandBar.Input);
    }

    [AvaloniaFact]
    public async Task Escape_closes_drawer_then_clears_input()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.CommandBar.Input = "echo x";
        await vm.CommandBar.RunAsync();
        Assert.True(vm.CommandBar.IsDrawerOpen);

        vm.CommandBar.Input = "partially typed";
        vm.CommandBar.Escape();
        Assert.False(vm.CommandBar.IsDrawerOpen);
        Assert.Equal("partially typed", vm.CommandBar.Input);

        vm.CommandBar.Escape();
        Assert.Equal("", vm.CommandBar.Input);
    }

    [AvaloniaFact]
    public async Task Stderr_lines_use_warning_color()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        vm.CommandBar.Input = "echo warn 1>&2";
        await vm.CommandBar.RunAsync();
        Dispatcher.UIThread.RunJobs();

        var line = Assert.Single(vm.CommandBar.Output, l => l.Text.Contains("warn"));
        Assert.Equal("#d9b45c", line.Color);
    }
}

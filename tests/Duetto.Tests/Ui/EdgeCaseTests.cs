using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Duetto.Tests.Core;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

public class EdgeCaseTests
{
    [AvaloniaFact]
    public void Empty_directory_shows_zero_items()
    {
        using var tmp = new TempDir();
        using var vm = new PaneViewModel(tmp.Path);
        var only = Assert.Single(vm.Rows);
        Assert.True(only.IsParentNav);
        Assert.Equal("0 items", vm.StatusText);
    }

    [AvaloniaFact]
    public void Vanished_directory_yields_empty_pane_not_crash()
    {
        using var tmp = new TempDir();
        var sub = tmp.Dir("gone");
        using var vm = new PaneViewModel(sub);
        Directory.Delete(sub);
        vm.Reload(preserveSelection: false);
        Assert.All(vm.Rows, r => Assert.True(r.IsParentNav));
    }

    [AvaloniaFact]
    public void Three_thousand_files_load_quickly()
    {
        using var tmp = new TempDir();
        for (var i = 0; i < 3000; i++)
            File.Create(Path.Combine(tmp.Path, $"file-{i:d5}.txt")).Dispose();

        var clock = Stopwatch.StartNew();
        using var vm = new PaneViewModel(tmp.Path);
        clock.Stop();

        Assert.Equal(3001, vm.Rows.Count);
        Assert.Equal("file-00000.txt", vm.Rows[1].Name);
        Assert.True(clock.ElapsedMilliseconds < 5000, $"load took {clock.ElapsedMilliseconds} ms");
    }

    [AvaloniaFact]
    public void Very_long_name_survives_roundtrip()
    {
        using var tmp = new TempDir();
        var longName = new string('x', 120) + ".txt";
        tmp.File(longName, "x");
        using var vm = new PaneViewModel(tmp.Path);
        Assert.Contains(vm.Rows, r => r.Name == longName);
    }
}

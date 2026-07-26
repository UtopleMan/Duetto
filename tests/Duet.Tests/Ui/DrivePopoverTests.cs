using Avalonia.Headless.XUnit;
using Duet.Core.FileSystem;
using Duet.Tests.Core;
using Duet.ViewModels;

namespace Duet.Tests.Ui;

public class DrivePopoverTests
{
    private static readonly VolumeInfo Root = new("Macintosh HD", "/", 500_000_000_000, 100_000_000_000, "APFS · 466 GB", false);

    private static VolumeInfo Backups(string mount = "/Volumes/Backups") =>
        new("Backups", mount, 2_000_000_000_000, 60_000_000_000, "APFS · 1.8 TB", true);

    private static DrivePopoverViewModel Popover(PaneViewModel pane, params VolumeInfo[] volumes)
    {
        var popover = pane.Drives;
        popover.ListVolumes = () => volumes;
        popover.Refresh();
        return popover;
    }

    [AvaloniaFact]
    public void Refresh_marks_current_volume_by_pane_path()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var popover = Popover(pane, Root, Backups());

        Assert.Equal("Macintosh HD", popover.Current?.Name);
        Assert.True(popover.Volumes.Single(v => v.Name == "Macintosh HD").IsCurrent);
        Assert.False(popover.Volumes.Single(v => v.Name == "Backups").IsCurrent);
    }

    [AvaloniaFact]
    public void Filter_narrows_by_name_and_mount()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var popover = Popover(pane, Root, Backups());

        popover.FilterText = "back";
        Assert.Equal(["Backups"], popover.Volumes.Select(v => v.Name));
        popover.FilterText = "/volumes";
        Assert.Equal(["Backups"], popover.Volumes.Select(v => v.Name));
        popover.FilterText = "";
        Assert.Equal(2, popover.Volumes.Count);
    }

    [AvaloniaFact]
    public void OpenVolume_navigates_pane_and_requests_close()
    {
        using var tmp = new TempDir();
        var sub = tmp.Dir("mount");
        using var pane = new PaneViewModel(tmp.Path);
        var target = new VolumeInfo("Sub", sub, 1000, 500, "x · 1000 B", false);
        var popover = Popover(pane, target);
        var closed = false;
        popover.CloseRequested += () => closed = true;

        popover.OpenVolume(popover.Volumes.Single());

        Assert.Equal(sub, pane.CurrentPath);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public async Task Eject_failure_shows_error_and_keeps_popover()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var current = new VolumeInfo("Stick", tmp.Path, 1000, 500, "x · 1000 B", true);
        var popover = Popover(pane, current);
        popover.Eject = _ => Task.FromResult(new EjectResult(false, "target is busy"));

        Assert.True(popover.CanEject);
        Assert.Equal("Eject Stick", popover.EjectLabel);
        await popover.EjectCurrentAsync();
        Assert.Equal("target is busy", popover.ErrorText);
    }

    [AvaloniaFact]
    public async Task Eject_success_closes_and_leaves_ejected_volume()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var current = new VolumeInfo("Stick", tmp.Path, 1000, 500, "x · 1000 B", true);
        var popover = Popover(pane, current);
        popover.Eject = _ => Task.FromResult(new EjectResult(true, ""));
        var closed = false;
        popover.CloseRequested += () => closed = true;

        await popover.EjectCurrentAsync();

        Assert.True(closed);
        Assert.NotEqual(tmp.Path, pane.CurrentPath); // pane left the ejected mount
    }

    [AvaloniaFact]
    public void Bar_color_by_usage()
    {
        Assert.Equal("#2f8f5b", new VolumeRowViewModel(new("a", "/", 100, 74, "x", false), false).BarColor);
        Assert.Equal("#c07a3a", new VolumeRowViewModel(new("b", "/", 100, 24, "x", false), false).BarColor);
        Assert.Equal("#b8443c", new VolumeRowViewModel(new("c", "/", 100, 5, "x", false), false).BarColor);
    }
}

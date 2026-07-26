using Duet.Core.FileSystem;

namespace Duet.Tests.Core;

public class VolumeEjectorTests
{
    [Fact]
    public void Commands_mac_uses_diskutil()
    {
        var commands = VolumeEjector.Commands("/Volumes/Backups", VolumePlatform.Mac);
        var (file, args) = Assert.Single(commands);
        Assert.Equal("diskutil", file);
        Assert.Equal(["eject", "/Volumes/Backups"], args);
    }

    [Fact]
    public void Commands_linux_tries_gio_then_umount()
    {
        var commands = VolumeEjector.Commands("/media/usb", VolumePlatform.Linux);
        Assert.Equal(2, commands.Count);

        var (file0, args0) = commands[0];
        Assert.Equal("gio", file0);
        Assert.Equal(new[] { "mount", "-u", "/media/usb" }, args0);

        var (file1, args1) = commands[1];
        Assert.Equal("umount", file1);
        Assert.Equal(new[] { "/media/usb" }, args1);
    }

    [Fact]
    public void Commands_windows_is_empty()
    {
        Assert.Empty(VolumeEjector.Commands(@"D:\", VolumePlatform.Windows));
    }

    [Fact]
    public async Task EjectAsync_success_on_first_command()
    {
        var calls = new List<string>();
        var result = await VolumeEjector.EjectAsync("/media/usb", (file, _) =>
        {
            calls.Add(file);
            return Task.FromResult((0, ""));
        }, VolumePlatform.Linux);
        Assert.True(result.Success);
        Assert.Equal(["gio"], calls);
    }

    [Fact]
    public async Task EjectAsync_falls_back_when_first_command_fails()
    {
        var calls = new List<string>();
        var result = await VolumeEjector.EjectAsync("/media/usb", (file, _) =>
        {
            calls.Add(file);
            return Task.FromResult(file == "gio" ? (127, "gio: not found") : (0, ""));
        }, VolumePlatform.Linux);
        Assert.True(result.Success);
        Assert.Equal(["gio", "umount"], calls);
    }

    [Fact]
    public async Task EjectAsync_reports_last_stderr_when_all_fail()
    {
        var result = await VolumeEjector.EjectAsync("/media/usb",
            (file, _) => Task.FromResult((1, $"{file}: target is busy")),
            VolumePlatform.Linux);
        Assert.False(result.Success);
        Assert.Equal("umount: target is busy", result.Error);
    }

    [Fact]
    public async Task EjectAsync_reports_exit_code_when_stderr_is_empty()
    {
        var result = await VolumeEjector.EjectAsync("/media/usb",
            (_, _) => Task.FromResult((3, "")),
            VolumePlatform.Linux);
        Assert.False(result.Success);
        Assert.Equal("umount exited with code 3", result.Error);
    }

    [Fact]
    public async Task EjectAsync_windows_fails_cleanly()
    {
        var result = await VolumeEjector.EjectAsync(@"D:\",
            (_, _) => Task.FromResult((0, "")),
            VolumePlatform.Windows);
        Assert.False(result.Success);
    }
}

using Duetto.Core.FileSystem;

namespace Duetto.Tests.Core;

public class VolumeCatalogTests
{
    private static VolumeSource Src(
        string mount, string? label = null, long total = 512_000_000_000, long free = 82_000_000_000,
        string format = "APFS", DriveType type = DriveType.Fixed) =>
        new(mount, label, total, free, format, type);

    [Fact]
    public void Build_names_from_label_falling_back_to_mount_dir()
    {
        var volumes = VolumeCatalog.Build(
            [Src("/Volumes/Backups", "Backups"), Src("/Volumes/SANDISK 128", label: null)],
            VolumePlatform.Mac);
        Assert.Equal(["Backups", "SANDISK 128"], volumes.Select(v => v.Name));
    }

    [Fact]
    public void Build_mac_root_is_macintosh_hd_when_unlabeled()
    {
        var volumes = VolumeCatalog.Build([Src("/", label: "")], VolumePlatform.Mac);
        Assert.Equal("Macintosh HD", Assert.Single(volumes).Name);
    }

    [Fact]
    public void Build_mac_root_ignores_mount_path_echoed_as_label()
    {
        var volumes = VolumeCatalog.Build([Src("/", label: "/")], VolumePlatform.Mac);
        Assert.Equal("Macintosh HD", Assert.Single(volumes).Name);
    }

    [Fact]
    public void Build_mount_dir_name_wins_over_mount_path_echoed_as_label()
    {
        var volumes = VolumeCatalog.Build(
            [Src("/Volumes/Backups", label: "/Volumes/Backups")],
            VolumePlatform.Mac);
        Assert.Equal("Backups", Assert.Single(volumes).Name);
    }

    [Fact]
    public void Build_windows_root_name_falls_back_to_drive_letter()
    {
        var volumes = VolumeCatalog.Build([Src(@"C:\", label: null)], VolumePlatform.Windows);
        Assert.Equal("C:", Assert.Single(volumes).Name);
    }

    [Fact]
    public void Build_skips_mac_system_snapshots_and_zero_size_and_squashfs()
    {
        var volumes = VolumeCatalog.Build(
            [
                Src("/System/Volumes/Data"),
                Src("/", "Macintosh HD"),
                Src("/snap/core", "core", format: "squashfs"),
                Src("/proc/foo", "foo", total: 0),
            ],
            VolumePlatform.Mac);
        Assert.Equal(["/"], volumes.Select(v => v.MountPath));
    }

    [Fact]
    public void Build_root_first_then_by_mount()
    {
        var volumes = VolumeCatalog.Build(
            [Src("/Volumes/zeta", "zeta"), Src("/", "Macintosh HD"), Src("/Volumes/alpha", "alpha")],
            VolumePlatform.Mac);
        Assert.Equal(["/", "/Volumes/alpha", "/Volumes/zeta"], volumes.Select(v => v.MountPath));
    }

    [Fact]
    public void Build_format_label_combines_fs_and_size()
    {
        var volume = Assert.Single(VolumeCatalog.Build(
            [Src(@"C:\", "Windows", total: 512L * 1024 * 1024 * 1024, format: "NTFS")],
            VolumePlatform.Windows));
        Assert.Equal("NTFS · 512 GB", volume.Format);
    }

    [Theory]
    [InlineData("/", VolumePlatform.Mac, DriveType.Fixed, false)]
    [InlineData("/Volumes/Backups", VolumePlatform.Mac, DriveType.Fixed, true)]
    [InlineData("/media/usb", VolumePlatform.Linux, DriveType.Fixed, true)]
    [InlineData("/run/media/anna/usb", VolumePlatform.Linux, DriveType.Fixed, true)]
    [InlineData("/mnt/data", VolumePlatform.Linux, DriveType.Fixed, true)]
    [InlineData("/home", VolumePlatform.Linux, DriveType.Fixed, false)]
    [InlineData(@"D:\", VolumePlatform.Windows, DriveType.Removable, true)]
    [InlineData(@"C:\", VolumePlatform.Windows, DriveType.Fixed, false)]
    public void Build_ejectable_rules(string mount, VolumePlatform platform, DriveType type, bool expected)
    {
        var volume = Assert.Single(VolumeCatalog.Build([Src(mount, "x", type: type)], platform));
        Assert.Equal(expected, volume.IsEjectable);
    }

    [Fact]
    public void UsedPercent_from_total_and_free()
    {
        var volume = new VolumeInfo("x", "/", 1000, 250, "APFS · 1 KB", false);
        Assert.Equal(75.0, volume.UsedPercent, precision: 3);
        Assert.Equal(0.0, new VolumeInfo("x", "/", 0, 0, "?", false).UsedPercent);
    }

    [Fact]
    public void FindByPath_picks_longest_mount_prefix()
    {
        var volumes = VolumeCatalog.Build(
            [Src("/", "Macintosh HD"), Src("/Volumes/Backups", "Backups")],
            VolumePlatform.Mac);
        Assert.Equal("Backups", VolumeCatalog.FindByPath(volumes, "/Volumes/Backups/2026-07")?.Name);
        Assert.Equal("Macintosh HD", VolumeCatalog.FindByPath(volumes, "/Users/anna")?.Name);
        Assert.Equal("Backups", VolumeCatalog.FindByPath(volumes, "/Volumes/Backups")?.Name);
        Assert.Equal("Macintosh HD", VolumeCatalog.FindByPath(volumes, "/Volumes/BackupsOld/x")?.Name);
        Assert.Null(VolumeCatalog.FindByPath([], "/anything"));
    }
}

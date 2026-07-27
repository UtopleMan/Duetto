# Drive Popover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drive popover (design 2a) opened from a new volume chip in each pane's path bar: local volumes with capacity bars, Connect… stub, Eject on mac/linux.

**Architecture:** Pure volume catalog + ejector in `Duetto.Core` (injected data/process-runner, unit-tested). Per-pane `DrivePopoverViewModel` in the app wires catalog to pane navigation. `PaneView` path bar splits into a chip `Button` whose `Flyout` hosts the popover card. Spec: `plans/2026-07-26-drive-popover-design.md`.

**Tech Stack:** net10.0, Avalonia 11.3.18 (+ Avalonia.Headless.XUnit in tests), CommunityToolkit.Mvvm 8.4.2, xunit.

## Global Constraints

- No new NuGet packages.
- Colors come from `App.axaml` resources where a key exists (`Accent` #2f6fd0, `ChipBg` #eef1f7, `RowHover` #f2f0ec, `HairlineLight` #e6e3dc, `HeaderText` #918f85, `MonoFont`); new literal colors allowed only for the three bar colors `#2f8f5b` / `#c07a3a` / `#b8443c` and swatch gray `#5b5950`.
- MVVM style: `ObservableObject` + `[ObservableProperty]`/`[RelayCommand]`, replaceable `Func<>`/delegate properties for test seams (see `PaneViewModel.LaunchFile`).
- Tests: Core logic in `tests/Duetto.Tests/Core` (plain xunit `[Fact]`), UI in `tests/Duetto.Tests/Ui` (`[AvaloniaFact]`).
- Test command: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "<filter>"` from repo root.
- Commit messages: plain imperative sentence, no attribution trailers, no AI mentions.
- Exception handling mirrors existing code: catch specific exception types only (`IOException`, `UnauthorizedAccessException`, …), never bare `catch`.

---

### Task 1: VolumeInfo + VolumeCatalog (Duetto.Core)

**Files:**
- Create: `src/Duetto.Core/FileSystem/VolumeInfo.cs`
- Create: `src/Duetto.Core/FileSystem/VolumeCatalog.cs`
- Test: `tests/Duetto.Tests/Core/VolumeCatalogTests.cs`

**Interfaces:**
- Consumes: `FormatUtil.HumanSize(long)` (existing).
- Produces:
  - `enum VolumePlatform { Windows, Mac, Linux }`
  - `record VolumeSource(string MountPath, string? Label, long TotalBytes, long FreeBytes, string DriveFormat, DriveType Type)`
  - `record VolumeInfo(string Name, string MountPath, long TotalBytes, long FreeBytes, string Format, bool IsEjectable)` with `double UsedPercent { get; }`
  - `static IReadOnlyList<VolumeInfo> VolumeCatalog.Build(IEnumerable<VolumeSource> sources, VolumePlatform platform)`
  - `static IReadOnlyList<VolumeInfo> VolumeCatalog.List()`
  - `static VolumeInfo? VolumeCatalog.FindByPath(IReadOnlyList<VolumeInfo> volumes, string path)`

- [ ] **Step 1: Write the failing tests**

`tests/Duetto.Tests/Core/VolumeCatalogTests.cs`:

```csharp
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
    [InlineData("/", VolumePlatform.Mac, DriveType.Fixed, false)]           // root never ejectable
    [InlineData("/Volumes/Backups", VolumePlatform.Mac, DriveType.Fixed, true)]
    [InlineData("/media/usb", VolumePlatform.Linux, DriveType.Fixed, true)]
    [InlineData("/run/media/anna/usb", VolumePlatform.Linux, DriveType.Fixed, true)]
    [InlineData("/mnt/data", VolumePlatform.Linux, DriveType.Fixed, true)]
    [InlineData("/home", VolumePlatform.Linux, DriveType.Fixed, false)]
    [InlineData(@"D:\", VolumePlatform.Windows, DriveType.Removable, true)] // removable always
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
        // "/Volumes/BackupsOld" must NOT match the "/Volumes/Backups" mount
        Assert.Equal("Macintosh HD", VolumeCatalog.FindByPath(volumes, "/Volumes/BackupsOld/x")?.Name);
        Assert.Null(VolumeCatalog.FindByPath([], "/anything"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~VolumeCatalogTests"`
Expected: build error — `VolumeCatalog` does not exist.

- [ ] **Step 3: Implement**

`src/Duetto.Core/FileSystem/VolumeInfo.cs`:

```csharp
namespace Duetto.Core.FileSystem;

public enum VolumePlatform
{
    Windows,
    Mac,
    Linux,
}

/// <summary>Raw data for one mounted volume, as read from DriveInfo (or faked in tests).</summary>
public sealed record VolumeSource(
    string MountPath, string? Label, long TotalBytes, long FreeBytes, string DriveFormat, DriveType Type);

public sealed record VolumeInfo(
    string Name, string MountPath, long TotalBytes, long FreeBytes, string Format, bool IsEjectable)
{
    public double UsedPercent => TotalBytes > 0 ? 100.0 * (TotalBytes - FreeBytes) / TotalBytes : 0;
}
```

`src/Duetto.Core/FileSystem/VolumeCatalog.cs`:

```csharp
namespace Duetto.Core.FileSystem;

public static class VolumeCatalog
{
    public static IReadOnlyList<VolumeInfo> Build(IEnumerable<VolumeSource> sources, VolumePlatform platform)
    {
        var volumes = new List<VolumeInfo>();
        foreach (var s in sources)
        {
            if (s.TotalBytes <= 0)
                continue;
            if (s.Type is not (DriveType.Fixed or DriveType.Removable or DriveType.Network))
                continue;
            if (string.Equals(s.DriveFormat, "squashfs", StringComparison.OrdinalIgnoreCase))
                continue;
            if (platform == VolumePlatform.Mac && s.MountPath.StartsWith("/System/Volumes", StringComparison.Ordinal))
                continue;

            volumes.Add(new VolumeInfo(
                DisplayName(s, platform),
                s.MountPath,
                s.TotalBytes,
                s.FreeBytes,
                $"{s.DriveFormat} · {FormatUtil.HumanSize(s.TotalBytes)}",
                IsEjectable(s, platform)));
        }

        return volumes
            .OrderBy(v => IsRoot(v.MountPath) ? 0 : 1)
            .ThenBy(v => v.MountPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Reads the real mounted volumes. Failures skip the volume, mirroring BuildPlaces.</summary>
    public static IReadOnlyList<VolumeInfo> List()
    {
        var platform = OperatingSystem.IsMacOS() ? VolumePlatform.Mac
            : OperatingSystem.IsWindows() ? VolumePlatform.Windows
            : VolumePlatform.Linux;
        var sources = new List<VolumeSource>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady)
                        continue;
                    sources.Add(new VolumeSource(
                        d.RootDirectory.FullName, d.VolumeLabel, d.TotalSize, d.AvailableFreeSpace,
                        d.DriveFormat, d.DriveType));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return Build(sources, platform);
    }

    /// <summary>The volume whose mount is the longest prefix of <paramref name="path"/> on a segment boundary.</summary>
    public static VolumeInfo? FindByPath(IReadOnlyList<VolumeInfo> volumes, string path)
    {
        VolumeInfo? best = null;
        foreach (var v in volumes)
        {
            var mount = v.MountPath.TrimEnd('/', '\\');
            var isMatch = mount.Length == 0 // unix root "/"
                || string.Equals(path, mount, StringComparison.OrdinalIgnoreCase)
                || (path.StartsWith(mount, StringComparison.OrdinalIgnoreCase)
                    && path.Length > mount.Length
                    && path[mount.Length] is '/' or '\\');
            if (isMatch && (best is null || mount.Length > best.MountPath.TrimEnd('/', '\\').Length))
                best = v;
        }

        return best;
    }

    private static string DisplayName(VolumeSource s, VolumePlatform platform)
    {
        if (!string.IsNullOrWhiteSpace(s.Label))
            return s.Label;
        if (platform == VolumePlatform.Mac && IsRoot(s.MountPath))
            return "Macintosh HD";
        var dir = Path.GetFileName(s.MountPath.TrimEnd('/', '\\'));
        return dir.Length > 0 ? dir : s.MountPath.TrimEnd('\\');
    }

    private static bool IsEjectable(VolumeSource s, VolumePlatform platform)
    {
        if (IsRoot(s.MountPath))
            return false;
        if (s.Type == DriveType.Removable)
            return true;
        return platform switch
        {
            VolumePlatform.Mac => s.MountPath.StartsWith("/Volumes/", StringComparison.Ordinal),
            VolumePlatform.Linux => s.MountPath.StartsWith("/media/", StringComparison.Ordinal)
                || s.MountPath.StartsWith("/run/media/", StringComparison.Ordinal)
                || s.MountPath.StartsWith("/mnt/", StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool IsRoot(string mount) =>
        mount == "/" || (mount.Length == 3 && mount[1] == ':' && (mount[2] == '\\' || mount[2] == '/'));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~VolumeCatalogTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Duetto.Core/FileSystem/VolumeInfo.cs src/Duetto.Core/FileSystem/VolumeCatalog.cs tests/Duetto.Tests/Core/VolumeCatalogTests.cs
git commit -m "Volume catalog with capacity and ejectable rules"
```

---

### Task 2: VolumeEjector (Duetto.Core)

**Files:**
- Create: `src/Duetto.Core/FileSystem/VolumeEjector.cs`
- Test: `tests/Duetto.Tests/Core/VolumeEjectorTests.cs`

**Interfaces:**
- Consumes: `VolumePlatform` (Task 1).
- Produces:
  - `record EjectResult(bool Success, string Error)`
  - `delegate Task<(int ExitCode, string StdErr)> ProcessRunner(string fileName, string[] args)` (nested in `VolumeEjector`)
  - `static IReadOnlyList<(string File, string[] Args)> VolumeEjector.Commands(string mountPath, VolumePlatform platform)`
  - `static Task<EjectResult> VolumeEjector.EjectAsync(string mountPath, VolumeEjector.ProcessRunner? runner = null)` — tries `Commands` for the current platform in order; a runner that cannot start the tool returns exit code 127.

- [ ] **Step 1: Write the failing tests**

`tests/Duetto.Tests/Core/VolumeEjectorTests.cs`:

```csharp
using Duetto.Core.FileSystem;

namespace Duetto.Tests.Core;

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
        Assert.Equal(("gio", (string[])["mount", "-u", "/media/usb"]), commands[0]);
        Assert.Equal(("umount", (string[])["/media/usb"]), commands[1]);
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
    public async Task EjectAsync_windows_fails_cleanly()
    {
        var result = await VolumeEjector.EjectAsync(@"D:\",
            (_, _) => Task.FromResult((0, "")),
            VolumePlatform.Windows);
        Assert.False(result.Success);
    }
}
```

Note the tests pass an explicit platform — `EjectAsync` needs an optional
`VolumePlatform? platform = null` parameter (null = detect current OS) so the
suite runs identically on every CI OS. Final signature:

```csharp
public static Task<EjectResult> EjectAsync(
    string mountPath, ProcessRunner? runner = null, VolumePlatform? platform = null)
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~VolumeEjectorTests"`
Expected: build error — `VolumeEjector` does not exist.

- [ ] **Step 3: Implement**

`src/Duetto.Core/FileSystem/VolumeEjector.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;

namespace Duetto.Core.FileSystem;

public sealed record EjectResult(bool Success, string Error);

public static class VolumeEjector
{
    public delegate Task<(int ExitCode, string StdErr)> ProcessRunner(string fileName, string[] args);

    public static IReadOnlyList<(string File, string[] Args)> Commands(string mountPath, VolumePlatform platform) =>
        platform switch
        {
            VolumePlatform.Mac => [("diskutil", ["eject", mountPath])],
            VolumePlatform.Linux => [("gio", ["mount", "-u", mountPath]), ("umount", [mountPath])],
            _ => [],
        };

    public static async Task<EjectResult> EjectAsync(
        string mountPath, ProcessRunner? runner = null, VolumePlatform? platform = null)
    {
        var os = platform ?? (OperatingSystem.IsMacOS() ? VolumePlatform.Mac
            : OperatingSystem.IsWindows() ? VolumePlatform.Windows
            : VolumePlatform.Linux);
        var commands = Commands(mountPath, os);
        if (commands.Count == 0)
            return new EjectResult(false, "Eject is not supported on this platform");

        runner ??= RunProcessAsync;
        var lastError = "";
        foreach (var (file, args) in commands)
        {
            var (exitCode, stdErr) = await runner(file, args).ConfigureAwait(false);
            if (exitCode == 0)
                return new EjectResult(true, "");
            lastError = LastLine(stdErr) is { Length: > 0 } line ? line : $"{file} exited with code {exitCode}";
        }

        return new EjectResult(false, lastError);
    }

    private static string? LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } lines
            ? lines[^1]
            : null;

    private static async Task<(int ExitCode, string StdErr)> RunProcessAsync(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdErr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            return (process.ExitCode, stdErr);
        }
        catch (Win32Exception e)
        {
            return (127, e.Message); // tool not installed — caller falls through to the next command
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~VolumeEjectorTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Duetto.Core/FileSystem/VolumeEjector.cs tests/Duetto.Tests/Core/VolumeEjectorTests.cs
git commit -m "Volume ejector with per-platform commands and fallback"
```

---

### Task 3: DrivePopoverViewModel + VolumeRowViewModel

**Files:**
- Create: `src/Duetto/ViewModels/DrivePopoverViewModel.cs`
- Test: `tests/Duetto.Tests/Ui/DrivePopoverTests.cs`

**Interfaces:**
- Consumes: `VolumeCatalog.List/FindByPath`, `VolumeInfo`, `VolumeEjector.EjectAsync`, `EjectResult` (Tasks 1–2); `PaneViewModel.NavigateTo(string)`, `PaneViewModel.CurrentPath` (existing).
- Produces (used by Tasks 4–6):
  - `class VolumeRowViewModel` with `VolumeInfo Volume`, `bool IsCurrent`, `string SwatchColor`, `string BarColor`, `double BarWidth`, `string FreeText`, `string Name`, `string MountPath`
  - `partial class DrivePopoverViewModel : ObservableObject` with:
    - ctor `DrivePopoverViewModel(PaneViewModel pane)`
    - `Func<IReadOnlyList<VolumeInfo>> ListVolumes { get; set; }` (default `VolumeCatalog.List`)
    - `Func<string, Task<EjectResult>> Eject { get; set; }` (default wraps `VolumeEjector.EjectAsync`)
    - `string PaneSide { get; set; }` ("left"/"right"), `string HeaderText => $"Open in {PaneSide} pane"`
    - `string FilterText` (observable; setter rebuilds `Volumes`)
    - `ObservableCollection<VolumeRowViewModel> Volumes { get; }`
    - `VolumeInfo? Current { get; }`, `bool CanEject { get; }`, `string EjectLabel { get; }`
    - `string ErrorText`, `bool IsEjecting` (observable)
    - `void Refresh()`, `void OpenVolume(VolumeRowViewModel row)`, `Task EjectCurrentAsync()`
    - `event Action? CloseRequested`, `event Action? ConnectRequested`
    - `[RelayCommand] void Connect()` → raises `ConnectRequested`
    - `VolumeInfo? VolumeFor(string path)` — catalog lookup against the last refreshed list (refreshes if never loaded)
  - Note: the spec's shares section is "hidden while empty" — implemented as no
    shares collection and no shares XAML at all until the remote backend exists
    (YAGNI; observable behavior identical).

- [ ] **Step 1: Write the failing tests**

`tests/Duetto.Tests/Ui/DrivePopoverTests.cs` (VM-only, but lives in Ui — it depends on the app project like other Ui tests; uses `[AvaloniaFact]` because `PaneViewModel` touches Avalonia types):

```csharp
using Avalonia.Headless.XUnit;
using Duetto.Core.FileSystem;
using Duetto.Tests.Core;
using Duetto.ViewModels;

namespace Duetto.Tests.Ui;

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
```

Note `Eject_success` asserts the pane navigated away (to the user profile dir);
on eject success when `pane.CurrentPath` is under the ejected mount, navigate to
`Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~DrivePopoverTests"`
Expected: build error — `DrivePopoverViewModel` / `pane.Drives` do not exist. (`pane.Drives` arrives in this task too — see step 3.)

- [ ] **Step 3: Implement**

`src/Duetto/ViewModels/DrivePopoverViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.FileSystem;

namespace Duetto.ViewModels;

public sealed class VolumeRowViewModel(VolumeInfo volume, bool isCurrent)
{
    public VolumeInfo Volume { get; } = volume;
    public bool IsCurrent { get; } = isCurrent;
    public string Name => Volume.Name;
    public string MountPath => Volume.MountPath;
    public string FreeText => $"{FormatUtil.HumanSize(Volume.FreeBytes)} free";
    public string SwatchColor => IsCurrent ? "#2f6fd0" : Volume.IsEjectable ? "#c8992f" : "#5b5950";
    public string BarColor => Volume.UsedPercent switch
    {
        > 90 => "#b8443c",
        > 75 => "#c07a3a",
        _ => "#2f8f5b",
    };
    public double BarWidth => Volume.UsedPercent * 1.7; // track is 170px wide
    public string RowBg => IsCurrent ? "#eef1f7" : "Transparent";
}

public partial class DrivePopoverViewModel(PaneViewModel pane) : ObservableObject
{
    private IReadOnlyList<VolumeInfo> _all = [];
    private bool _loaded;

    public Func<IReadOnlyList<VolumeInfo>> ListVolumes { get; set; } = VolumeCatalog.List;
    public Func<string, Task<EjectResult>> Eject { get; set; } = m => VolumeEjector.EjectAsync(m);

    public string PaneSide { get; set; } = "left";
    public string HeaderText => $"Open in {PaneSide} pane";
    // instance property: {Binding ConnectShortcut} can't resolve statics
    public string ConnectShortcut => OperatingSystem.IsMacOS() ? "⌘K" : "Ctrl K";

    public ObservableCollection<VolumeRowViewModel> Volumes { get; } = [];

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEject), nameof(EjectLabel))]
    private VolumeInfo? _current;

    [ObservableProperty]
    private string _errorText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEject))]
    private bool _isEjecting;

    public bool CanEject => Current is { IsEjectable: true } && !OperatingSystem.IsWindows() && !IsEjecting;
    public string EjectLabel => $"Eject {Current?.Name}";

    public event Action? CloseRequested;
    public event Action? ConnectRequested;

    partial void OnFilterTextChanged(string value) => RebuildRows();

    public void Refresh()
    {
        _all = ListVolumes();
        _loaded = true;
        Current = VolumeCatalog.FindByPath(_all, pane.CurrentPath);
        ErrorText = "";
        FilterText = "";
        RebuildRows();
    }

    public VolumeInfo? VolumeFor(string path)
    {
        if (!_loaded)
        {
            _all = ListVolumes();
            _loaded = true;
        }

        return VolumeCatalog.FindByPath(_all, path);
    }

    public void OpenVolume(VolumeRowViewModel row)
    {
        pane.NavigateTo(row.MountPath);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void Connect() => ConnectRequested?.Invoke();

    [RelayCommand]
    public async Task EjectCurrentAsync()
    {
        if (Current is not { IsEjectable: true } volume || IsEjecting)
            return;
        IsEjecting = true;
        ErrorText = "";
        try
        {
            var result = await Eject(volume.MountPath);
            if (!result.Success)
            {
                ErrorText = result.Error;
                return;
            }

            if (VolumeCatalog.FindByPath([volume], pane.CurrentPath) is not null)
                pane.NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            CloseRequested?.Invoke();
        }
        finally
        {
            IsEjecting = false;
        }
    }

    private void RebuildRows()
    {
        Volumes.Clear();
        foreach (var v in _all)
        {
            if (FilterText.Length > 0
                && !v.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
                && !v.MountPath.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
                continue;
            Volumes.Add(new VolumeRowViewModel(v, v.MountPath == Current?.MountPath));
        }
    }
}
```

Wire into `PaneViewModel` (modify `src/Duetto/ViewModels/PaneViewModel.cs`): add the
property and construct it in the ctor after `_currentPath` is set:

```csharp
    public DrivePopoverViewModel Drives { get; }
```

```csharp
    public PaneViewModel(string initialPath)
    {
        _currentPath = initialPath;
        Drives = new DrivePopoverViewModel(this);
        // …existing ctor body unchanged…
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~DrivePopoverTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Duetto/ViewModels/DrivePopoverViewModel.cs src/Duetto/ViewModels/PaneViewModel.cs tests/Duetto.Tests/Ui/DrivePopoverTests.cs
git commit -m "Drive popover view model with filter, navigation and eject"
```

---

### Task 4: Path-bar chip properties + side labels

**Files:**
- Modify: `src/Duetto/ViewModels/PaneViewModel.cs`
- Modify: `src/Duetto/ViewModels/MainViewModel.cs` (ctor)
- Test: `tests/Duetto.Tests/Ui/DrivePopoverTests.cs` (append)

**Interfaces:**
- Consumes: `DrivePopoverViewModel.VolumeFor(string)` (Task 3).
- Produces (bound by Task 5's XAML):
  - `string PaneViewModel.VolumeChipText` — volume display name; on Windows `"{mount} {name}"` (e.g. `D:\ Backups`), elsewhere the name (e.g. `Backups`). Falls back to `CurrentPath` when no volume matches.
  - `string PaneViewModel.PathTailText` — `CurrentPath` with the mount prefix removed, keeping the leading separator (e.g. `\2026-07`, `/Users/anna`); empty at the mount root.
  - `MainViewModel` ctor sets `Left.Drives.PaneSide = "left"; Right.Drives.PaneSide = "right";`

- [ ] **Step 1: Write the failing tests**

Append to `tests/Duetto.Tests/Ui/DrivePopoverTests.cs`:

```csharp
    [AvaloniaFact]
    public void Chip_splits_path_into_volume_and_tail()
    {
        using var tmp = new TempDir();
        var mount = tmp.Dir("stick");
        var inside = tmp.Dir("stick/photos");
        using var pane = new PaneViewModel(inside);
        pane.Drives.ListVolumes = () => [new VolumeInfo("Stick", mount, 1000, 500, "x · 1000 B", true)];

        Assert.Equal(OperatingSystem.IsWindows() ? $"{mount} Stick" : "Stick", pane.VolumeChipText);
        Assert.Equal(Path.DirectorySeparatorChar + "photos", pane.PathTailText);

        pane.NavigateTo(mount);
        Assert.Equal("", pane.PathTailText);
    }

    [AvaloniaFact]
    public void Chip_falls_back_to_full_path_without_volumes()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        pane.Drives.ListVolumes = () => [];

        Assert.Equal(tmp.Path, pane.VolumeChipText);
        Assert.Equal("", pane.PathTailText);
    }

    [AvaloniaFact]
    public void Main_view_model_labels_pane_sides()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);
        Assert.Equal("Open in left pane", vm.Left.Drives.HeaderText);
        Assert.Equal("Open in right pane", vm.Right.Drives.HeaderText);
    }
```

(`TempDir.Dir` returns the created directory's full path.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~DrivePopoverTests"`
Expected: build error — `VolumeChipText` does not exist.

- [ ] **Step 3: Implement**

In `PaneViewModel`, extend the `CurrentPath` observable property's notifications
and add the computed properties:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirName), nameof(VolumeChipText), nameof(PathTailText))]
    private string _currentPath;
```

```csharp
    public string VolumeChipText
    {
        get
        {
            if (Drives.VolumeFor(CurrentPath) is not { } volume)
                return CurrentPath;
            return OperatingSystem.IsWindows() ? $"{volume.MountPath} {volume.Name}" : volume.Name;
        }
    }

    public string PathTailText
    {
        get
        {
            if (Drives.VolumeFor(CurrentPath) is not { } volume)
                return "";
            var mount = volume.MountPath.TrimEnd('/', '\\');
            return CurrentPath.Length > mount.Length ? CurrentPath[mount.Length..] : "";
        }
    }
```

In `MainViewModel` ctor, right after `Right = new PaneViewModel(rightPath);`:

```csharp
        Left.Drives.PaneSide = "left";
        Right.Drives.PaneSide = "right";
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~DrivePopoverTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Duetto/ViewModels/PaneViewModel.cs src/Duetto/ViewModels/MainViewModel.cs tests/Duetto.Tests/Ui/DrivePopoverTests.cs
git commit -m "Path bar splits into volume chip and tail"
```

---

### Task 5: Chip + popover UI in PaneView

**Files:**
- Modify: `src/Duetto/Views/PaneView.axaml` (path-bar `DockPanel`, lines ~33–48, plus styles)
- Modify: `src/Duetto/Views/PaneView.axaml.cs`
- Test: `tests/Duetto.Tests/Ui/DrivePopoverTests.cs` (append)

**Interfaces:**
- Consumes: `PaneViewModel.VolumeChipText/PathTailText/Drives` (Tasks 3–4), `DrivePopoverViewModel` members (Task 3), resources from `App.axaml`.
- Produces: named controls `VolumeChip` (Button), `DriveFlyout` (Flyout), `DriveFilterBox` (TextBox), `DriveList` (ListBox) — Task 6 reuses the code-behind's `OnDriveFilterKeyDown`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Duetto.Tests/Ui/DrivePopoverTests.cs`:

```csharp
    [AvaloniaFact]
    public void Path_bar_shows_volume_chip()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);
        var window = new MainWindow(vm);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var chip = window.GetVisualDescendants().OfType<Views.PaneView>().First()
            .FindControl<Avalonia.Controls.Button>("VolumeChip");
        Assert.NotNull(chip);
        Assert.True(chip!.IsVisible);
        window.Close();
    }
```

Add `using Avalonia.VisualTree;`, `using Avalonia.Controls;` and `using Duetto.Views;`
to the file's usings (then `Views.PaneView` becomes just `PaneView`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~Path_bar_shows_volume_chip"`
Expected: FAIL — `FindControl("VolumeChip")` returns null.

- [ ] **Step 3: Implement the XAML**

Replace the path-bar `DockPanel` content in `PaneView.axaml` (keep the `ACTIVE`
label) with:

```xml
      <DockPanel Margin="9,0,12,0" VerticalAlignment="Center">
        <TextBlock DockPanel.Dock="Right" Text="ACTIVE"
                   IsVisible="{Binding IsActive}"
                   FontSize="10.5" LetterSpacing="0.7"
                   Foreground="{StaticResource AccentDim}"
                   VerticalAlignment="Center" />
        <Button DockPanel.Dock="Left" x:Name="VolumeChip" Classes="volumechip"
                Click="OnVolumeChipClicked">
          <StackPanel Orientation="Horizontal" Spacing="7" VerticalAlignment="Center">
            <Border Width="10" Height="10" CornerRadius="2" Background="{StaticResource Accent}" />
            <TextBlock Text="{Binding VolumeChipText}"
                       FontFamily="{StaticResource MonoFont}" FontSize="11"
                       VerticalAlignment="Center" />
            <TextBlock Text="▾" FontSize="9" VerticalAlignment="Center" />
          </StackPanel>
          <Button.Flyout>
            <Flyout x:Name="DriveFlyout" Placement="BottomEdgeAlignedLeft" ShowMode="Transient">
              <Border Width="392" Background="#fdfcfa" CornerRadius="10"
                      BorderBrush="{StaticResource HairlineDark}" BorderThickness="1"
                      DataContext="{Binding Drives}">
                <StackPanel>
                  <!-- Header -->
                  <Border Height="30" Background="{StaticResource ChromeBg}"
                          BorderBrush="{StaticResource HairlineLight}" BorderThickness="0,0,0,1"
                          CornerRadius="10,10,0,0">
                    <DockPanel Margin="12,0" VerticalAlignment="Center">
                      <TextBlock DockPanel.Dock="Right" Text="type to filter"
                                 FontFamily="{StaticResource MonoFont}" FontSize="10"
                                 Foreground="{StaticResource TextGhost}" VerticalAlignment="Center" />
                      <TextBlock Text="{Binding HeaderText}" FontSize="11"
                                 Foreground="{StaticResource TextDim}" VerticalAlignment="Center" />
                    </DockPanel>
                  </Border>
                  <StackPanel Margin="6,7,6,6">
                    <!-- invisible-border filter box: it has focus; typing filters -->
                    <TextBox x:Name="DriveFilterBox" Text="{Binding FilterText}"
                             Background="Transparent" BorderThickness="0" MinHeight="0"
                             Padding="10,0,10,4" FontSize="11" CaretBrush="{StaticResource Accent}"
                             KeyDown="OnDriveFilterKeyDown" />
                    <TextBlock Text="THIS MACHINE" FontSize="10.5" LetterSpacing="0.6"
                               Foreground="{StaticResource HeaderText}" Margin="10,0,10,6" />
                    <ListBox x:Name="DriveList" ItemsSource="{Binding Volumes}"
                             Background="Transparent" Padding="0"
                             DoubleTapped="OnDriveRowActivated" Tapped="OnDriveRowActivated">
                      <ListBox.ItemContainerTheme>
                        <ControlTheme TargetType="ListBoxItem">
                          <Setter Property="Padding" Value="0" />
                          <Setter Property="MinHeight" Value="38" />
                          <Setter Property="CornerRadius" Value="7" />
                          <Setter Property="Background" Value="Transparent" />
                          <Setter Property="Template">
                            <ControlTemplate>
                              <ContentPresenter Name="PART_ContentPresenter"
                                                Background="{TemplateBinding Background}"
                                                CornerRadius="{TemplateBinding CornerRadius}"
                                                Content="{TemplateBinding Content}"
                                                ContentTemplate="{TemplateBinding ContentTemplate}"
                                                Padding="{TemplateBinding Padding}" />
                            </ControlTemplate>
                          </Setter>
                          <Style Selector="^:pointerover">
                            <Setter Property="Background" Value="{StaticResource ChipBg}" />
                          </Style>
                          <Style Selector="^:selected">
                            <Setter Property="Background" Value="{StaticResource ChipBg}" />
                          </Style>
                        </ControlTheme>
                      </ListBox.ItemContainerTheme>
                      <ListBox.ItemTemplate>
                        <DataTemplate x:DataType="vm:VolumeRowViewModel">
                          <Border Background="{Binding RowBg}" CornerRadius="7" Padding="10,0" Height="38">
                            <Grid ColumnDefinitions="12,11,*,11,96" VerticalAlignment="Center">
                              <Border Grid.Column="0" Width="12" Height="12" CornerRadius="3"
                                      Background="{Binding SwatchColor}" />
                              <StackPanel Grid.Column="2" Spacing="3" VerticalAlignment="Center">
                                <StackPanel Orientation="Horizontal" Spacing="7">
                                  <TextBlock Text="{Binding Name}" FontSize="12.5" FontWeight="Medium"
                                             Foreground="{StaticResource TextPrimary}"
                                             TextTrimming="CharacterEllipsis" />
                                  <TextBlock Text="{Binding MountPath}"
                                             FontFamily="{StaticResource MonoFont}" FontSize="10"
                                             Foreground="{StaticResource TextGhost}" VerticalAlignment="Center" />
                                </StackPanel>
                                <Border Height="3" CornerRadius="2" Background="{StaticResource HairlineLight}"
                                        Width="170" HorizontalAlignment="Left">
                                  <Border Height="3" CornerRadius="2" Background="{Binding BarColor}"
                                          Width="{Binding BarWidth}" HorizontalAlignment="Left" />
                                </Border>
                              </StackPanel>
                              <TextBlock Grid.Column="4" Text="{Binding FreeText}"
                                         FontFamily="{StaticResource MonoFont}" FontSize="10.5"
                                         Foreground="{StaticResource TextFaint}" HorizontalAlignment="Right"
                                         VerticalAlignment="Center" />
                            </Grid>
                          </Border>
                        </DataTemplate>
                      </ListBox.ItemTemplate>
                    </ListBox>
                    <Border Height="1" Background="{StaticResource HairlineLight}" Margin="10,7" />
                    <!-- Connect… row (stub; dialog wired in Task 6) -->
                    <Button x:Name="ConnectRow" Command="{Binding ConnectCommand}"
                            Background="{StaticResource ChipBg}" CornerRadius="7" Height="34"
                            Padding="10,0" HorizontalAlignment="Stretch"
                            HorizontalContentAlignment="Stretch" BorderThickness="0">
                      <DockPanel VerticalAlignment="Center">
                        <Border DockPanel.Dock="Left" Width="12" Height="12" CornerRadius="3"
                                BorderBrush="{StaticResource Accent}" BorderThickness="1.5"
                                Margin="0,0,10,0" VerticalAlignment="Center" />
                        <TextBlock DockPanel.Dock="Right"
                                   Text="{Binding ConnectShortcut}"
                                   FontFamily="{StaticResource MonoFont}" FontSize="10"
                                   Foreground="{StaticResource AccentDim}" VerticalAlignment="Center" />
                        <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
                          <TextBlock Text="Connect…" FontSize="12.5" FontWeight="Medium"
                                     Foreground="{StaticResource Accent}" />
                          <TextBlock Text="SFTP, S3 or SMB" FontSize="11"
                                     Foreground="{StaticResource AccentDim}" />
                        </StackPanel>
                      </DockPanel>
                    </Button>
                    <!-- Eject row -->
                    <Button x:Name="EjectRow" Command="{Binding EjectCurrentCommand}"
                            IsVisible="{Binding CanEject}"
                            Background="Transparent" CornerRadius="7" Height="30"
                            Padding="10,0" HorizontalAlignment="Stretch"
                            HorizontalContentAlignment="Left" BorderThickness="0">
                      <TextBlock Text="{Binding EjectLabel}" FontSize="12.5"
                                 Foreground="{StaticResource TextDim}" VerticalAlignment="Center" />
                    </Button>
                    <TextBlock Text="{Binding ErrorText}" FontSize="11"
                               Foreground="{StaticResource DangerText}" Margin="10,2,10,4"
                               TextWrapping="Wrap"
                               IsVisible="{Binding ErrorText, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                  </StackPanel>
                </StackPanel>
              </Border>
            </Flyout>
          </Button.Flyout>
        </Button>
        <TextBlock x:Name="PathText" Text="{Binding PathTailText}" Margin="7,0,0,0"
                   FontFamily="{StaticResource MonoFont}" FontSize="11"
                   TextTrimming="CharacterEllipsis"
                   VerticalAlignment="Center" />
      </DockPanel>
```

Add chip styles to `PaneView.axaml`'s `<UserControl.Styles>`:

```xml
    <Style Selector="Button.volumechip">
      <Setter Property="Background" Value="Transparent" />
      <Setter Property="BorderThickness" Value="1" />
      <Setter Property="BorderBrush" Value="Transparent" />
      <Setter Property="CornerRadius" Value="5" />
      <Setter Property="Padding" Value="8,3" />
      <Setter Property="MinHeight" Value="0" />
      <Setter Property="Foreground" Value="{StaticResource TextDim}" />
    </Style>
    <Style Selector="Button.volumechip:pointerover /template/ ContentPresenter">
      <Setter Property="Background" Value="{StaticResource ButtonHover}" />
    </Style>
    <Style Selector="Border#PathBar.active Button.volumechip">
      <Setter Property="Background" Value="#ffffff" />
      <Setter Property="BorderBrush" Value="#b9cbea" />
      <Setter Property="Foreground" Value="{StaticResource Accent}" />
    </Style>
    <Style Selector="Border#PathBar.active TextBlock#PathText">
      <Setter Property="Foreground" Value="{StaticResource AccentDim}" />
    </Style>
```

(The last selector replaces the existing `Border#PathBar.active TextBlock#PathText`
style block — the tail is dimmer than the chip per design; delete the old
`FontWeight` setter there.)

Note the existing style `Style Selector="TextBlock#PathText"` keeps the inactive
foreground. The old plain-path `TextBlock` is replaced by the chip + tail pair.

- [ ] **Step 4: Implement the code-behind**

Add to `PaneView.axaml.cs`:

```csharp
    private void OnVolumeChipClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        Interacted?.Invoke(this);
        vm.Drives.Refresh();
        // Flyout opens automatically (Button.Flyout); focus the filter box for type-to-filter.
        Dispatcher.UIThread.Post(() =>
        {
            DriveFilterBox.Focus();
            if (DriveList.ItemCount > 0 && DriveList.SelectedIndex < 0)
                DriveList.SelectedIndex = 0;
        });
    }

    private void OnDriveRowActivated(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && (e.Source as Control)?.DataContext is VolumeRowViewModel row)
        {
            vm.Drives.OpenVolume(row);
            e.Handled = true;
        }
    }

    private void OnDriveFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        switch (e.Key)
        {
            case Key.Down:
                DriveList.SelectedIndex = Math.Min(DriveList.SelectedIndex + 1, DriveList.ItemCount - 1);
                e.Handled = true;
                break;
            case Key.Up:
                DriveList.SelectedIndex = Math.Max(DriveList.SelectedIndex - 1, 0);
                e.Handled = true;
                break;
            case Key.Enter when DriveList.SelectedItem is VolumeRowViewModel row:
                vm.Drives.OpenVolume(row);
                e.Handled = true;
                break;
            case Key.Escape:
                HideDriveFlyout();
                e.Handled = true;
                break;
            case Key.K when e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                vm.Drives.Connect();
                e.Handled = true;
                break;
        }
    }
```

Add a helper and subscribe `CloseRequested` where the view model is (re)bound —
inside the existing `DataContextChanged` handler in the constructor, after
`_subscribedVm = Vm;`:

```csharp
    /// <summary>x:Name fields inside Flyout content can be unreliable; go via the chip.</summary>
    private void HideDriveFlyout()
    {
        (VolumeChip.Flyout as Avalonia.Controls.Primitives.FlyoutBase)?.Hide();
        FocusList();
    }
```

```csharp
            if (_subscribedVm is { } newVm)
                newVm.Drives.CloseRequested += () => Dispatcher.UIThread.Post(HideDriveFlyout);
```

If the generated `DriveFilterBox` / `DriveList` fields come back null at runtime
(they live inside Flyout content), resolve them lazily instead:
`var box = (VolumeChip.Flyout as Flyout)?.Content is Control c ? c.FindControl<TextBox>("DriveFilterBox") : null;`
— but try the generated fields first; they normally work for inline flyout content.

(The lambda captures the chip; panes never swap view models after startup, so
no unsubscribe bookkeeping is needed — mirrors the `Reloaded` handling.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~Path_bar_shows_volume_chip"`
Expected: PASS.

- [ ] **Step 6: Run the whole suite (UI regressions)**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj`
Expected: all pass. Watch for `ChromeTests` / `PaneTests` breaking on the path-bar
change — those assert against `PathText`; update any assertion that expected the
full path in `PathText` to use `PathTailText` semantics or the chip.

- [ ] **Step 7: Commit**

```bash
git add src/Duetto/Views/PaneView.axaml src/Duetto/Views/PaneView.axaml.cs tests/Duetto.Tests/Ui/DrivePopoverTests.cs
git commit -m "Volume chip opens drive popover in path bar"
```

---

### Task 6: Connect… placeholder dialog

**Files:**
- Create: `src/Duetto/Views/ConnectStubWindow.axaml`
- Create: `src/Duetto/Views/ConnectStubWindow.axaml.cs`
- Modify: `src/Duetto/Views/PaneView.axaml.cs` (handle `ConnectRequested`)
- Test: `tests/Duetto.Tests/Ui/DrivePopoverTests.cs` (append)

**Interfaces:**
- Consumes: `DrivePopoverViewModel.ConnectRequested` / `Connect()` (Task 3).
- Produces: `ConnectStubWindow : Window` (modal, `ShowDialog(owner)`).

- [ ] **Step 1: Write the failing test**

Append to `tests/Duetto.Tests/Ui/DrivePopoverTests.cs`:

```csharp
    [AvaloniaFact]
    public void Connect_command_raises_request()
    {
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        var popover = Popover(pane, Root);
        var requested = false;
        popover.ConnectRequested += () => requested = true;

        popover.Connect();

        Assert.True(requested);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~Connect_command_raises_request"`
Expected: PASS already if Task 3 shipped `Connect()`/`ConnectRequested` — then this
step only guards the contract. If it fails to build, fix Task 3's VM first.

- [ ] **Step 3: Implement the dialog**

`src/Duetto/Views/ConnectStubWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="Duetto.Views.ConnectStubWindow"
        Title="Connect"
        Width="360" Height="170"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        Background="{StaticResource WindowBg}">
  <StackPanel Spacing="10" HorizontalAlignment="Center" VerticalAlignment="Center">
    <Border Width="28" Height="28" CornerRadius="7"
            BorderBrush="{StaticResource Accent}" BorderThickness="1.5"
            HorizontalAlignment="Center" />
    <TextBlock Text="Remote connections are coming soon" FontSize="13" FontWeight="SemiBold"
               Foreground="{StaticResource TextPrimary}" HorizontalAlignment="Center" />
    <TextBlock Text="SFTP, S3 and SMB shares will connect from here." FontSize="11.5"
               Foreground="{StaticResource TextDim}" HorizontalAlignment="Center" />
    <Button Content="Close" Click="OnCloseClicked" HorizontalAlignment="Center"
            Padding="18,4" CornerRadius="6" />
  </StackPanel>
</Window>
```

`src/Duetto/Views/ConnectStubWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Duetto.Views;

public partial class ConnectStubWindow : Window
{
    public ConnectStubWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
```

Wire in `PaneView.axaml.cs`, next to the `CloseRequested` subscription added in
Task 5 (same `DataContextChanged` block):

```csharp
            if (_subscribedVm is { } vmForConnect)
                vmForConnect.Drives.ConnectRequested += () => Dispatcher.UIThread.Post(() =>
                {
                    HideDriveFlyout();
                    if (TopLevel.GetTopLevel(this) is Window owner)
                        new ConnectStubWindow().ShowDialog(owner);
                });
```

(Merge both subscriptions into one `if` block — single pattern variable, two
`+=` lines — rather than two separate `if`s.)

- [ ] **Step 4: Run test and build**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~DrivePopoverTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Duetto/Views/ConnectStubWindow.axaml src/Duetto/Views/ConnectStubWindow.axaml.cs src/Duetto/Views/PaneView.axaml.cs tests/Duetto.Tests/Ui/DrivePopoverTests.cs
git commit -m "Connect placeholder dialog from drive popover"
```

---

### Task 7: Full verification

**Files:**
- No new files. Possibly touched: any test broken by the path-bar change.

- [ ] **Step 1: Full suite**

Run: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj`
Expected: all pass (76 pre-existing + new ones). Fix regressions, don't skip.

- [ ] **Step 2: Smoke the app**

Run: `dotnet run --project src/Duetto -- --smoke`
Expected: exit 0.

- [ ] **Step 3: Visual check against design 2a**

Run: `dotnet run --project src/Duetto -- --screenshot /tmp/duetto-popover.png` and also
launch interactively (`dotnet run --project src/Duetto`): click the chip, verify
popover layout (header, capacity bars, current-volume tint, Connect… row, eject
row on an external volume), typing filters, ↑/↓ + Enter navigates, Esc closes.

- [ ] **Step 4: Commit any fixes**

```bash
git add -A
git commit -m "Fix regressions from drive popover"
```

(Skip if nothing changed.)

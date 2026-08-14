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

    public static VolumeInfo? FindByPath(IReadOnlyList<VolumeInfo> volumes, string path)
    {
        VolumeInfo? best = null;
        foreach (var v in volumes)
        {
            var mount = v.MountPath.TrimEnd('/', '\\');
            var isMatch = mount.Length == 0
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
        if (!string.IsNullOrWhiteSpace(s.Label) && !IsMountEcho(s.Label, s.MountPath))
            return s.Label;
        if (platform == VolumePlatform.Mac && IsRoot(s.MountPath))
            return "Macintosh HD";
        var dir = Path.GetFileName(s.MountPath.TrimEnd('/', '\\'));
        return dir.Length > 0 ? dir : s.MountPath.TrimEnd('\\');
    }

    private static bool IsEjectable(VolumeSource s, VolumePlatform platform)
    {
        if (s.Type == DriveType.Removable)
            return true;
        if (IsRoot(s.MountPath))
            return false;
        return platform switch
        {
            VolumePlatform.Mac => s.MountPath.StartsWith("/Volumes/", StringComparison.Ordinal),
            VolumePlatform.Linux => s.MountPath.StartsWith("/media/", StringComparison.Ordinal)
                || s.MountPath.StartsWith("/run/media/", StringComparison.Ordinal)
                || s.MountPath.StartsWith("/mnt/", StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool IsMountEcho(string label, string mount) =>
        string.Equals(label.TrimEnd('/', '\\'), mount.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);

    private static bool IsRoot(string mount) =>
        mount == "/" || (mount.Length == 3 && mount[1] == ':' && (mount[2] == '\\' || mount[2] == '/'));
}

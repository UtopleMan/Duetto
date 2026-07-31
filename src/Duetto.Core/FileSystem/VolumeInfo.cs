namespace Duetto.Core.FileSystem;

public enum VolumePlatform
{
    Windows,
    Mac,
    Linux,
}

public sealed record VolumeSource(
    string MountPath, string? Label, long TotalBytes, long FreeBytes, string DriveFormat, DriveType Type);

public sealed record VolumeInfo(
    string Name, string MountPath, long TotalBytes, long FreeBytes, string Format, bool IsEjectable)
{
    public double UsedPercent => TotalBytes > 0 ? 100.0 * (TotalBytes - FreeBytes) / TotalBytes : 0;
}

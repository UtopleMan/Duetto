using System.Runtime.InteropServices;
using System.Text;

namespace Duetto.Core.Operations;

public static class TrashService
{
    /// <summary>
    /// Moves a file or directory to the OS trash. Returns the path inside the
    /// trash on Unix, null on Windows (shell API does not report it).
    /// </summary>
    public static string? Trash(string fullPath)
    {
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new FileNotFoundException("Nothing to trash", fullPath);

        if (OperatingSystem.IsWindows())
        {
            TrashWindows(fullPath);
            return null;
        }

        return OperatingSystem.IsMacOS() ? TrashMac(fullPath) : TrashFreedesktop(fullPath);
    }

    private static string TrashMac(string fullPath)
    {
        var trashDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
        Directory.CreateDirectory(trashDir);
        var dest = UniqueDestination(trashDir, Path.GetFileName(fullPath));
        MoveAny(fullPath, dest);
        return dest;
    }

    private static string TrashFreedesktop(string fullPath)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome))
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share");
        var filesDir = Path.Combine(dataHome, "Trash/files");
        var infoDir = Path.Combine(dataHome, "Trash/info");
        Directory.CreateDirectory(filesDir);
        Directory.CreateDirectory(infoDir);

        var dest = UniqueDestination(filesDir, Path.GetFileName(fullPath));
        var trashName = Path.GetFileName(dest);
        File.WriteAllText(
            Path.Combine(infoDir, trashName + ".trashinfo"),
            $"[Trash Info]\nPath={Uri.EscapeDataString(fullPath).Replace("%2F", "/")}\n" +
            $"DeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
        MoveAny(fullPath, dest);
        return dest;
    }

    private static string UniqueDestination(string dir, string name)
    {
        var dest = Path.Combine(dir, name);
        if (!File.Exists(dest) && !Directory.Exists(dest))
            return dest;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var n = 1;
        do
        {
            dest = Path.Combine(dir, $"{stem} {++n}{ext}");
        } while (File.Exists(dest) || Directory.Exists(dest));

        return dest;
    }

    private static void MoveAny(string source, string dest)
    {
        if (Directory.Exists(source))
            Directory.Move(source, dest);
        else
            File.Move(source, dest);
    }

    private static void TrashWindows(string fullPath)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = 3, // FO_DELETE
            pFrom = fullPath + "\0\0",
            // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
            fFlags = 0x0040 | 0x0010 | 0x0004,
        };
        var result = SHFileOperationW(ref op);
        if (result != 0 || op.fAnyOperationsAborted)
            throw new IOException($"Recycle bin operation failed (code {result})");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);
}

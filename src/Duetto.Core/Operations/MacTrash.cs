using System.Runtime.InteropServices;

namespace Duetto.Core.Operations;

/// <summary>
/// macOS-only: moves an item to the trash via Cocoa's
/// <c>[[NSFileManager defaultManager] trashItemAtURL:resultingItemURL:error:]</c> — the same
/// API Finder uses. Unlike a raw move into <c>~/.Trash</c>, this records "Put Back" metadata
/// and routes items on other volumes to that volume's own <c>.Trashes</c>.
/// </summary>
internal static class MacTrash
{
    private const string Libobjc = "/usr/lib/libobjc.dylib";

    [DllImport(Libobjc)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(Libobjc)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendUtf8(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern byte SendTrash(IntPtr receiver, IntPtr selector, IntPtr url, ref IntPtr resultUrl, ref IntPtr error);

    /// <summary>
    /// Trashes <paramref name="fullPath"/> and returns the resulting item's path inside the
    /// trash. Throws <see cref="IOException"/> if the native call fails.
    /// </summary>
    public static string Trash(string fullPath)
    {
        var nsString = objc_getClass("NSString");
        var nsUrl = objc_getClass("NSURL");
        var nsFileManager = objc_getClass("NSFileManager");

        var pathString = SendUtf8(nsString, sel_registerName("stringWithUTF8String:"), fullPath);
        var url = SendPtr(nsUrl, sel_registerName("fileURLWithPath:"), pathString);
        var manager = Send(nsFileManager, sel_registerName("defaultManager"));

        IntPtr resultUrl = IntPtr.Zero;
        IntPtr error = IntPtr.Zero;
        var ok = SendTrash(manager, sel_registerName("trashItemAtURL:resultingItemURL:error:"),
            url, ref resultUrl, ref error);

        if (ok == 0)
            throw new IOException($"macOS trashItemAtURL failed: {DescribeError(error)}");

        return resultUrl != IntPtr.Zero
            ? Utf8(Send(resultUrl, sel_registerName("path")))
            : fullPath;
    }

    private static string DescribeError(IntPtr error)
    {
        if (error == IntPtr.Zero)
            return "unknown error";
        var description = Send(error, sel_registerName("localizedDescription"));
        return description == IntPtr.Zero ? "unknown error" : Utf8(description);
    }

    /// <summary>Reads an NSString's UTF-8 bytes into a managed string.</summary>
    private static string Utf8(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
            return "";
        var utf8 = Send(nsString, sel_registerName("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8) ?? "";
    }
}

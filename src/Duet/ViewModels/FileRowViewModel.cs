using CommunityToolkit.Mvvm.ComponentModel;
using Duet.Core.FileSystem;

namespace Duet.ViewModels;

public partial class FileRowViewModel : ObservableObject
{
    public FileEntry Entry { get; }

    /// <summary>True for the ".." row that navigates to the parent directory.</summary>
    public bool IsParentNav { get; }

    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Marked for operations (Insert/⌘-click). Independent of the cursor.</summary>
    [ObservableProperty]
    private bool _isMarked;

    [ObservableProperty]
    private string _editName;

    /// <summary>Per-file transfer badge ("done", "42%", "queued", "skipped"); empty when idle.</summary>
    [ObservableProperty]
    private string _transferStatus = "";

    [ObservableProperty]
    private string _transferStatusColor = "#a8a69c";

    public FileRowViewModel(FileEntry entry, bool isParentNav = false)
    {
        Entry = entry;
        IsParentNav = isParentNav;
        _editName = entry.Name;
    }

    public static FileRowViewModel ParentNav(string parentPath) => new(
        new FileEntry
        {
            Name = "..",
            FullPath = parentPath,
            IsDirectory = true,
            SizeBytes = -1,
            TypeLabel = "Up",
            ModifiedUtc = DateTime.UnixEpoch,
            UnixPermissions = "",
            AccessSummary = "",
        },
        isParentNav: true);

    public string Name => Entry.Name;
    public bool IsDirectory => Entry.IsDirectory;
    public string SizeText => FormatUtil.HumanSize(Entry.SizeBytes, Entry.IsDirectory);
    public string TypeText => Entry.TypeLabel;
    public string ModifiedText => IsParentNav ? "" : FormatUtil.DateLong(Entry.ModifiedUtc);
    public string PermsText => Entry.UnixPermissions;
    public string AccessText => Entry.AccessSummary;

    /// <summary>Last column: "RW" summary on Windows, rwx string on Unix (design 1a vs 1b/1c).</summary>
    public string AccessColText => OperatingSystem.IsWindows() ? AccessText : PermsText;
    public string MarkColor => Entry.IsDirectory ? "#c8992f" : "#b6b3a8";
    public string NameWeight => Entry.IsDirectory ? "Medium" : "Normal";
}

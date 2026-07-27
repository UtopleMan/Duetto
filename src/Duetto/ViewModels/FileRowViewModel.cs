using CommunityToolkit.Mvvm.ComponentModel;
using Duetto.Core.FileSystem;

namespace Duetto.ViewModels;

public partial class FileRowViewModel : ObservableObject
{
    public FileEntry Entry { get; }

    /// <summary>True for the ".." row that navigates to the parent directory.</summary>
    public bool IsParentNav { get; }

    /// <summary>
    /// True for the synthetic "new folder/file" row shown in edit mode before the entry
    /// exists on disk. Commit creates the real entry; cancel discards this row.
    /// </summary>
    public bool IsNewPlaceholder { get; }

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

    public FileRowViewModel(FileEntry entry, bool isParentNav = false, bool isNewPlaceholder = false)
    {
        Entry = entry;
        IsParentNav = isParentNav;
        IsNewPlaceholder = isNewPlaceholder;
        _editName = entry.Name;
    }

    /// <summary>
    /// A synthetic, editable row for a not-yet-created folder (<paramref name="isDirectory"/>
    /// true) or file. The suggested name seeds both the display and the edit box.
    /// </summary>
    public static FileRowViewModel NewPlaceholder(string parentPath, string suggestedName, bool isDirectory) => new(
        new FileEntry
        {
            Name = suggestedName,
            FullPath = Path.Combine(parentPath, suggestedName),
            IsDirectory = isDirectory,
            SizeBytes = isDirectory ? -1 : 0,
            TypeLabel = "",
            ModifiedUtc = DateTime.UnixEpoch,
            UnixPermissions = "",
            AccessSummary = "",
        },
        isNewPlaceholder: true)
    {
        EditName = suggestedName,
        IsEditing = true,
    };

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

using CommunityToolkit.Mvvm.ComponentModel;
using Duetto.Core.FileSystem;

namespace Duetto.ViewModels;

public partial class FileRowViewModel : ObservableObject
{
    public FileEntry Entry { get; }

    public bool IsParentNav { get; }

    public bool IsNewPlaceholder { get; }

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isMarked;

    [ObservableProperty]
    private string _editName;

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

    public string AccessColText => OperatingSystem.IsWindows() ? AccessText : PermsText;
    public string MarkColor => Entry.IsDirectory
        ? PaletteLookup.Hex("FolderMark", "#c8992f")
        : PaletteLookup.Hex("FileMark", "#b6b3a8");
    public string NameWeight => Entry.IsDirectory ? "Medium" : "Normal";
}

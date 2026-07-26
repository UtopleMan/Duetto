using CommunityToolkit.Mvvm.ComponentModel;
using Duet.Core.FileSystem;

namespace Duet.ViewModels;

public partial class FileRowViewModel : ObservableObject
{
    public FileEntry Entry { get; }

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName;

    /// <summary>Per-file transfer badge ("done", "42%", "queued", "skipped"); empty when idle.</summary>
    [ObservableProperty]
    private string _transferStatus = "";

    [ObservableProperty]
    private string _transferStatusColor = "#a8a69c";

    public FileRowViewModel(FileEntry entry)
    {
        Entry = entry;
        _editName = entry.Name;
    }

    public string Name => Entry.Name;
    public bool IsDirectory => Entry.IsDirectory;
    public string SizeText => FormatUtil.HumanSize(Entry.SizeBytes, Entry.IsDirectory);
    public string TypeText => Entry.TypeLabel;
    public string ModifiedText => FormatUtil.DateLong(Entry.ModifiedUtc);
    public string PermsText => Entry.UnixPermissions;
    public string AccessText => Entry.AccessSummary;

    /// <summary>Last column: "RW" summary on Windows, rwx string on Unix (design 1a vs 1b/1c).</summary>
    public string AccessColText => OperatingSystem.IsWindows() ? AccessText : PermsText;
    public string MarkColor => Entry.IsDirectory ? "#c8992f" : "#b6b3a8";
    public string NameWeight => Entry.IsDirectory ? "Medium" : "Normal";
}

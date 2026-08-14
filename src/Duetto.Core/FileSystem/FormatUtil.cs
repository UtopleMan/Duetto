using System.Globalization;

namespace Duetto.Core.FileSystem;

public static class FormatUtil
{
    public static string HumanSize(long bytes, bool isDirectory = false)
    {
        if (isDirectory || bytes < 0)
            return "—";
        if (bytes < 1024)
            return $"{bytes} B";

        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var text = value < 10
            ? value.ToString("0.#", CultureInfo.InvariantCulture)
            : Math.Round(value).ToString(CultureInfo.InvariantCulture);
        return $"{text} {units[unit]}";
    }

    public static string DateLong(DateTime utc) =>
        utc.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    public static string DateShort(DateTime utc) =>
        utc.ToLocalTime().ToString("dd MMM", CultureInfo.InvariantCulture);

    private static readonly Dictionary<string, string> TypeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "Text",
        [".gitignore"] = "Text",
        [".log"] = "Text",
        [".md"] = "Markdown",
        [".yml"] = "YAML",
        [".yaml"] = "YAML",
        [".json"] = "JSON",
        [".xml"] = "XML",
        [".cs"] = "C# Source",
        [".sln"] = "Solution",
        [".slnx"] = "Solution",
        [".csproj"] = "Project",
        [".axaml"] = "XAML",
        [".xaml"] = "XAML",
        [".png"] = "Image",
        [".jpg"] = "Image",
        [".jpeg"] = "Image",
        [".gif"] = "Image",
        [".webp"] = "Image",
        [".heic"] = "Image",
        [".mp4"] = "Video",
        [".mov"] = "Video",
        [".mkv"] = "Video",
        [".webm"] = "Video",
        [".mp3"] = "Audio",
        [".wav"] = "Audio",
        [".flac"] = "Audio",
        [".zip"] = "Archive",
        [".tar"] = "Archive",
        [".gz"] = "Archive",
        [".7z"] = "Archive",
        [".rar"] = "Archive",
        [".pdf"] = "PDF",
        [".doc"] = "Document",
        [".docx"] = "Document",
        [".xls"] = "Sheet",
        [".xlsx"] = "Sheet",
        [".csv"] = "Sheet",
        [".svg"] = "Vector",
        [".sh"] = "Script",
        [".py"] = "Python",
        [".js"] = "JavaScript",
        [".ts"] = "TypeScript",
        [".html"] = "HTML",
        [".css"] = "CSS",
    };

    public static string TypeLabel(string name, bool isDirectory)
    {
        if (isDirectory)
            return "Folder";
        var ext = Path.GetExtension(name);
        if (ext.Length == 0 && name.StartsWith('.'))
            ext = name;
        if (TypeByExtension.TryGetValue(ext, out var label))
            return label;
        return ext.Length > 1 ? ext[1..].ToUpperInvariant() : "File";
    }

    public static string UnixPermissions(UnixFileMode mode)
    {
        Span<char> c = stackalloc char[9];
        c[0] = mode.HasFlag(UnixFileMode.UserRead) ? 'r' : '-';
        c[1] = mode.HasFlag(UnixFileMode.UserWrite) ? 'w' : '-';
        c[2] = mode.HasFlag(UnixFileMode.UserExecute) ? 'x' : '-';
        c[3] = mode.HasFlag(UnixFileMode.GroupRead) ? 'r' : '-';
        c[4] = mode.HasFlag(UnixFileMode.GroupWrite) ? 'w' : '-';
        c[5] = mode.HasFlag(UnixFileMode.GroupExecute) ? 'x' : '-';
        c[6] = mode.HasFlag(UnixFileMode.OtherRead) ? 'r' : '-';
        c[7] = mode.HasFlag(UnixFileMode.OtherWrite) ? 'w' : '-';
        c[8] = mode.HasFlag(UnixFileMode.OtherExecute) ? 'x' : '-';
        return new string(c);
    }
}

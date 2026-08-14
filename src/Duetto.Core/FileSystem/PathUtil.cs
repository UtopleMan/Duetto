namespace Duetto.Core.FileSystem;

public sealed record RemoteAddress(string Scheme, string Id, string LocalPath);

public static class PathUtil
{
    private const string SchemeSeparator = "://";

    public static bool IsRemote(string path) => path.Contains(SchemeSeparator, StringComparison.Ordinal);

    public static RemoteAddress? ParseRemote(string path)
    {
        var schemeEnd = path.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (schemeEnd < 0)
            return null;

        var scheme = path[..schemeEnd];
        var rest = path[(schemeEnd + SchemeSeparator.Length)..];
        var slash = rest.IndexOf('/');
        if (slash < 0)
            return new RemoteAddress(scheme, rest, "/");

        var id = rest[..slash];
        var local = rest[slash..];
        return new RemoteAddress(scheme, id, local.Length == 0 ? "/" : local);
    }

    public static string Leaf(string path)
    {
        if (ParseRemote(path) is not { } r)
            return Path.GetFileName(path);
        var trimmed = r.LocalPath.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    public static string? Parent(string path)
    {
        if (ParseRemote(path) is not { } r)
            return Path.GetDirectoryName(path);

        var local = r.LocalPath.TrimEnd('/');
        if (local.Length == 0)
            return null;

        var slash = local.LastIndexOf('/');
        var parentLocal = slash <= 0 ? "/" : local[..slash];
        return Rebuild(r, parentLocal);
    }

    public static string Combine(string parent, string name)
    {
        if (ParseRemote(parent) is not { } r)
            return Path.Combine(parent, name);

        var local = r.LocalPath.TrimEnd('/');
        var childLocal = local.Length == 0 ? "/" + name : local + "/" + name;
        return Rebuild(r, childLocal);
    }

    public static string ToAddress(string panePath, string rowPath) =>
        ParseRemote(panePath) is { } r
            ? $"{r.Scheme}://{r.Id}{rowPath}"
            : rowPath;

    private static string Rebuild(RemoteAddress r, string localPath) =>
        localPath == "/"
            ? $"{r.Scheme}://{r.Id}/"
            : $"{r.Scheme}://{r.Id}{localPath}";
}

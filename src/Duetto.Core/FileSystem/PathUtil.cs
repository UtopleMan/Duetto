namespace Duetto.Core.FileSystem;

/// <summary>A parsed remote address: <c>scheme://id/localPath</c>.</summary>
public sealed record RemoteAddress(string Scheme, string Id, string LocalPath);

/// <summary>
/// Path helpers that work for both local paths and remote <c>scheme://id/path</c>
/// addresses, so navigation (parent/leaf/combine) is provider-agnostic. Remote
/// addresses always use '/'; local paths delegate to <see cref="Path"/>.
/// </summary>
public static class PathUtil
{
    private const string SchemeSeparator = "://";

    public static bool IsRemote(string path) => path.Contains(SchemeSeparator, StringComparison.Ordinal);

    /// <summary>Parses a remote address, or returns null for a local path.</summary>
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

    /// <summary>The last path segment (file or directory name); "" at a remote root.</summary>
    public static string Leaf(string path)
    {
        if (ParseRemote(path) is not { } r)
            return Path.GetFileName(path);
        var trimmed = r.LocalPath.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    /// <summary>The parent address, or null at a root (local or remote).</summary>
    public static string? Parent(string path)
    {
        if (ParseRemote(path) is not { } r)
            return Path.GetDirectoryName(path);

        var local = r.LocalPath.TrimEnd('/');
        if (local.Length == 0)
            return null; // already at the remote root

        var slash = local.LastIndexOf('/');
        var parentLocal = slash <= 0 ? "/" : local[..slash];
        return Rebuild(r, parentLocal);
    }

    /// <summary>Joins <paramref name="name"/> onto <paramref name="parent"/> with the right separator.</summary>
    public static string Combine(string parent, string name)
    {
        if (ParseRemote(parent) is not { } r)
            return Path.Combine(parent, name);

        var local = r.LocalPath.TrimEnd('/');
        var childLocal = local.Length == 0 ? "/" + name : local + "/" + name;
        return Rebuild(r, childLocal);
    }

    private static string Rebuild(RemoteAddress r, string localPath) =>
        localPath == "/"
            ? $"{r.Scheme}://{r.Id}/"
            : $"{r.Scheme}://{r.Id}{localPath}";
}

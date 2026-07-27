namespace Duetto.Core.FileSystem;

/// <summary>
/// Maps a path to the provider that owns it. Local paths resolve to the shared
/// <see cref="LocalFileSystemProvider"/>; a <c>scheme://id/…</c> address resolves to the
/// provider registered for that scheme+id (an SFTP connection, later S3/SMB), together
/// with the provider-local path to hand it.
/// </summary>
public sealed class FileSystemRegistry
{
    private readonly IFileSystemProvider _local = new LocalFileSystemProvider();
    private readonly Dictionary<string, IFileSystemProvider> _remote = [];

    private static string Key(string scheme, string id) => $"{scheme}://{id}";

    public void Register(string scheme, string id, IFileSystemProvider provider) =>
        _remote[Key(scheme, id)] = provider;

    public void Unregister(string scheme, string id) => _remote.Remove(Key(scheme, id));

    public (IFileSystemProvider Provider, string LocalPath) Resolve(string path)
    {
        if (PathUtil.ParseRemote(path) is not { } address)
            return (_local, path);

        if (_remote.TryGetValue(Key(address.Scheme, address.Id), out var provider))
            return (provider, address.LocalPath);

        throw new InvalidOperationException($"No file-system provider registered for {address.Scheme}://{address.Id}");
    }

    /// <summary>The provider owning <paramref name="path"/> (ignores the resolved local path).</summary>
    public IFileSystemProvider ProviderFor(string path) => Resolve(path).Provider;
}

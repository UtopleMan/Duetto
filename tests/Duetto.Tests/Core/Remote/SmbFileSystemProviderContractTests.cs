using Duetto.Core.FileSystem;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class SmbFileSystemProviderContractTests : FileSystemProviderContract, IDisposable
{
    private readonly FakeSmbClientAdapter adapter;
    private readonly SmbConnection conn;
    private readonly SmbFileSystemProvider provider;

    public SmbFileSystemProviderContractTests()
    {
        adapter = new FakeSmbClientAdapter();

        adapter.CreateDirectory("/Shared");

        conn = new SmbConnection(
            new SmbConnectionInfo("id", "Test", "fake.local"),
            ConnectSecret.FromPassword("pw"),
            new FakeSmbFactory(adapter));
        provider = new SmbFileSystemProvider(conn);
    }

    protected override IFileSystemProvider Provider => provider;

    protected override string Root => "/Shared";

    public void Dispose()
    {
        provider.Dispose();
        conn.Dispose();
    }

    [Fact]
    public void Root_lists_shares_as_directories()
    {
        adapter.CreateDirectory("/Other");

        var listed = provider.List("/");

        Assert.Contains(listed, e => e is { Name: "Shared", IsDirectory: true });
        Assert.Contains(listed, e => e is { Name: "Other", IsDirectory: true });
    }

    [Fact]
    public void Dot_and_dotdot_entries_are_filtered_from_List_and_EnumerateRecursive()
    {
        var sub = provider.CreateDirectory(Root, "dottest");
        provider.CreateFile(sub, "child.txt");

        var listed = provider.List(sub);
        Assert.DoesNotContain(listed, e => e.Name is "." or "..");
        Assert.Contains(listed, e => e.Name == "child.txt");

        var walked = provider.EnumerateRecursive(Root).ToList();
        Assert.DoesNotContain(walked, e => e.Name is "." or "..");
    }

    [Fact]
    public void ReadOnly_attribute_maps_to_R_access_summary()
    {
        var file = provider.CreateFile(Root, "ro.txt");
        adapter.MarkReadOnly(file, readOnly: true);

        var entry = provider.Stat(file);

        Assert.NotNull(entry);
        Assert.Equal("R", entry.AccessSummary);
        Assert.Equal("", entry.UnixPermissions);
    }

    [Fact]
    public void Mtime_roundtrips_via_SetLastWriteTimeUtc()
    {
        var file = provider.CreateFile(Root, "mtime.txt");
        var t = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        provider.SetLastWriteTimeUtc(file, t);
        var entry = provider.Stat(file);

        Assert.NotNull(entry);
        Assert.Equal(t, entry.ModifiedUtc);
    }

    [Fact]
    public void Delete_recurses_into_nested_directories()
    {
        var dir = provider.CreateDirectory(Root, "nest");
        var inner = provider.CreateDirectory(dir, "inner");
        provider.CreateFile(inner, "file.txt");
        provider.CreateFile(dir, "top.txt");

        provider.Delete(dir, toTrash: false);

        Assert.False(provider.DirectoryExists(dir));
        Assert.False(provider.FileExists(dir + "/top.txt"));
        Assert.False(provider.DirectoryExists(inner));
    }

    [Fact]
    public void List_auto_connects_and_reconnects_once_on_drop()
    {
        provider.CreateFile(Root, "ping.txt");
        Assert.Equal(1, adapter.ConnectCount);

        adapter.NextListThrow = new SmbConnectionException("dropped mid-list");
        var entries = provider.List(Root);

        Assert.Contains(entries, e => e.Name == "ping.txt");
        Assert.Equal(2, adapter.ConnectCount);
    }

    [Fact]
    public void EnumerateRecursive_skips_directory_that_throws_IOException_and_continues()
    {
        var okDir = provider.CreateDirectory(Root, "ok");
        provider.CreateFile(okDir, "visible.txt");
        var badDir = provider.CreateDirectory(Root, "bad");
        provider.CreateFile(badDir, "hidden.txt");

        adapter.ListThrowsByPath[badDir] = new IOException("SMB protocol error");

        var names = provider.EnumerateRecursive(Root).Select(e => e.Name).ToList();

        Assert.Contains("ok", names);
        Assert.Contains("visible.txt", names);
        Assert.Contains("bad", names);
        Assert.DoesNotContain("hidden.txt", names);
    }

    [Fact]
    public void EnumerateRecursive_propagates_authentication_failure_from_subdirectory()
    {
        var okDir = provider.CreateDirectory(Root, "good");
        provider.CreateFile(okDir, "file.txt");
        var authFailDir = provider.CreateDirectory(Root, "authfail");

        adapter.ListThrowsByPath[authFailDir] = new SmbAuthenticationException("Authentication failed");

        Assert.Throws<SmbAuthenticationException>(
            () => provider.EnumerateRecursive(Root).ToList());
    }
}

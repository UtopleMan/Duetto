using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Renci.SshNet.Common;
using DuettoConnectionInfo = Duetto.Core.Remote.ConnectionInfo;

namespace Duetto.Tests.Core.Remote;

public sealed class SftpFileSystemProviderContractTests : FileSystemProviderContract, IDisposable
{
    private readonly FakeSftpClientAdapter _adapter;
    private readonly SftpConnection _conn;
    private readonly SftpFileSystemProvider _provider;

    public SftpFileSystemProviderContractTests()
    {
        _adapter = new FakeSftpClientAdapter();
        _adapter.CreateDirectory(Root);

        var factory = new FakeSftpFactory(_adapter);
        var info = new DuettoConnectionInfo("test", "Test", "fake.local");
        var secret = ConnectSecret.FromPassword("pw");
        _conn = new SftpConnection(info, secret, factory);
        _provider = new SftpFileSystemProvider(_conn);
    }

    protected override IFileSystemProvider Provider => _provider;

    protected override string Root => "/test";

    public void Dispose()
    {
        _provider.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public void Attrs_permissions_are_mapped_from_entry()
    {
        var file = _provider.CreateFile(Root, "perm.txt");
        var entry = _provider.Stat(file);
        Assert.NotNull(entry);
        Assert.Equal("rw-r--r--", entry.UnixPermissions);
        Assert.Equal("RW", entry.AccessSummary);
    }

    [Fact]
    public void Attrs_mtime_is_preserved_via_SetLastWriteTimeUtc()
    {
        var file = _provider.CreateFile(Root, "mtime.txt");
        var t = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        _provider.SetLastWriteTimeUtc(file, t);
        var entry = _provider.Stat(file);
        Assert.NotNull(entry);
        Assert.Equal(t, entry.ModifiedUtc);
    }

    [Fact]
    public void Dot_and_dotdot_entries_are_filtered_from_List_and_EnumerateRecursive()
    {
        var sub = _provider.CreateDirectory(Root, "dottest");
        _provider.CreateFile(sub, "child.txt");

        var listed = _provider.List(sub);
        Assert.DoesNotContain(listed, e => e.Name is "." or "..");

        var walked = _provider.EnumerateRecursive(Root).ToList();
        Assert.DoesNotContain(walked, e => e.Name is "." or "..");
    }

    [Fact]
    public void Delete_recurses_into_nested_directories()
    {
        var dir = _provider.CreateDirectory(Root, "nest");
        var inner = _provider.CreateDirectory(dir, "inner");
        _provider.CreateFile(inner, "file.txt");
        _provider.CreateFile(dir, "top.txt");

        _provider.Delete(dir, toTrash: false);

        Assert.False(_provider.DirectoryExists(dir));
        Assert.False(_provider.FileExists(dir + "/top.txt"));
        Assert.False(_provider.DirectoryExists(inner));
        Assert.False(_provider.FileExists(inner + "/file.txt"));
    }

    [Fact]
    public void List_auto_connects_after_explicit_disconnect()
    {
        _provider.CreateFile(Root, "ping.txt");

        _conn.Disconnect();
        var entries = _provider.List(Root);
        Assert.Contains(entries, e => e.Name == "ping.txt");
    }

    [Fact]
    public void Reconnect_once_on_SshConnectionException_then_retries()
    {
        _provider.CreateFile(Root, "ping.txt");
        Assert.Equal(1, _adapter.ConnectCount);

        _adapter.NextListThrow = new SshConnectionException("dropped mid-list");

        var entries = _provider.List(Root);

        Assert.Contains(entries, e => e.Name == "ping.txt");
        Assert.Equal(2, _adapter.ConnectCount);
    }

    [Fact]
    public void EnumerateRecursive_skips_directory_that_throws_SshException_and_continues()
    {
        var okDir = _provider.CreateDirectory(Root, "ok");
        _provider.CreateFile(okDir, "visible.txt");
        var badDir = _provider.CreateDirectory(Root, "bad");
        _provider.CreateFile(badDir, "hidden.txt");

        _adapter.ListThrowsByPath[badDir] = new SshException("SFTP protocol error");

        var names = _provider.EnumerateRecursive(Root).Select(e => e.Name).ToList();

        Assert.Contains("ok", names);
        Assert.Contains("visible.txt", names);
        Assert.Contains("bad", names);
        Assert.DoesNotContain("hidden.txt", names);
    }

    [Fact]
    public void EnumerateRecursive_propagates_SshAuthenticationException_from_subdirectory()
    {
        var okDir = _provider.CreateDirectory(Root, "good");
        _provider.CreateFile(okDir, "file.txt");
        var authFailDir = _provider.CreateDirectory(Root, "authfail");

        _adapter.ListThrowsByPath[authFailDir] =
            new SshAuthenticationException("Authentication failed");

        Assert.Throws<SshAuthenticationException>(
            () => _provider.EnumerateRecursive(Root).ToList());
    }
}

internal sealed class FakeSftpFactory : ISftpClientFactory
{
    private readonly FakeSftpClientAdapter _adapter;
    public FakeSftpFactory(FakeSftpClientAdapter adapter) => _adapter = adapter;

    public ISftpClientAdapter Create(DuettoConnectionInfo info, ConnectSecret secret) => _adapter;
}

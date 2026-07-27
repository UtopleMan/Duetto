using Duetto.Core.FileSystem;

namespace Duetto.Tests.Core;

public class PathUtilTests
{
    [Fact]
    public void IsRemote_distinguishes_scheme_addresses_from_local_paths()
    {
        Assert.True(PathUtil.IsRemote("sftp://conn1/home/user"));
        Assert.False(PathUtil.IsRemote("/home/user"));
        Assert.False(PathUtil.IsRemote(@"C:\Users\me"));
    }

    [Fact]
    public void ParseRemote_splits_scheme_id_and_local_path()
    {
        var r = PathUtil.ParseRemote("sftp://conn1/home/user/docs");
        Assert.NotNull(r);
        Assert.Equal("sftp", r!.Scheme);
        Assert.Equal("conn1", r.Id);
        Assert.Equal("/home/user/docs", r.LocalPath);
    }

    [Fact]
    public void ParseRemote_treats_bare_and_slash_host_as_root()
    {
        Assert.Equal("/", PathUtil.ParseRemote("sftp://conn1")!.LocalPath);
        Assert.Equal("/", PathUtil.ParseRemote("sftp://conn1/")!.LocalPath);
    }

    [Fact]
    public void ParseRemote_returns_null_for_a_local_path() =>
        Assert.Null(PathUtil.ParseRemote("/home/user"));

    [Fact]
    public void Leaf_returns_the_last_segment()
    {
        Assert.Equal("b.txt", PathUtil.Leaf("sftp://conn1/a/b.txt"));
        Assert.Equal("", PathUtil.Leaf("sftp://conn1/"));
    }

    [Fact]
    public void Parent_walks_up_the_remote_tree_and_stops_at_root()
    {
        Assert.Equal("sftp://conn1/a", PathUtil.Parent("sftp://conn1/a/b"));
        Assert.Equal("sftp://conn1/", PathUtil.Parent("sftp://conn1/a"));
        Assert.Null(PathUtil.Parent("sftp://conn1/"));
    }

    [Fact]
    public void Combine_joins_with_the_remote_separator()
    {
        Assert.Equal("sftp://conn1/a/b", PathUtil.Combine("sftp://conn1/a", "b"));
        Assert.Equal("sftp://conn1/b", PathUtil.Combine("sftp://conn1/", "b"));
    }

    [Fact]
    public void Local_paths_delegate_to_system_path()
    {
        var dir = Path.Combine("x", "y");
        Assert.Equal("y", PathUtil.Leaf(Path.Combine("x", "y")));
        Assert.Equal("x", PathUtil.Leaf(PathUtil.Parent(dir)!));
        Assert.Equal(Path.Combine("x", "y"), PathUtil.Combine("x", "y"));
    }
}

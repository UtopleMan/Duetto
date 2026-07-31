using Duetto.Core.State;
using Duetto.Tests.Core;
using Duetto.ViewModels;
using Xunit;

namespace Duetto.Tests.Ui;

public class ResolveStartupPathsTests
{
    private const string Home = "/home/user";

    [Fact]
    public void No_arg_no_session_uses_home_for_both()
    {
        var (left, right) = MainViewModel.ResolveStartupPaths(folderArg: null, saved: null, home: Home);
        Assert.Equal(Home, left);
        Assert.Equal(Home, right);
    }

    [Fact]
    public void No_arg_restores_both_saved_dirs()
    {
        using var l = new TempDir();
        using var r = new TempDir();
        var (left, right) = MainViewModel.ResolveStartupPaths(null, new SessionState(l.Path, r.Path), Home);
        Assert.Equal(l.Path, left);
        Assert.Equal(r.Path, right);
    }

    [Fact]
    public void Arg_wins_for_left_right_restores_saved()
    {
        using var r = new TempDir();
        var (left, right) = MainViewModel.ResolveStartupPaths("/opt/arg", new SessionState("/whatever", r.Path), Home);
        Assert.Equal("/opt/arg", left);
        Assert.Equal(r.Path, right);
    }

    [Fact]
    public void Missing_saved_left_falls_back_to_home()
    {
        using var r = new TempDir();
        var saved = new SessionState("/does/not/exist", r.Path);
        var (left, right) = MainViewModel.ResolveStartupPaths(null, saved, Home);
        Assert.Equal(Home, left);
        Assert.Equal(r.Path, right);
    }

    [Fact]
    public void Remote_saved_path_falls_back_to_home()
    {
        var saved = new SessionState("sftp://conn/home/user", "sftp://conn/var");
        var (left, right) = MainViewModel.ResolveStartupPaths(null, saved, Home);
        Assert.Equal(Home, left);
        Assert.Equal(Home, right);
    }
}

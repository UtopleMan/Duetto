using Duetto;
using Duetto.Tests.Core;
using Xunit;

namespace Duetto.Tests;

public class AppOptionsTests
{
    [Fact]
    public void Positional_valid_dir_sets_absolute_folder()
    {
        using var tmp = new TempDir();
        var opts = AppOptions.Parse([tmp.Path]);
        Assert.Equal(tmp.Path, opts.Folder);
    }

    [Fact]
    public void Positional_relative_dir_resolves_against_cwd()
    {
        using var tmp = new TempDir();
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, tmp.Path);
        var opts = AppOptions.Parse([relative]);
        Assert.Equal(tmp.Path, opts.Folder);
    }

    [Fact]
    public void Missing_dir_falls_back_to_null()
    {
        var missing = Path.Combine(Path.GetTempPath(), "duetto-nope-" + Guid.NewGuid().ToString("N"));
        var opts = AppOptions.Parse([missing]);
        Assert.Null(opts.Folder);
    }

    [Fact]
    public void File_path_is_not_a_folder()
    {
        using var tmp = new TempDir();
        var file = tmp.File("f.txt");
        var opts = AppOptions.Parse([file]);
        Assert.Null(opts.Folder);
    }

    [Fact]
    public void Garbage_path_falls_back_to_null()
    {
        var opts = AppOptions.Parse(["\0not/a/real/path"]);
        Assert.Null(opts.Folder);
    }

    [Fact]
    public void Chrome_flag_value_is_not_taken_as_folder()
    {
        using var tmp = new TempDir();
        var opts = AppOptions.Parse(["--chrome", "win", tmp.Path]);
        Assert.Equal(ChromeKind.Win, opts.Chrome);
        Assert.Equal(tmp.Path, opts.Folder);
    }

    [Fact]
    public void Only_first_positional_is_used()
    {
        using var tmp = new TempDir();
        var first = tmp.Dir("a");
        tmp.Dir("b");
        var second = Path.Combine(tmp.Path, "b");
        var opts = AppOptions.Parse([first, second]);
        Assert.Equal(first, opts.Folder);
    }

    [Fact]
    public void No_positional_leaves_folder_null()
    {
        var opts = AppOptions.Parse(["--smoke"]);
        Assert.True(opts.Smoke);
        Assert.Null(opts.Folder);
    }

    [Fact]
    public void Unknown_flag_is_not_taken_as_folder()
    {
        var opts = AppOptions.Parse(["--totally-unknown"]);
        Assert.Null(opts.Folder);
    }
}

using Duetto.Core.FileSystem;

namespace Duetto.Tests.Core;

public class FormatUtilTests
{
    [Theory]
    [InlineData(412, "412 B")]
    [InlineData(1843, "1.8 KB")]
    [InlineData(9421, "9.2 KB")]
    [InlineData(14336, "14 KB")]
    [InlineData(6144, "6 KB")]
    [InlineData(1258291, "1.2 MB")]
    [InlineData(4509715661, "4.2 GB")]
    public void HumanSize_matches_design_samples(long bytes, string expected) =>
        Assert.Equal(expected, FormatUtil.HumanSize(bytes));

    [Fact]
    public void HumanSize_directory_is_dash() =>
        Assert.Equal("—", FormatUtil.HumanSize(-1, isDirectory: true));

    [Theory]
    [InlineData("readme.md", false, "Markdown")]
    [InlineData("Program.cs", false, "C# Source")]
    [InlineData("app.axaml", false, "XAML")]
    [InlineData("movie.MOV", false, "Video")]
    [InlineData("archive.zip", false, "Archive")]
    [InlineData("whatever.xyz", false, "XYZ")]
    [InlineData("Makefile", false, "File")]
    [InlineData("src", true, "Folder")]
    public void TypeLabel_maps_extensions(string name, bool isDir, string expected) =>
        Assert.Equal(expected, FormatUtil.TypeLabel(name, isDir));

    [Fact]
    public void UnixPermissions_formats_rwx()
    {
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                   UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                   UnixFileMode.OtherRead;
        Assert.Equal("rwxr-xr--", FormatUtil.UnixPermissions(mode));
    }
}

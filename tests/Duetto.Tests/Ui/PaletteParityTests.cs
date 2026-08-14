using System.Text.RegularExpressions;

namespace Duetto.Tests.Ui;

public class PaletteParityTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Duetto.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root (Duetto.slnx) not found");
    }

    private static HashSet<string> Keys(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));
        return Regex.Matches(text, "x:Key=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
    }

    [Fact]
    public void Light_and_Dark_palettes_define_the_same_brush_keys()
    {
        var light = Keys("src/Duetto/Themes/Palette.Light.axaml");
        var dark = Keys("src/Duetto/Themes/Palette.Dark.axaml");

        Assert.NotEmpty(light);
        Assert.Equal(light, dark);
    }
}

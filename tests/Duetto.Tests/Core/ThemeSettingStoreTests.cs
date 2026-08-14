using Duetto.Core.State;

namespace Duetto.Tests.Core;

public class ThemeSettingStoreTests
{
    private static ThemeSettingStore InMemory(string? seed = null)
    {
        var disk = new Dictionary<string, string>();
        if (seed is not null)
            disk["settings.json"] = seed;
        return new ThemeSettingStore("settings.json",
            p => disk.TryGetValue(p, out var v) ? v : null,
            (p, c) => disk[p] = c);
    }

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.System)]
    public void Save_then_Load_round_trips(AppTheme theme)
    {
        var store = InMemory();
        store.Save(theme);
        Assert.Equal(theme, store.Load());
    }

    [Fact]
    public void Missing_file_defaults_to_System()
    {
        Assert.Equal(AppTheme.System, InMemory().Load());
    }

    [Fact]
    public void Corrupt_json_defaults_to_System()
    {
        Assert.Equal(AppTheme.System, InMemory(seed: "{ not json").Load());
    }

    [Fact]
    public void Unknown_theme_value_defaults_to_System()
    {
        Assert.Equal(AppTheme.System, InMemory(seed: "{\"theme\":\"Sepia\"}").Load());
    }
}

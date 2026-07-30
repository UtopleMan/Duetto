using Duetto.Core.State;
using Xunit;

namespace Duetto.Tests.Core;

public class WindowPlacementStoreTests
{
    private static WindowPlacementStore InMemory(string? initial, out Func<string?> read)
    {
        string? content = initial;
        read = () => content;
        return new WindowPlacementStore("mem", _ => content, (_, c) => content = c);
    }

    [Fact]
    public void Load_returns_null_when_file_missing()
    {
        var store = InMemory(null, out _);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = InMemory(null, out _);
        var placement = new WindowPlacement(120, 80, 1024, 768, Maximized: true);

        store.Save(placement);

        Assert.Equal(placement, store.Load());
    }

    [Fact]
    public void Load_returns_null_on_corrupt_json()
    {
        var store = InMemory("{ not valid json", out _);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_returns_null_on_empty_content()
    {
        var store = InMemory("   ", out _);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_returns_null_when_reader_throws_io()
    {
        var store = new WindowPlacementStore(
            "mem",
            _ => throw new IOException("locked"),
            (_, _) => { });
        Assert.Null(store.Load());
    }
}

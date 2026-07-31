using Duetto.Core.Remote;
using Duetto.Core.State;
using Xunit;

namespace Duetto.Tests.Core;

public class SessionStoreTests
{
    private sealed class Box { public string? Value; }

    private static SessionStore InMemory(string? initial)
    {
        var box = new Box { Value = initial };
        return new SessionStore("mem", _ => box.Value, (_, c) => box.Value = c);
    }

    [Fact]
    public void Load_returns_null_when_missing()
    {
        Assert.Null(InMemory(null).Load());
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = InMemory(null);
        var state = new SessionState("/left/dir", "/right/dir");

        store.Save(state);

        Assert.Equal(state, store.Load());
    }

    [Fact]
    public void Load_returns_null_on_corrupt_json()
    {
        Assert.Null(InMemory("{ broken").Load());
    }

    [Fact]
    public void Load_returns_null_on_empty()
    {
        Assert.Null(InMemory("   ").Load());
    }

    [Fact]
    public void Load_returns_null_when_reader_throws_io()
    {
        var store = new SessionStore("mem", _ => throw new IOException("locked"), (_, _) => { });
        Assert.Null(store.Load());
    }

    [Fact]
    public void SessionJsonPath_is_session_json_in_config_dir()
    {
        Assert.Equal(Path.Combine(AppPaths.ConfigDir, "session.json"), AppPaths.SessionJsonPath);
    }
}

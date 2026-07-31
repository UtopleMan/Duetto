using System.Text.Json;

namespace Duetto.Core.State;

/// <summary>
/// Persists the <see cref="SessionState"/> (the two pane directories) as JSON. Mirrors
/// <c>WindowPlacementStore</c>: the production constructor uses the real filesystem with an
/// atomic temp-then-move write; the injected constructor takes reader/writer delegates for
/// unit tests. <see cref="Load"/> never throws — a missing, empty, or corrupt file yields
/// <see langword="null"/> so the caller falls back to default folders.
/// </summary>
public sealed class SessionStore
{
    private readonly string _path;
    private readonly Func<string, string?> _reader;
    private readonly Action<string, string> _writer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Creates a store backed by the real filesystem at <paramref name="path"/>.</summary>
    public SessionStore(string path)
        : this(path,
               p => File.Exists(p) ? File.ReadAllText(p) : null,
               (p, content) =>
               {
                   var tmp = p + ".tmp";
                   File.WriteAllText(tmp, content);
                   File.Move(tmp, p, overwrite: true);
               })
    { }

    /// <summary>Creates a store with injected IO — intended for unit tests.</summary>
    public SessionStore(string path, Func<string, string?> reader, Action<string, string> writer)
    {
        _path = path;
        _reader = reader;
        _writer = writer;
    }

    /// <summary>
    /// Loads the saved session, or <see langword="null"/> when the file is missing, empty,
    /// or corrupt. Never throws.
    /// </summary>
    public SessionState? Load()
    {
        try
        {
            var content = _reader(_path);
            if (string.IsNullOrWhiteSpace(content))
                return null;

            return JsonSerializer.Deserialize<SessionState>(content, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Saves <paramref name="state"/>, overwriting any existing file.</summary>
    public void Save(SessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var content = JsonSerializer.Serialize(state, JsonOptions);
        _writer(_path, content);
    }
}

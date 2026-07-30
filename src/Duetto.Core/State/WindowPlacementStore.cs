using System.Text.Json;

namespace Duetto.Core.State;

/// <summary>
/// Persists the main window's <see cref="WindowPlacement"/> as JSON. Mirrors
/// <c>ConnectionStore</c>: the production constructor uses the real filesystem with an
/// atomic temp-then-move write; the injected constructor takes reader/writer delegates for
/// unit tests. <see cref="Load"/> never throws — a missing, empty, or corrupt file yields
/// <see langword="null"/> so the caller falls back to a default placement.
/// </summary>
public sealed class WindowPlacementStore
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
    public WindowPlacementStore(string path)
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
    /// <param name="path">Logical path passed to <paramref name="reader"/> and <paramref name="writer"/>.</param>
    /// <param name="reader">Returns the file content, or <see langword="null"/> when the file does not exist.</param>
    /// <param name="writer">Writes the file content (temp + move logic belongs here for production use).</param>
    public WindowPlacementStore(string path, Func<string, string?> reader, Action<string, string> writer)
    {
        _path = path;
        _reader = reader;
        _writer = writer;
    }

    /// <summary>
    /// Loads the saved placement, or <see langword="null"/> when the file is missing, empty,
    /// or corrupt. Never throws.
    /// </summary>
    public WindowPlacement? Load()
    {
        try
        {
            var content = _reader(_path);
            if (string.IsNullOrWhiteSpace(content))
                return null;

            return JsonSerializer.Deserialize<WindowPlacement>(content, JsonOptions);
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

    /// <summary>Saves <paramref name="placement"/>, overwriting any existing file.</summary>
    public void Save(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var content = JsonSerializer.Serialize(placement, JsonOptions);
        _writer(_path, content);
    }
}

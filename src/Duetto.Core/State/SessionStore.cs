using System.Text.Json;

namespace Duetto.Core.State;

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

    public SessionStore(string path)
        : this(path,
               p => File.Exists(p) ? File.ReadAllText(p) : null,
               (p, content) =>
               {
                   // Atomic temp-then-move so a crash mid-write cannot leave a torn file.
                   var tmp = p + ".tmp";
                   File.WriteAllText(tmp, content);
                   File.Move(tmp, p, overwrite: true);
               })
    { }

    public SessionStore(string path, Func<string, string?> reader, Action<string, string> writer)
    {
        _path = path;
        _reader = reader;
        _writer = writer;
    }

    // Null when the file is missing, empty, or corrupt — never throws, so the caller falls
    // back to default folders instead of crashing on a mangled file.
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

    public void Save(SessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var content = JsonSerializer.Serialize(state, JsonOptions);
        _writer(_path, content);
    }
}

using System.Text.Json;

namespace Duetto.Core.Remote;

// A missing or corrupt file loads as an empty dictionary — never throws. Saves write a
// .tmp sibling first, then File.Move over the target, to prevent torn files.
public sealed class JsonHostKeyPersistence : IHostKeyPersistence
{
    private readonly string _path;
    private readonly Func<string, string?> _reader;
    private readonly Action<string, string> _writer;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public JsonHostKeyPersistence(string path)
        : this(path,
               p => File.Exists(p) ? File.ReadAllText(p) : null,
               (p, content) =>
               {
                   var tmp = p + ".tmp";
                   File.WriteAllText(tmp, content);
                   File.Move(tmp, p, overwrite: true);
               })
    { }

    public JsonHostKeyPersistence(string path, Func<string, string?> reader, Action<string, string> writer)
    {
        _path = path;
        _reader = reader;
        _writer = writer;
    }

    public IReadOnlyDictionary<string, string> LoadAll()
    {
        try
        {
            var content = _reader(_path);
            if (string.IsNullOrWhiteSpace(content))
                return new Dictionary<string, string>();

            return JsonSerializer.Deserialize<Dictionary<string, string>>(content, JsonOptions)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
        catch (IOException)
        {
            return new Dictionary<string, string>();
        }
    }

    public void Save(string storeKey, string fingerprint)
    {
        var pins = new Dictionary<string, string>(LoadAll()) { [storeKey] = fingerprint };
        Flush(pins);
    }

    public void Remove(string storeKey)
    {
        var pins = new Dictionary<string, string>(LoadAll());
        if (pins.Remove(storeKey))
            Flush(pins);
    }

    private void Flush(Dictionary<string, string> pins)
    {
        var content = JsonSerializer.Serialize(pins, JsonOptions);
        _writer(_path, content);
    }

    public static JsonHostKeyPersistence Attach(string hostKeysJsonPath) =>
        new(hostKeysJsonPath);
}

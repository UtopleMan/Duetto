using System.Text.Json;

namespace Duetto.Core.Remote;

/// <summary>
/// <see cref="IHostKeyPersistence"/> implementation that reads and writes host-key pins
/// as a JSON object in <c>hostkeys.json</c>.
///
/// <para>
/// The JSON format is a flat object whose keys are OpenSSH-style store keys
/// (<c>"algo:[host]:port"</c>, produced by <see cref="HostKeyStore.MakeStoreKey"/>) and
/// whose values are the SHA-256 fingerprint strings:
/// </para>
/// <code>
/// {
///   "ssh-ed25519:[example.com]:22": "ohD8VZEXGWo6Ez8GSEJQ9WpafgLFsOfLOtGGQCQo6Og",
///   "ssh-ed25519:[192.168.1.1]:2222": "mBc9XkQ2rT7wLpZa4VuHnE5yD8fGiJ1oKsM6NqR3SxY"
/// }
/// </code>
///
/// <para>
/// <b>Resilience:</b> a missing or corrupt file loads as an empty dictionary — never throws.
/// </para>
///
/// <para>
/// <b>Atomic writes:</b> saves write to a <c>.tmp</c> sibling first, then overwrite the
/// target with <see cref="File.Move"/> to prevent torn files.
/// </para>
///
/// <para>
/// <b>Wire-up:</b> Phase 4 can attach persistence to a <see cref="HostKeyStore"/> via
/// <see cref="Attach(AppPaths)"/> or <see cref="Attach(string)"/>:
/// </para>
/// <code>
/// var store = new HostKeyStore(JsonHostKeyPersistence.Attach(AppPaths.HostKeysJsonPath));
/// </code>
/// </summary>
public sealed class JsonHostKeyPersistence : IHostKeyPersistence
{
    private readonly string _path;
    private readonly Func<string, string?> _reader;
    private readonly Action<string, string> _writer;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Creates a <see cref="JsonHostKeyPersistence"/> backed by the real filesystem at
    /// <paramref name="path"/>.
    /// </summary>
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

    /// <summary>
    /// Creates a <see cref="JsonHostKeyPersistence"/> with injected IO — intended for unit tests.
    /// </summary>
    public JsonHostKeyPersistence(string path, Func<string, string?> reader, Action<string, string> writer)
    {
        _path = path;
        _reader = reader;
        _writer = writer;
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void Save(string storeKey, string fingerprint)
    {
        var pins = new Dictionary<string, string>(LoadAll()) { [storeKey] = fingerprint };
        Flush(pins);
    }

    /// <inheritdoc/>
    public void Remove(string storeKey)
    {
        var pins = new Dictionary<string, string>(LoadAll());
        if (pins.Remove(storeKey))
            Flush(pins);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void Flush(Dictionary<string, string> pins)
    {
        var content = JsonSerializer.Serialize(pins, JsonOptions);
        _writer(_path, content);
    }

    // ── attach factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Convenience factory: creates a <see cref="JsonHostKeyPersistence"/> pointing at
    /// <paramref name="hostKeysJsonPath"/> (typically <see cref="AppPaths.HostKeysJsonPath"/>).
    ///
    /// <para>Phase 4 usage:</para>
    /// <code>
    /// var store = new HostKeyStore(JsonHostKeyPersistence.Attach(AppPaths.HostKeysJsonPath));
    /// </code>
    /// </summary>
    public static JsonHostKeyPersistence Attach(string hostKeysJsonPath) =>
        new(hostKeysJsonPath);
}

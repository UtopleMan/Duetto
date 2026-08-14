using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duetto.Core.Remote;

public sealed record StoredConnection
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; init; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; init; } = 22;

    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    [JsonPropertyName("authMode")]
    public AuthMode AuthMode { get; init; } = AuthMode.Password;

    [JsonPropertyName("keyPath")]
    public string? KeyPath { get; init; }

    [JsonPropertyName("initialRemotePath")]
    public string InitialRemotePath { get; init; } = "/";

    [JsonPropertyName("savePassword")]
    public bool SavePassword { get; init; }

    [JsonPropertyName("obfuscatedSecret")]
    public string ObfuscatedSecret { get; init; } = string.Empty;
}

public sealed class ConnectionStore
{
    private readonly string _path;
    private readonly Func<string, string?> _reader;
    private readonly Action<string, string> _writer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ConnectionStore(string path)
        : this(path,
               p => File.Exists(p) ? File.ReadAllText(p) : null,
               (p, content) =>
               {
                   var tmp = p + ".tmp";
                   File.WriteAllText(tmp, content);
                   File.Move(tmp, p, overwrite: true);
               })
    { }

    public ConnectionStore(string path, Func<string, string?> reader, Action<string, string> writer)
    {
        _path = path;
        _reader = reader;
        _writer = writer;
    }

    public StoredConnection[] Load()
    {
        try
        {
            var content = _reader(_path);
            if (string.IsNullOrWhiteSpace(content))
                return [];

            return JsonSerializer.Deserialize<StoredConnection[]>(content, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public void Save(StoredConnection[] connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var content = JsonSerializer.Serialize(connections, JsonOptions);
        _writer(_path, content);
    }

    public static ConnectionInfo ResolveInfo(StoredConnection stored) =>
        new(
            Id: stored.Id,
            Name: stored.Name,
            Host: stored.Host,
            Port: stored.Port,
            Username: stored.Username,
            AuthMode: stored.AuthMode,
            KeyPath: stored.KeyPath,
            InitialRemotePath: stored.InitialRemotePath);

    public static ConnectSecret? ResolveSecret(StoredConnection stored, SecretCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        if (!stored.SavePassword || string.IsNullOrEmpty(stored.ObfuscatedSecret))
            return null;

        var plaintext = codec.TryDecrypt(stored.ObfuscatedSecret);
        if (plaintext is null)
            return null;

        return stored.AuthMode switch
        {
            AuthMode.Password => ConnectSecret.FromPassword(plaintext),
            AuthMode.Key => ConnectSecret.FromKey(plaintext == string.Empty ? null : plaintext),
            _ => null,
        };
    }

    public static (ConnectionInfo Info, ConnectSecret? Secret) Resolve(StoredConnection stored, SecretCodec codec) =>
        (ResolveInfo(stored), ResolveSecret(stored, codec));

    public static StoredConnection Pack(
        ConnectionInfo info,
        ConnectSecret? secret,
        bool savePassword,
        SecretCodec codec)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(codec);

        string obfuscated = string.Empty;
        if (savePassword && secret is not null)
        {
            var plaintext = info.AuthMode switch
            {
                AuthMode.Password => secret.Password ?? string.Empty,
                AuthMode.Key => secret.KeyPassphrase ?? string.Empty,
                _ => string.Empty,
            };
            obfuscated = codec.Encrypt(plaintext);
        }

        return new StoredConnection
        {
            Id = info.Id,
            Name = info.Name,
            Host = info.Host,
            Port = info.Port,
            Username = info.Username,
            AuthMode = info.AuthMode,
            KeyPath = info.KeyPath,
            InitialRemotePath = info.InitialRemotePath,
            SavePassword = savePassword,
            ObfuscatedSecret = obfuscated,
        };
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duetto.Core.Remote;

public sealed record StoredSmbConnection
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; init; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; init; } = 445;

    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;

    [JsonPropertyName("guest")]
    public bool Guest { get; init; }

    [JsonPropertyName("initialPath")]
    public string InitialPath { get; init; } = "/";

    [JsonPropertyName("savePassword")]
    public bool SavePassword { get; init; }

    [JsonPropertyName("obfuscatedSecret")]
    public string ObfuscatedSecret { get; init; } = string.Empty;
}

public sealed class SmbConnectionStore
{
    private readonly string path;
    private readonly Func<string, string?> reader;
    private readonly Action<string, string> writer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SmbConnectionStore(string path)
        : this(path,
               p => File.Exists(p) ? File.ReadAllText(p) : null,
               (p, content) =>
               {
                   var tmp = p + ".tmp";
                   File.WriteAllText(tmp, content);
                   File.Move(tmp, p, overwrite: true);
               })
    {
    }

    public SmbConnectionStore(string path, Func<string, string?> reader, Action<string, string> writer)
    {
        this.path = path;
        this.reader = reader;
        this.writer = writer;
    }

    public StoredSmbConnection[] Load()
    {
        try
        {
            var content = reader(path);
            if (string.IsNullOrWhiteSpace(content))
                return [];

            return JsonSerializer.Deserialize<StoredSmbConnection[]>(content, JsonOptions) ?? [];
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

    public void Save(StoredSmbConnection[] connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var content = JsonSerializer.Serialize(connections, JsonOptions);
        writer(path, content);
    }

    public static SmbConnectionInfo ResolveInfo(StoredSmbConnection stored) =>
        new(
            Id: stored.Id,
            Name: stored.Name,
            Host: stored.Host,
            Port: stored.Port,
            Username: stored.Username,
            Domain: stored.Domain,
            Guest: stored.Guest,
            InitialPath: stored.InitialPath);

    public static ConnectSecret? ResolveSecret(StoredSmbConnection stored, SecretCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        if (stored.Guest)
            return ConnectSecret.FromPassword(string.Empty);

        if (!stored.SavePassword || string.IsNullOrEmpty(stored.ObfuscatedSecret))
            return null;

        var plaintext = codec.TryDecrypt(stored.ObfuscatedSecret);
        return plaintext is null ? null : ConnectSecret.FromPassword(plaintext);
    }

    public static (SmbConnectionInfo Info, ConnectSecret? Secret) Resolve(StoredSmbConnection stored, SecretCodec codec) =>
        (ResolveInfo(stored), ResolveSecret(stored, codec));

    public static StoredSmbConnection Pack(
        SmbConnectionInfo info,
        ConnectSecret? secret,
        bool savePassword,
        SecretCodec codec)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(codec);

        var obfuscated = string.Empty;
        if (!info.Guest && savePassword && secret is not null)
            obfuscated = codec.Encrypt(secret.Password ?? string.Empty);

        return new StoredSmbConnection
        {
            Id = info.Id,
            Name = info.Name,
            Host = info.Host,
            Port = info.Port,
            Username = info.Username,
            Domain = info.Domain,
            Guest = info.Guest,
            InitialPath = info.InitialPath,
            SavePassword = savePassword,
            ObfuscatedSecret = obfuscated,
        };
    }
}

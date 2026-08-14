using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duetto.Core.Remote;

public sealed record StoredAzureConnection
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    [JsonPropertyName("accountName")]
    public string AccountName { get; init; } = string.Empty;

    [JsonPropertyName("authMode")]
    public AzureAuthMode AuthMode { get; init; } = AzureAuthMode.SharedKey;

    [JsonPropertyName("container")]
    public string Container { get; init; } = string.Empty;

    [JsonPropertyName("initialPath")]
    public string InitialPath { get; init; } = "/";

    [JsonPropertyName("savePassword")]
    public bool SavePassword { get; init; }

    [JsonPropertyName("obfuscatedSecret")]
    public string ObfuscatedSecret { get; init; } = string.Empty;
}

public sealed class AzureConnectionStore
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

    private sealed record SecretPayload(
        [property: JsonPropertyName("s")] string Secret);

    public AzureConnectionStore(string path)
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

    public AzureConnectionStore(string path, Func<string, string?> reader, Action<string, string> writer)
    {
        this.path = path;
        this.reader = reader;
        this.writer = writer;
    }

    public StoredAzureConnection[] Load()
    {
        try
        {
            var content = reader(path);
            if (string.IsNullOrWhiteSpace(content))
                return [];

            return JsonSerializer.Deserialize<StoredAzureConnection[]>(content, JsonOptions) ?? [];
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

    public void Save(StoredAzureConnection[] connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var content = JsonSerializer.Serialize(connections, JsonOptions);
        writer(path, content);
    }

    public static AzureConnectionInfo ResolveInfo(StoredAzureConnection stored) =>
        new(
            Id: stored.Id,
            Name: stored.Name,
            Endpoint: stored.Endpoint,
            AccountName: stored.AccountName,
            AuthMode: stored.AuthMode,
            Container: stored.Container,
            InitialPath: stored.InitialPath);

    public static ConnectSecret? ResolveSecret(StoredAzureConnection stored, SecretCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        if (stored.AuthMode == AzureAuthMode.Anonymous)
            return new ConnectSecret();

        if (!stored.SavePassword || string.IsNullOrEmpty(stored.ObfuscatedSecret))
            return null;

        var plaintext = codec.TryDecrypt(stored.ObfuscatedSecret);
        if (plaintext is null)
            return null;

        try
        {
            var payload = JsonSerializer.Deserialize<SecretPayload>(plaintext, JsonOptions);
            return payload is null ? null : ConnectSecret.FromPassword(payload.Secret);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static (AzureConnectionInfo Info, ConnectSecret? Secret) Resolve(StoredAzureConnection stored, SecretCodec codec) =>
        (ResolveInfo(stored), ResolveSecret(stored, codec));

    public static StoredAzureConnection Pack(
        AzureConnectionInfo info,
        ConnectSecret? secret,
        bool savePassword,
        SecretCodec codec)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(codec);

        var obfuscated = string.Empty;
        if (info.AuthMode != AzureAuthMode.Anonymous && savePassword && secret is not null)
        {
            var payload = new SecretPayload(secret.Password ?? string.Empty);
            obfuscated = codec.Encrypt(JsonSerializer.Serialize(payload, JsonOptions));
        }

        return new StoredAzureConnection
        {
            Id = info.Id,
            Name = info.Name,
            Endpoint = info.Endpoint,
            AccountName = info.AccountName,
            AuthMode = info.AuthMode,
            Container = info.Container,
            InitialPath = info.InitialPath,
            SavePassword = savePassword,
            ObfuscatedSecret = obfuscated,
        };
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duetto.Core.Remote;

// On-disk DTO for a saved Azure Blob connection. Secrets are never placed on AzureConnectionInfo;
// the (obfuscated) account key / SAS token / connection string lives here in ObfuscatedSecret. Only
// the credentialed modes (SharedKey, ConnectionString, Sas) persist a secret — Anonymous stores
// none. Separate from StoredS3Connection by design — Azure has account/container/endpoint instead of
// region/path-style/bucket.
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

    // The single credential value (account key, SAS, or connection string) is packed as JSON before
    // obfuscation so it survives a save/load round-trip inside the ObfuscatedSecret field.
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

    // Returns an empty array when the file is missing, empty, or corrupt — never throws, so a
    // hand-mangled config can't crash startup.
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

    // Anonymous needs no stored secret — an empty ConnectSecret lets the connect flow proceed without
    // a prompt. For the credentialed modes, null means the caller must obtain the secret at connect
    // time (it wasn't saved, or decryption failed on a foreign machine).
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

    // The secret is persisted (obfuscated) only for credentialed modes when savePassword is true and
    // secret is non-null.
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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duetto.Core.Remote;

// On-disk DTO for a saved S3 connection. Secrets are never placed on S3ConnectionInfo; the
// (obfuscated) secret access key + optional session token live here in ObfuscatedSecret. Only Keys
// auth persists a secret — Profile and Anonymous store none. Separate from StoredConnection (SFTP)
// and StoredSmbConnection by design — S3 has endpoint/region/path-style/bucket instead of a host.
public sealed record StoredS3Connection
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    [JsonPropertyName("region")]
    public string Region { get; init; } = string.Empty;

    [JsonPropertyName("pathStyle")]
    public bool PathStyle { get; init; }

    [JsonPropertyName("authMode")]
    public S3AuthMode AuthMode { get; init; } = S3AuthMode.Keys;

    [JsonPropertyName("accessKeyId")]
    public string AccessKeyId { get; init; } = string.Empty;

    [JsonPropertyName("profile")]
    public string Profile { get; init; } = string.Empty;

    [JsonPropertyName("bucket")]
    public string Bucket { get; init; } = string.Empty;

    [JsonPropertyName("initialPath")]
    public string InitialPath { get; init; } = "/";

    [JsonPropertyName("savePassword")]
    public bool SavePassword { get; init; }

    [JsonPropertyName("obfuscatedSecret")]
    public string ObfuscatedSecret { get; init; } = string.Empty;
}

public sealed class S3ConnectionStore
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

    // Secret access key + optional session token are packed as JSON before obfuscation so both
    // survive a save/load round-trip inside the single ObfuscatedSecret field.
    private sealed record SecretPayload(
        [property: JsonPropertyName("s")] string Secret,
        [property: JsonPropertyName("t")] string? Token);

    public S3ConnectionStore(string path)
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

    public S3ConnectionStore(string path, Func<string, string?> reader, Action<string, string> writer)
    {
        this.path = path;
        this.reader = reader;
        this.writer = writer;
    }

    // Returns an empty array when the file is missing, empty, or corrupt — never throws, so a
    // hand-mangled config can't crash startup.
    public StoredS3Connection[] Load()
    {
        try
        {
            var content = reader(path);
            if (string.IsNullOrWhiteSpace(content))
                return [];

            return JsonSerializer.Deserialize<StoredS3Connection[]>(content, JsonOptions) ?? [];
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

    public void Save(StoredS3Connection[] connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var content = JsonSerializer.Serialize(connections, JsonOptions);
        writer(path, content);
    }

    public static S3ConnectionInfo ResolveInfo(StoredS3Connection stored) =>
        new(
            Id: stored.Id,
            Name: stored.Name,
            Endpoint: stored.Endpoint,
            Region: stored.Region,
            PathStyle: stored.PathStyle,
            AuthMode: stored.AuthMode,
            AccessKeyId: stored.AccessKeyId,
            Profile: stored.Profile,
            Bucket: stored.Bucket,
            InitialPath: stored.InitialPath);

    // Profile and Anonymous need no stored secret — an empty ConnectSecret lets the connect flow
    // proceed without a prompt. For Keys auth, null means the caller must obtain the secret at
    // connect time (it wasn't saved, or decryption failed on a foreign machine).
    public static ConnectSecret? ResolveSecret(StoredS3Connection stored, SecretCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        if (stored.AuthMode != S3AuthMode.Keys)
            return new ConnectSecret();

        if (!stored.SavePassword || string.IsNullOrEmpty(stored.ObfuscatedSecret))
            return null;

        var plaintext = codec.TryDecrypt(stored.ObfuscatedSecret);
        if (plaintext is null)
            return null;

        try
        {
            var payload = JsonSerializer.Deserialize<SecretPayload>(plaintext, JsonOptions);
            return payload is null ? null : ConnectSecret.FromKeys(payload.Secret, payload.Token);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static (S3ConnectionInfo Info, ConnectSecret? Secret) Resolve(StoredS3Connection stored, SecretCodec codec) =>
        (ResolveInfo(stored), ResolveSecret(stored, codec));

    // The secret is persisted (obfuscated) only for Keys auth when savePassword is true and secret
    // is non-null. Session token is packed alongside the secret access key.
    public static StoredS3Connection Pack(
        S3ConnectionInfo info,
        ConnectSecret? secret,
        bool savePassword,
        SecretCodec codec)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(codec);

        var obfuscated = string.Empty;
        if (info.AuthMode == S3AuthMode.Keys && savePassword && secret is not null)
        {
            var payload = new SecretPayload(secret.Password ?? string.Empty, secret.SessionToken);
            obfuscated = codec.Encrypt(JsonSerializer.Serialize(payload, JsonOptions));
        }

        return new StoredS3Connection
        {
            Id = info.Id,
            Name = info.Name,
            Endpoint = info.Endpoint,
            Region = info.Region,
            PathStyle = info.PathStyle,
            AuthMode = info.AuthMode,
            AccessKeyId = info.AccessKeyId,
            Profile = info.Profile,
            Bucket = info.Bucket,
            InitialPath = info.InitialPath,
            SavePassword = savePassword,
            ObfuscatedSecret = obfuscated,
        };
    }
}

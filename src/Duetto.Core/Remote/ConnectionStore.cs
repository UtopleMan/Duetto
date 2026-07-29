using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duetto.Core.Remote;

// ── Stored DTO ────────────────────────────────────────────────────────────────

/// <summary>
/// The JSON-persisted shape for a single saved connection.
///
/// <para>
/// <b>Separation of concerns:</b> secrets are never placed on <see cref="ConnectionInfo"/>.
/// <see cref="StoredConnection"/> is the on-disk DTO that carries the obfuscated secret and
/// the <see cref="SavePassword"/> flag.  Call <see cref="ConnectionStore.Resolve"/> (or
/// <see cref="ConnectionStore.ResolveInfo"/> / <see cref="ConnectionStore.ResolveSecret"/>)
/// to materialise the live objects that the rest of the app works with.
/// </para>
///
/// <b>Phase 4 usage:</b>
/// <code>
/// // Loading all connections for the sidebar list:
/// var stored = store.Load();
/// var infos = stored.Select(ConnectionStore.ResolveInfo).ToList();
///
/// // Connecting (password saved):
/// var (info, secret) = ConnectionStore.Resolve(stored[i]);
/// if (secret is null) secret = await AskUserForSecret(info);
/// await manager.Connect(info, secret);
///
/// // Saving a new connection with password:
/// store.Save(store.Load().Append(
///     ConnectionStore.Pack(info, ConnectSecret.FromPassword("hunter2"), savePassword: true, codec))
///     .ToArray());
/// </code>
/// </summary>
public sealed record StoredConnection
{
    // ── ConnectionInfo fields (mirrored so the DTO is self-contained) ─────────

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

    /// <summary>
    /// Path to the private-key file.  <see langword="null"/> when not using key auth.
    /// Key paths are always saved regardless of <see cref="SavePassword"/>.
    /// </summary>
    [JsonPropertyName("keyPath")]
    public string? KeyPath { get; init; }

    [JsonPropertyName("initialRemotePath")]
    public string InitialRemotePath { get; init; } = "/";

    // ── Secret fields ─────────────────────────────────────────────────────────

    /// <summary>
    /// When <see langword="true"/> the password / passphrase was saved; the obfuscated
    /// value is in <see cref="ObfuscatedSecret"/>.
    /// When <see langword="false"/> <see cref="ObfuscatedSecret"/> is empty and the secret
    /// must be resolved at connect time (e.g. via a UI prompt).
    /// </summary>
    [JsonPropertyName("savePassword")]
    public bool SavePassword { get; init; }

    /// <summary>
    /// AES-obfuscated password (for <see cref="AuthMode.Password"/>) or key passphrase
    /// (for <see cref="AuthMode.Key"/>), base64-encoded.
    /// Empty string when <see cref="SavePassword"/> is <see langword="false"/> or the key has
    /// no passphrase.
    /// </summary>
    [JsonPropertyName("obfuscatedSecret")]
    public string ObfuscatedSecret { get; init; } = string.Empty;
}

// ── Store ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Loads and saves <see cref="StoredConnection"/> arrays from/to <c>connections.json</c>.
///
/// <para>
/// <b>Resilience:</b> a missing file, a corrupt file, or any <see cref="IOException"/> during
/// reading returns an empty array — it never throws out of <see cref="Load"/>.
/// </para>
///
/// <para>
/// <b>Atomic writes:</b> <see cref="Save"/> writes to a <c>.tmp</c> sibling first, then
/// overwrites the target file with <see cref="File.Move"/> to prevent torn files.
/// </para>
///
/// <para>
/// <b>File-IO seam:</b> inject <paramref name="reader"/> / <paramref name="writer"/> in tests
/// to avoid touching the real filesystem.
/// </para>
/// </summary>
public sealed class ConnectionStore
{
    private readonly string _path;
    private readonly Func<string, string?> _reader;
    private readonly Action<string, string> _writer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Tolerate hand-edited files whose property names use a different casing
        // (e.g. "Host" instead of "host") instead of silently loading defaults.
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Creates a store backed by the real filesystem at <paramref name="path"/>.
    /// </summary>
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

    /// <summary>
    /// Creates a store with injected IO — intended for unit tests.
    /// </summary>
    /// <param name="path">Logical path passed to <paramref name="reader"/> and <paramref name="writer"/>.</param>
    /// <param name="reader">Returns the file content, or <see langword="null"/> when the file does not exist.</param>
    /// <param name="writer">Writes the file content atomically (temp + move logic belongs here for production use).</param>
    public ConnectionStore(string path, Func<string, string?> reader, Action<string, string> writer)
    {
        _path = path;
        _reader = reader;
        _writer = writer;
    }

    /// <summary>
    /// Loads all stored connections from the configured path.
    /// Returns an empty array when the file is missing, empty, or corrupt — never throws.
    /// </summary>
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

    /// <summary>
    /// Saves <paramref name="connections"/> to the configured path, overwriting any existing
    /// file.  Uses an atomic temp-then-move strategy in the production constructor.
    /// </summary>
    public void Save(StoredConnection[] connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var content = JsonSerializer.Serialize(connections, JsonOptions);
        _writer(_path, content);
    }

    // ── Resolve helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the <see cref="ConnectionInfo"/> from a <see cref="StoredConnection"/>.
    /// No secrets are involved.
    /// </summary>
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

    /// <summary>
    /// Attempts to resolve the saved secret using <paramref name="codec"/>.
    /// Returns <see langword="null"/> when <see cref="StoredConnection.SavePassword"/> is
    /// <see langword="false"/>, the obfuscated field is empty, or decryption fails.
    /// When <see langword="null"/> is returned the caller must obtain the secret at connect time
    /// (e.g. via a UI prompt).
    /// </summary>
    public static ConnectSecret? ResolveSecret(StoredConnection stored, SecretCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        if (!stored.SavePassword || string.IsNullOrEmpty(stored.ObfuscatedSecret))
            return null;

        var plaintext = codec.TryDecrypt(stored.ObfuscatedSecret);
        if (plaintext is null)
            return null; // corrupt / foreign-machine ciphertext → prompt at connect

        return stored.AuthMode switch
        {
            AuthMode.Password => ConnectSecret.FromPassword(plaintext),
            AuthMode.Key => ConnectSecret.FromKey(plaintext == string.Empty ? null : plaintext),
            _ => null,
        };
    }

    /// <summary>
    /// Convenience overload: returns the <see cref="ConnectionInfo"/> and an optional
    /// <see cref="ConnectSecret"/> (null when the secret needs to be prompted).
    /// </summary>
    public static (ConnectionInfo Info, ConnectSecret? Secret) Resolve(StoredConnection stored, SecretCodec codec) =>
        (ResolveInfo(stored), ResolveSecret(stored, codec));

    /// <summary>
    /// Packs a <see cref="ConnectionInfo"/> and an optional <see cref="ConnectSecret"/> into a
    /// <see cref="StoredConnection"/> for persistence.
    ///
    /// <para>
    /// The key path is always persisted.  The secret is persisted (obfuscated) only when
    /// <paramref name="savePassword"/> is <see langword="true"/> and
    /// <paramref name="secret"/> is non-null.
    /// </para>
    /// </summary>
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

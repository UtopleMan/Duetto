using System.Security.Cryptography;
using System.Text;

namespace Duetto.Core.Remote;

/// <summary>
/// Reversible obfuscation of short secret strings (passwords, key passphrases) for storage
/// in <c>connections.json</c>.
///
/// <para>
/// <b>Security model:</b> this is <em>obfuscation only</em>, not cryptographically secure
/// protection.  The key is derived from <see cref="Environment.MachineName"/> + the OS
/// user name + a fixed app salt, so the ciphertext is unreadable on a different machine or
/// user account but is trivially reversible by anyone with access to the same account and
/// the source code.  It protects against shoulder-surfing and casual file inspection; it
/// does NOT protect against a local attacker who can execute code in the same user context.
/// </para>
///
/// <para>
/// Algorithm: AES-256-CBC, SHA-256 key derivation, random 16-byte IV prepended to the
/// ciphertext, base64-encoded output.  A corrupt or foreign-machine ciphertext produces
/// <see langword="null"/> from <see cref="TryDecrypt"/> rather than throwing.
/// </para>
/// </summary>
public sealed class SecretCodec
{
    /// <summary>Fixed salt mixed into the key derivation so the key is app-specific.</summary>
    private const string AppSalt = "Duetto-ConfigStore-v1";

    private readonly byte[] _key;

    /// <summary>
    /// Creates a <see cref="SecretCodec"/> using the default machine-derived key.
    /// The key is derived from <see cref="Environment.MachineName"/>,
    /// <see cref="Environment.UserName"/>, and the fixed app salt.
    /// </summary>
    public SecretCodec() : this(DeriveKey(Environment.MachineName, Environment.UserName)) { }

    /// <summary>
    /// Creates a <see cref="SecretCodec"/> with an explicit 32-byte AES-256 key.
    /// Primarily used in unit tests to make round-trips deterministic across machines.
    /// </summary>
    public SecretCodec(byte[] key)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (key.Length != 32) throw new ArgumentException("Key must be 32 bytes (AES-256).", nameof(key));
        _key = key;
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> and returns a base64 string
    /// (IV prepended to ciphertext).
    /// </summary>
    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length); // 16-byte IV prefix

        using (var encryptor = aes.CreateEncryptor())
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            cs.Write(bytes, 0, bytes.Length);
            cs.FlushFinalBlock();
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// Attempts to decrypt a base64 ciphertext produced by <see cref="Encrypt"/>.
    /// Returns the original plaintext on success, or <see langword="null"/> when the
    /// ciphertext is corrupt, too short, or was encrypted with a different key
    /// (e.g. on a different machine or user account).
    /// </summary>
    public string? TryDecrypt(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return null;

        try
        {
            var raw = Convert.FromBase64String(ciphertext);

            const int ivLen = 16;
            if (raw.Length <= ivLen)
                return null;

            var iv = raw[..ivLen];
            var payload = raw[ivLen..];

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = iv;

            using var ms = new MemoryStream();
            using (var decryptor = aes.CreateDecryptor())
            using (var cs = new CryptoStream(new MemoryStream(payload), decryptor, CryptoStreamMode.Read))
            {
                cs.CopyTo(ms);
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (FormatException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    // ── key derivation ────────────────────────────────────────────────────────

    /// <summary>
    /// Derives a 32-byte AES-256 key from the machine name, username, and app salt
    /// by hashing their concatenation with SHA-256.
    /// </summary>
    public static byte[] DeriveKey(string machineName, string userName)
    {
        var material = $"{AppSalt}|{machineName}|{userName}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }
}

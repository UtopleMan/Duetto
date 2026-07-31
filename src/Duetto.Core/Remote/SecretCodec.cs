using System.Security.Cryptography;
using System.Text;

namespace Duetto.Core.Remote;

// Security model: this is obfuscation only, not cryptographically secure protection. The key
// is derived from machine name + OS user + a fixed app salt, so the ciphertext is unreadable on
// a different machine or account but is trivially reversible by anyone with the same account and
// the source. Protects against shoulder-surfing and casual file inspection; NOT against a local
// attacker who can execute code in the same user context.
public sealed class SecretCodec
{
    private const string AppSalt = "Duetto-ConfigStore-v1";

    private readonly byte[] _key;

    public SecretCodec() : this(DeriveKey(Environment.MachineName, Environment.UserName)) { }

    public SecretCodec(byte[] key)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (key.Length != 32) throw new ArgumentException("Key must be 32 bytes (AES-256).", nameof(key));
        _key = key;
    }

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

    // Returns null (never throws) on corrupt, too-short, or foreign-key ciphertext so the
    // caller can fall back to prompting instead of crashing.
    public string? TryDecrypt(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return null;

        try
        {
            var raw = Convert.FromBase64String(ciphertext);

            // Valid output of Encrypt is a 16-byte IV followed by at least one whole
            // AES block (CBC + PKCS7 padding always emits >= 1 block, even for empty
            // plaintext).  Reject anything shorter — including exactly-16 bytes (an IV
            // with an empty payload) — and any payload that is not a whole number of
            // blocks, via the guard rather than relying on a padding exception.
            const int ivLen = 16;
            const int blockLen = 16;
            if (raw.Length < ivLen + blockLen || (raw.Length - ivLen) % blockLen != 0)
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

    public static byte[] DeriveKey(string machineName, string userName)
    {
        var material = $"{AppSalt}|{machineName}|{userName}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }
}

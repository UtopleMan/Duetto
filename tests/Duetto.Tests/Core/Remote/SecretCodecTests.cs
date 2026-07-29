using Duetto.Core.Remote;
using System.Security.Cryptography;

namespace Duetto.Tests.Core.Remote;

/// <summary>
/// Tests for SecretCodec obfuscation.  All tests inject a deterministic 32-byte key so
/// results are reproducible across machines.
/// </summary>
public class SecretCodecTests
{
    // Fixed 32-byte test key (non-zero, non-trivial).
    private static readonly byte[] TestKey = SHA256.HashData("duetto-test-key-v1"u8.ToArray());

    private static SecretCodec MakeCodec() => new(TestKey);

    [Fact]
    public void Encrypt_TryDecrypt_is_identity()
    {
        var codec = MakeCodec();
        const string plaintext = "hunter2";

        var cipher = codec.Encrypt(plaintext);
        var result = codec.TryDecrypt(cipher);

        Assert.Equal(plaintext, result);
    }

    [Fact]
    public void Ciphertext_differs_from_plaintext()
    {
        var codec = MakeCodec();
        const string plaintext = "my-secret";

        var cipher = codec.Encrypt(plaintext);

        Assert.NotEqual(plaintext, cipher);
    }

    [Fact]
    public void Two_encryptions_of_same_plaintext_produce_different_ciphertexts()
    {
        // Random IV means the same input gives different base64 blobs each time.
        var codec = MakeCodec();
        const string plaintext = "same-password";

        var c1 = codec.Encrypt(plaintext);
        var c2 = codec.Encrypt(plaintext);

        Assert.NotEqual(c1, c2);
    }

    [Fact]
    public void Encrypt_empty_string_round_trips()
    {
        var codec = MakeCodec();
        var cipher = codec.Encrypt(string.Empty);
        Assert.Equal(string.Empty, codec.TryDecrypt(cipher));
    }

    [Fact]
    public void Encrypt_unicode_string_round_trips()
    {
        var codec = MakeCodec();
        const string plaintext = "päässönä"; // Finnish "päässönä"
        var cipher = codec.Encrypt(plaintext);
        Assert.Equal(plaintext, codec.TryDecrypt(cipher));
    }

    [Fact]
    public void TryDecrypt_null_returns_null()
    {
        var codec = MakeCodec();
        Assert.Null(codec.TryDecrypt(null));
    }

    [Fact]
    public void TryDecrypt_empty_returns_null()
    {
        var codec = MakeCodec();
        Assert.Null(codec.TryDecrypt(string.Empty));
    }

    [Fact]
    public void TryDecrypt_invalid_base64_returns_null()
    {
        var codec = MakeCodec();
        Assert.Null(codec.TryDecrypt("this is not base64!!!"));
    }

    [Fact]
    public void TryDecrypt_too_short_returns_null()
    {
        var codec = MakeCodec();
        // 15 bytes of valid base64 — shorter than the 16-byte IV prefix.
        var tooShort = Convert.ToBase64String(new byte[15]);
        Assert.Null(codec.TryDecrypt(tooShort));
    }

    [Fact]
    public void TryDecrypt_exactly_16_bytes_returns_null()
    {
        var codec = MakeCodec();
        // Exactly one IV with an empty payload — invalid by the length guard
        // (valid ciphertext is IV + at least one whole AES block).
        var ivOnly = Convert.ToBase64String(new byte[16]);
        Assert.Null(codec.TryDecrypt(ivOnly));
    }

    [Fact]
    public void TryDecrypt_non_block_multiple_payload_returns_null()
    {
        var codec = MakeCodec();
        // IV (16) + 8-byte payload — not a whole number of AES blocks.
        var ragged = Convert.ToBase64String(new byte[24]);
        Assert.Null(codec.TryDecrypt(ragged));
    }

    [Fact]
    public void TryDecrypt_wrong_key_returns_null()
    {
        var codec1 = MakeCodec();

        var otherKey = SHA256.HashData("other-key"u8.ToArray());
        var codec2 = new SecretCodec(otherKey);

        var cipher = codec1.Encrypt("secret");

        // Decrypting with the wrong key must not throw — must return null.
        var result = codec2.TryDecrypt(cipher);
        Assert.Null(result);
    }

    [Fact]
    public void DeriveKey_produces_32_bytes()
    {
        var key = SecretCodec.DeriveKey("MYPC", "alice");
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void DeriveKey_is_deterministic()
    {
        var k1 = SecretCodec.DeriveKey("MYPC", "alice");
        var k2 = SecretCodec.DeriveKey("MYPC", "alice");
        Assert.Equal(k1, k2);
    }

    [Fact]
    public void DeriveKey_differs_for_different_machine_or_user()
    {
        var k1 = SecretCodec.DeriveKey("MYPC", "alice");
        var k2 = SecretCodec.DeriveKey("MYPC", "bob");
        var k3 = SecretCodec.DeriveKey("OTHER", "alice");

        Assert.NotEqual(k1, k2);
        Assert.NotEqual(k1, k3);
    }
}

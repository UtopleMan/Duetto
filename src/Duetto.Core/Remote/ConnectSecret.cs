namespace Duetto.Core.Remote;

/// <summary>
/// The ephemeral secret supplied by the caller at connect time.
/// Exactly one property is meaningful depending on the <see cref="ConnectionInfo.AuthMode"/>:
/// <list type="bullet">
///   <item><description><c>Password</c> — plaintext password for <see cref="AuthMode.Password"/>.</description></item>
///   <item><description><c>KeyPassphrase</c> — passphrase to decrypt the private-key file for <see cref="AuthMode.Key"/>; may be <see langword="null"/> when the key is unencrypted.</description></item>
/// </list>
/// </summary>
public sealed record ConnectSecret(string? Password = null, string? KeyPassphrase = null)
{
    /// <summary>Convenience factory: password authentication.</summary>
    public static ConnectSecret FromPassword(string password) => new(Password: password);

    /// <summary>Convenience factory: private-key authentication with an optional passphrase.</summary>
    public static ConnectSecret FromKey(string? passphrase = null) => new(KeyPassphrase: passphrase);
}

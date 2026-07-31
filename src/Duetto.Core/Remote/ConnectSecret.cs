namespace Duetto.Core.Remote;

// Exactly one property is meaningful depending on ConnectionInfo.AuthMode: Password for
// AuthMode.Password; KeyPassphrase decrypts the private-key file for AuthMode.Key and may be
// null when the key is unencrypted.
public sealed record ConnectSecret(string? Password = null, string? KeyPassphrase = null)
{
    public static ConnectSecret FromPassword(string password) => new(Password: password);

    public static ConnectSecret FromKey(string? passphrase = null) => new(KeyPassphrase: passphrase);
}

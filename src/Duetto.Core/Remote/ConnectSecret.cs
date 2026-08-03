namespace Duetto.Core.Remote;

// Which property is meaningful depends on the backend/auth mode: Password for SFTP/SMB password
// auth; KeyPassphrase decrypts the private-key file for SFTP AuthMode.Key (null when unencrypted).
// For S3 Keys auth, Password carries the secret access key and SessionToken the optional STS token.
public sealed record ConnectSecret(string? Password = null, string? KeyPassphrase = null, string? SessionToken = null)
{
    public static ConnectSecret FromPassword(string password) => new(Password: password);

    public static ConnectSecret FromKey(string? passphrase = null) => new(KeyPassphrase: passphrase);

    public static ConnectSecret FromKeys(string secretAccessKey, string? sessionToken = null) =>
        new(Password: secretAccessKey, SessionToken: sessionToken);
}

namespace Duetto.Core.Remote;

public sealed record ConnectSecret(string? Password = null, string? KeyPassphrase = null, string? SessionToken = null)
{
    public static ConnectSecret FromPassword(string password) => new(Password: password);

    public static ConnectSecret FromKey(string? passphrase = null) => new(KeyPassphrase: passphrase);

    public static ConnectSecret FromKeys(string secretAccessKey, string? sessionToken = null) =>
        new(Password: secretAccessKey, SessionToken: sessionToken);
}

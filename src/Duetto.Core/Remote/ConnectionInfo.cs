namespace Duetto.Core.Remote;

public enum AuthMode
{
    Password,
    Key,
}

// Secrets (password / key passphrase) are NOT stored here — they are supplied at
// connect time via ConnectSecret.
public sealed record ConnectionInfo(
    string Id,
    string Name,
    string Host,
    int Port = 22,
    string Username = "",
    AuthMode AuthMode = AuthMode.Password,
    string? KeyPath = null,
    string InitialRemotePath = "/");

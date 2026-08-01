namespace Duetto.Core.Remote;

// Secrets (the password) are NOT stored here — they are supplied at connect time via
// ConnectSecret. Port is retained for storage/UI parity but SMBLibrary always dials 445
// (DirectTCP); a non-default port is not honored by the transport.
public sealed record SmbConnectionInfo(
    string Id,
    string Name,
    string Host,
    int Port = 445,
    string Username = "",
    string Domain = "",
    bool Guest = false,
    string InitialPath = "/");

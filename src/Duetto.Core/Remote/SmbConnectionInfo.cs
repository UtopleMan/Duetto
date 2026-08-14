namespace Duetto.Core.Remote;

public sealed record SmbConnectionInfo(
    string Id,
    string Name,
    string Host,
    int Port = 445,
    string Username = "",
    string Domain = "",
    bool Guest = false,
    string InitialPath = "/");

namespace Duetto.Core.Remote;

public sealed record AzureConnectionInfo(
    string Id,
    string Name,
    string Endpoint = "",
    string AccountName = "",
    AzureAuthMode AuthMode = AzureAuthMode.SharedKey,
    string Container = "",
    string InitialPath = "/");

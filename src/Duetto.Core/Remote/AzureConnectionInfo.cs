namespace Duetto.Core.Remote;

// Secrets (account key / SAS token / connection string) are NOT stored here — they are supplied at
// connect time via ConnectSecret. Endpoint is blank for real Azure (built as
// https://{AccountName}.blob.core.windows.net); a non-blank Endpoint (e.g.
// http://127.0.0.1:10000/devstoreaccount1) targets an emulator or on-prem service with the account
// name in the path. Container blank means the root lists all containers; a set Container scopes the
// root to that one container (and is required for Anonymous auth). AccountName may be blank for
// ConnectionString auth, where the connection string carries the account and endpoint.
public sealed record AzureConnectionInfo(
    string Id,
    string Name,
    string Endpoint = "",
    string AccountName = "",
    AzureAuthMode AuthMode = AzureAuthMode.SharedKey,
    string Container = "",
    string InitialPath = "/");

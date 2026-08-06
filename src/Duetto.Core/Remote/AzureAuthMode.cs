namespace Duetto.Core.Remote;

// SharedKey: storage account name + account key (the classic shared-key credential).
// ConnectionString: a full connection string carries endpoint + account + key (or
// UseDevelopmentStorage=true for Azurite); the whole string is the secret.
// Sas: a Shared Access Signature token or SAS URL — scoped, time-limited, no account key.
// Anonymous: no credentials (public read-only); a Container is required because anonymous
// principals cannot list the account's containers.
public enum AzureAuthMode
{
    SharedKey,
    ConnectionString,
    Sas,
    Anonymous,
}

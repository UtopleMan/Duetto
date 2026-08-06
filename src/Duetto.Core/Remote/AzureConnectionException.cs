namespace Duetto.Core.Remote;

// Recoverable transport failure (connection reset, transient network error). The connection layer
// may rebuild the client and retry once.
public sealed class AzureConnectionException : Exception
{
    public AzureConnectionException(string message) : base(message) { }

    public AzureConnectionException(string message, Exception inner) : base(message, inner) { }
}

// Credential/authorization failure (bad account key, invalid SAS, access denied). Never retried —
// the caller must fix the credentials.
public sealed class AzureAuthenticationException : Exception
{
    public AzureAuthenticationException(string message) : base(message) { }

    public AzureAuthenticationException(string message, Exception inner) : base(message, inner) { }
}

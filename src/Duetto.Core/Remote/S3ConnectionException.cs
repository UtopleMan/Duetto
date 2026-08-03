namespace Duetto.Core.Remote;

// Recoverable transport failure (connection reset, transient network error). The connection layer
// may rebuild the client and retry once.
public sealed class S3ConnectionException : Exception
{
    public S3ConnectionException(string message) : base(message) { }

    public S3ConnectionException(string message, Exception inner) : base(message, inner) { }
}

// Credential/authorization failure (bad access key, signature mismatch, access denied). Never
// retried — the caller must fix the credentials.
public sealed class S3AuthenticationException : Exception
{
    public S3AuthenticationException(string message) : base(message) { }

    public S3AuthenticationException(string message, Exception inner) : base(message, inner) { }
}

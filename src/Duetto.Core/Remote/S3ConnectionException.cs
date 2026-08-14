namespace Duetto.Core.Remote;

public sealed class S3ConnectionException : Exception
{
    public S3ConnectionException(string message) : base(message) { }

    public S3ConnectionException(string message, Exception inner) : base(message, inner) { }
}

public sealed class S3AuthenticationException : Exception
{
    public S3AuthenticationException(string message) : base(message) { }

    public S3AuthenticationException(string message, Exception inner) : base(message, inner) { }
}

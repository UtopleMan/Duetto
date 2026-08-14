namespace Duetto.Core.Remote;

public sealed class AzureConnectionException : Exception
{
    public AzureConnectionException(string message) : base(message) { }

    public AzureConnectionException(string message, Exception inner) : base(message, inner) { }
}

public sealed class AzureAuthenticationException : Exception
{
    public AzureAuthenticationException(string message) : base(message) { }

    public AzureAuthenticationException(string message, Exception inner) : base(message, inner) { }
}

namespace Duetto.Core.Remote;

/// <summary>
/// Authentication mode for an SFTP connection.
/// </summary>
public enum AuthMode
{
    /// <summary>Authenticate with a password supplied at connect time.</summary>
    Password,

    /// <summary>Authenticate with a private-key file; an optional passphrase may be supplied at connect time.</summary>
    Key,
}

/// <summary>
/// Immutable descriptor for a named SFTP connection.  Secrets (password / key passphrase) are NOT
/// stored here — they are supplied at connect time via <see cref="ConnectSecret"/>.
/// </summary>
/// <param name="Id">Stable opaque identifier (e.g. a GUID string) used to key the connection in the registry and in <c>hostkeys.json</c>.</param>
/// <param name="Name">Human-readable display name shown in the UI.</param>
/// <param name="Host">DNS name or IP address of the remote server.</param>
/// <param name="Port">SSH port; defaults to 22.</param>
/// <param name="Username">Login username.</param>
/// <param name="AuthMode">Whether to authenticate with a password or a private-key file.</param>
/// <param name="KeyPath">Path to the private-key file.  Required when <see cref="AuthMode"/> is <see cref="Remote.AuthMode.Key"/>; ignored otherwise.</param>
/// <param name="InitialRemotePath">Working directory to navigate to immediately after connecting.</param>
public sealed record ConnectionInfo(
    string Id,
    string Name,
    string Host,
    int Port = 22,
    string Username = "",
    AuthMode AuthMode = AuthMode.Password,
    string? KeyPath = null,
    string InitialRemotePath = "/");

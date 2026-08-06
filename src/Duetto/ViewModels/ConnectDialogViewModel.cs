using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.Remote;
using Renci.SshNet.Common;

namespace Duetto.ViewModels;

public enum ConnectProtocol
{
    Sftp,
    Smb,
    S3,
    AzureBlob,
}

// One protocol-aware connect dialog (per the drive-popover design spec: a single "Connect…"
// entry that opens one dialog with a protocol selector). SFTP and SMB keep separate on-disk
// stores and managers; this VM routes connect/save by the selected Protocol.
public partial class ConnectDialogViewModel : ObservableObject
{
    // Runs on a background thread; tests inject fakes that open no sockets.
    public Action<ConnectionInfo, ConnectSecret> ConnectAction { get; set; }

    public Action<SmbConnectionInfo, ConnectSecret> SmbConnectAction { get; set; }

    public Action<S3ConnectionInfo, ConnectSecret> S3ConnectAction { get; set; }

    public Action<AzureConnectionInfo, ConnectSecret> AzureConnectAction { get; set; }

    public Action<StoredConnection> SaveAction { get; set; }

    public Action<StoredSmbConnection> SmbSaveAction { get; set; }

    public Action<StoredS3Connection> S3SaveAction { get; set; }

    public Action<StoredAzureConnection> AzureSaveAction { get; set; }

    // Removes a stale host-key pin so the next connect attempt re-pins (SFTP only).
    public Action<string> ForgetKeyAction { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSftp), nameof(IsSmb), nameof(IsS3), nameof(IsAzure),
        nameof(SftpAuthVisible), nameof(SmbFieldsVisible), nameof(KeySectionVisible), nameof(PasswordVisible),
        nameof(UsernameVisible), nameof(HostKeyWarningVisible), nameof(HostPortVisible),
        nameof(S3FieldsVisible), nameof(S3KeysVisible), nameof(S3ProfileVisible),
        nameof(AzureFieldsVisible), nameof(AzureAccountVisible), nameof(AzureKeyVisible),
        nameof(AzureConnStringVisible), nameof(AzureSasVisible), nameof(AzureContainerVisible),
        nameof(SaveSecretVisible))]
    private ConnectProtocol _protocol = ConnectProtocol.Sftp;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _host = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPortValid))]
    private string _portText = "22";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPasswordMode), nameof(IsKeyMode),
        nameof(KeySectionVisible), nameof(PasswordVisible))]
    private AuthMode _authMode = AuthMode.Password;

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _keyPath = "";

    [ObservableProperty]
    private string _keyPassphrase = "";

    // SMB only.
    [ObservableProperty]
    private string _domain = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordVisible), nameof(UsernameVisible))]
    private bool _guest;

    [ObservableProperty]
    private string _initialRemotePath = "/";

    // S3 only.
    [ObservableProperty]
    private string _endpoint = "";

    [ObservableProperty]
    private string _region = "";

    [ObservableProperty]
    private bool _pathStyle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(S3KeysVisible), nameof(S3ProfileVisible),
        nameof(IsS3KeysMode), nameof(IsS3ProfileMode), nameof(IsS3AnonymousMode),
        nameof(SaveSecretVisible))]
    private S3AuthMode _s3Auth = S3AuthMode.Keys;

    [ObservableProperty]
    private string _accessKeyId = "";

    [ObservableProperty]
    private string _secretKey = "";

    [ObservableProperty]
    private string _sessionToken = "";

    [ObservableProperty]
    private string _profile = "";

    [ObservableProperty]
    private string _bucket = "";

    // Azure Blob only. Endpoint (reused from S3) is the optional custom service URL.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AzureAccountVisible), nameof(AzureKeyVisible),
        nameof(AzureConnStringVisible), nameof(AzureSasVisible),
        nameof(IsAzureSharedKeyMode), nameof(IsAzureConnStringMode), nameof(IsAzureSasMode),
        nameof(IsAzureAnonymousMode), nameof(SaveSecretVisible))]
    private AzureAuthMode _azureAuth = AzureAuthMode.SharedKey;

    [ObservableProperty]
    private string _azureAccount = "";

    [ObservableProperty]
    private string _azureAccountKey = "";

    [ObservableProperty]
    private string _azureSasToken = "";

    [ObservableProperty]
    private string _azureConnectionString = "";

    [ObservableProperty]
    private string _azureContainer = "";

    [ObservableProperty]
    private bool _savePassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = "";

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHostKeyWarning), nameof(HostKeyWarningVisible))]
    private bool _isHostKeyChanged;

    [ObservableProperty]
    private string _oldFingerprint = "";

    [ObservableProperty]
    private string _newFingerprint = "";

    private string _hostKeyStoreKey = "";

    public bool IsSftp => Protocol == ConnectProtocol.Sftp;
    public bool IsSmb => Protocol == ConnectProtocol.Smb;
    public bool IsS3 => Protocol == ConnectProtocol.S3;
    public bool IsAzure => Protocol == ConnectProtocol.AzureBlob;

    public bool IsPasswordMode => AuthMode == AuthMode.Password;
    public bool IsKeyMode => AuthMode == AuthMode.Key;

    public bool IsS3KeysMode => S3Auth == S3AuthMode.Keys;
    public bool IsS3ProfileMode => S3Auth == S3AuthMode.Profile;
    public bool IsS3AnonymousMode => S3Auth == S3AuthMode.Anonymous;

    public bool IsAzureSharedKeyMode => AzureAuth == AzureAuthMode.SharedKey;
    public bool IsAzureConnStringMode => AzureAuth == AzureAuthMode.ConnectionString;
    public bool IsAzureSasMode => AzureAuth == AzureAuthMode.Sas;
    public bool IsAzureAnonymousMode => AzureAuth == AzureAuthMode.Anonymous;

    // SSH auth section (password/key radios + key file) is SFTP-only.
    public bool SftpAuthVisible => IsSftp;

    // Domain + guest are SMB-only.
    public bool SmbFieldsVisible => IsSmb;

    public bool KeySectionVisible => IsSftp && IsKeyMode;

    // Host + port apply to SFTP/SMB; S3 and Azure use an endpoint URL instead.
    public bool HostPortVisible => IsSftp || IsSmb;

    // Endpoint / region / path-style / bucket / auth selector are S3-only.
    public bool S3FieldsVisible => IsS3;

    // Access key + secret + session token show only for S3 Keys auth.
    public bool S3KeysVisible => IsS3 && S3Auth == S3AuthMode.Keys;

    // Profile name shows only for S3 Profile auth.
    public bool S3ProfileVisible => IsS3 && S3Auth == S3AuthMode.Profile;

    // Endpoint / container / auth selector are Azure-only.
    public bool AzureFieldsVisible => IsAzure;

    // Account name shows for the modes that need it (SharedKey; also useful for SAS/Anonymous to
    // build the default endpoint). Hidden for ConnectionString (the string carries the account).
    public bool AzureAccountVisible => IsAzure && AzureAuth != AzureAuthMode.ConnectionString;

    // Account key shows only for Azure SharedKey auth.
    public bool AzureKeyVisible => IsAzure && AzureAuth == AzureAuthMode.SharedKey;

    // Connection string shows only for Azure ConnectionString auth.
    public bool AzureConnStringVisible => IsAzure && AzureAuth == AzureAuthMode.ConnectionString;

    // SAS token shows only for Azure SAS auth.
    public bool AzureSasVisible => IsAzure && AzureAuth == AzureAuthMode.Sas;

    // Container field is shown for every Azure mode (required for Anonymous).
    public bool AzureContainerVisible => IsAzure;

    // The password box is shown for SFTP password auth and for non-guest SMB.
    public bool PasswordVisible => (IsSftp && IsPasswordMode) || (IsSmb && !Guest);

    // Any Azure mode except Anonymous carries a persistable secret.
    public bool AzureSecretVisible => IsAzure && AzureAuth != AzureAuthMode.Anonymous;

    // The "save secret" checkbox covers SFTP/SMB passwords, the S3 secret key, and Azure secrets.
    public bool SaveSecretVisible => PasswordVisible || S3KeysVisible || AzureSecretVisible;

    // Username is hidden for SMB guest connections and for S3/Azure (which use keys).
    public bool UsernameVisible => IsSftp || (IsSmb && !Guest);

    public bool HostKeyWarningVisible => IsSftp && IsHostKeyChanged;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);
    public bool HasHostKeyWarning => IsHostKeyChanged;

    public bool IsPortValid =>
        int.TryParse(PortText, out var port) && port is >= 1 and <= 65535;

    public event Action<ConnectionInfo>? Connected;
    public event Action<SmbConnectionInfo>? SmbConnected;
    public event Action<S3ConnectionInfo>? S3Connected;
    public event Action<AzureConnectionInfo>? AzureConnected;
    public event Action? Cancelled;

    // Null for a new connection, set when editing an existing one.
    private string? _editingId;

    private readonly SecretCodec _codec;

    public ConnectDialogViewModel(
        ConnectionManager manager,
        ConnectionStore store,
        HostKeyStore hostKeyStore,
        SecretCodec codec,
        SmbConnectionManager smbManager,
        SmbConnectionStore smbStore,
        S3ConnectionManager s3Manager,
        S3ConnectionStore s3Store,
        AzureConnectionManager azureManager,
        AzureConnectionStore azureStore)
    {
        ConnectAction = (info, secret) => manager.Connect(info, secret);
        SmbConnectAction = (info, secret) => smbManager.Connect(info, secret);
        S3ConnectAction = (info, secret) => s3Manager.Connect(info, secret);
        AzureConnectAction = (info, secret) => azureManager.Connect(info, secret);

        SaveAction = stored =>
        {
            var all = store.Load().ToList();
            var idx = all.FindIndex(c => c.Id == stored.Id);
            if (idx >= 0)
                all[idx] = stored;
            else
                all.Add(stored);
            store.Save(all.ToArray());
        };

        SmbSaveAction = stored =>
        {
            var all = smbStore.Load().ToList();
            var idx = all.FindIndex(c => c.Id == stored.Id);
            if (idx >= 0)
                all[idx] = stored;
            else
                all.Add(stored);
            smbStore.Save(all.ToArray());
        };

        S3SaveAction = stored =>
        {
            var all = s3Store.Load().ToList();
            var idx = all.FindIndex(c => c.Id == stored.Id);
            if (idx >= 0)
                all[idx] = stored;
            else
                all.Add(stored);
            s3Store.Save(all.ToArray());
        };

        AzureSaveAction = stored =>
        {
            var all = azureStore.Load().ToList();
            var idx = all.FindIndex(c => c.Id == stored.Id);
            if (idx >= 0)
                all[idx] = stored;
            else
                all.Add(stored);
            azureStore.Save(all.ToArray());
        };

        ForgetKeyAction = storeKey => hostKeyStore.Forget(storeKey);
        _codec = codec;
    }

    // Swap the port default when switching protocols, unless the user already set a custom port.
    partial void OnProtocolChanged(ConnectProtocol value)
    {
        if (value == ConnectProtocol.Smb && PortText == "22")
            PortText = "445";
        else if (value == ConnectProtocol.Sftp && PortText == "445")
            PortText = "22";
    }

    public void ForEdit(StoredConnection stored)
    {
        Protocol = ConnectProtocol.Sftp;
        _editingId = stored.Id;
        Name = stored.Name;
        Host = stored.Host;
        PortText = stored.Port.ToString();
        Username = stored.Username;
        AuthMode = stored.AuthMode;
        KeyPath = stored.KeyPath ?? "";
        InitialRemotePath = stored.InitialRemotePath;
        SavePassword = stored.SavePassword;

        if (stored.SavePassword && !string.IsNullOrEmpty(stored.ObfuscatedSecret))
        {
            var secret = ConnectionStore.ResolveSecret(stored, _codec);
            if (secret is not null)
            {
                if (stored.AuthMode == AuthMode.Password)
                    Password = secret.Password ?? "";
                else
                    KeyPassphrase = secret.KeyPassphrase ?? "";
            }
        }
    }

    public void ForEdit(StoredSmbConnection stored)
    {
        Protocol = ConnectProtocol.Smb;
        _editingId = stored.Id;
        Name = stored.Name;
        Host = stored.Host;
        PortText = stored.Port.ToString();
        Username = stored.Username;
        Domain = stored.Domain;
        Guest = stored.Guest;
        InitialRemotePath = stored.InitialPath;
        SavePassword = stored.SavePassword;

        if (!stored.Guest && stored.SavePassword && !string.IsNullOrEmpty(stored.ObfuscatedSecret))
        {
            var secret = SmbConnectionStore.ResolveSecret(stored, _codec);
            if (secret is not null)
                Password = secret.Password ?? "";
        }
    }

    public void ForEdit(StoredS3Connection stored)
    {
        Protocol = ConnectProtocol.S3;
        _editingId = stored.Id;
        Name = stored.Name;
        Endpoint = stored.Endpoint;
        Region = stored.Region;
        PathStyle = stored.PathStyle;
        S3Auth = stored.AuthMode;
        AccessKeyId = stored.AccessKeyId;
        Profile = stored.Profile;
        Bucket = stored.Bucket;
        InitialRemotePath = stored.InitialPath;
        SavePassword = stored.SavePassword;

        if (stored.AuthMode == S3AuthMode.Keys && stored.SavePassword
            && !string.IsNullOrEmpty(stored.ObfuscatedSecret))
        {
            var secret = S3ConnectionStore.ResolveSecret(stored, _codec);
            if (secret is not null)
            {
                SecretKey = secret.Password ?? "";
                SessionToken = secret.SessionToken ?? "";
            }
        }
    }

    public void ForEdit(StoredAzureConnection stored)
    {
        Protocol = ConnectProtocol.AzureBlob;
        _editingId = stored.Id;
        Name = stored.Name;
        Endpoint = stored.Endpoint;
        AzureAccount = stored.AccountName;
        AzureAuth = stored.AuthMode;
        AzureContainer = stored.Container;
        InitialRemotePath = stored.InitialPath;
        SavePassword = stored.SavePassword;

        if (stored.AuthMode != AzureAuthMode.Anonymous && stored.SavePassword
            && !string.IsNullOrEmpty(stored.ObfuscatedSecret))
        {
            var secret = AzureConnectionStore.ResolveSecret(stored, _codec);
            if (secret?.Password is { } value)
            {
                switch (stored.AuthMode)
                {
                    case AzureAuthMode.SharedKey:
                        AzureAccountKey = value;
                        break;
                    case AzureAuthMode.ConnectionString:
                        AzureConnectionString = value;
                        break;
                    case AzureAuthMode.Sas:
                        AzureSasToken = value;
                        break;
                }
            }
        }
    }

    private string? Validate()
    {
        if (IsS3)
            return ValidateS3();

        if (IsAzure)
            return ValidateAzure();

        if (string.IsNullOrWhiteSpace(Host))
            return "Host is required";
        if (!IsPortValid)
            return "Port must be a number between 1 and 65535";

        if (IsSftp)
        {
            if (string.IsNullOrWhiteSpace(Username))
                return "Username is required";
            if (AuthMode == AuthMode.Key && string.IsNullOrWhiteSpace(KeyPath))
                return "Key file path is required";
            if (AuthMode == AuthMode.Key && !string.IsNullOrEmpty(KeyPath) && !File.Exists(KeyPath))
                return $"Key file not found: {KeyPath}";
        }
        else
        {
            if (!Guest && string.IsNullOrWhiteSpace(Username))
                return "Username is required (or connect as guest)";
        }

        return null;
    }

    private string? ValidateS3() => S3Auth switch
    {
        S3AuthMode.Keys when string.IsNullOrWhiteSpace(AccessKeyId) => "Access key ID is required",
        S3AuthMode.Keys when string.IsNullOrWhiteSpace(SecretKey) => "Secret access key is required",
        S3AuthMode.Profile when string.IsNullOrWhiteSpace(Profile) => "Profile name is required",
        // Anonymous cannot list buckets, so a specific bucket is mandatory.
        S3AuthMode.Anonymous when string.IsNullOrWhiteSpace(Bucket) => "Bucket is required for anonymous access",
        _ => null,
    };

    private string? ValidateAzure() => AzureAuth switch
    {
        AzureAuthMode.SharedKey when string.IsNullOrWhiteSpace(AzureAccount) => "Storage account name is required",
        AzureAuthMode.SharedKey when string.IsNullOrWhiteSpace(AzureAccountKey) => "Account key is required",
        AzureAuthMode.ConnectionString when string.IsNullOrWhiteSpace(AzureConnectionString) => "Connection string is required",
        AzureAuthMode.Sas when string.IsNullOrWhiteSpace(AzureSasToken) => "SAS token is required",
        AzureAuthMode.Sas when string.IsNullOrWhiteSpace(AzureAccount) && string.IsNullOrWhiteSpace(Endpoint) => "Account name or endpoint is required",
        // Anonymous cannot list containers, so a specific container is mandatory.
        AzureAuthMode.Anonymous when string.IsNullOrWhiteSpace(AzureContainer) => "Container is required for anonymous access",
        AzureAuthMode.Anonymous when string.IsNullOrWhiteSpace(AzureAccount) && string.IsNullOrWhiteSpace(Endpoint) => "Account name or endpoint is required",
        _ => null,
    };

    private ConnectionInfo BuildInfo() => new(
        Id: _editingId ?? Guid.NewGuid().ToString("N"),
        Name: string.IsNullOrWhiteSpace(Name) ? Host : Name.Trim(),
        Host: Host.Trim(),
        Port: int.TryParse(PortText, out var p) ? p : 22,
        Username: Username.Trim(),
        AuthMode: AuthMode,
        KeyPath: AuthMode == AuthMode.Key ? KeyPath.Trim() : null,
        InitialRemotePath: string.IsNullOrWhiteSpace(InitialRemotePath) ? "/" : InitialRemotePath.Trim());

    private ConnectSecret BuildSecret() => AuthMode switch
    {
        AuthMode.Key => ConnectSecret.FromKey(string.IsNullOrEmpty(KeyPassphrase) ? null : KeyPassphrase),
        _ => ConnectSecret.FromPassword(Password),
    };

    private SmbConnectionInfo BuildSmbInfo() => new(
        Id: _editingId ?? Guid.NewGuid().ToString("N"),
        Name: string.IsNullOrWhiteSpace(Name) ? Host : Name.Trim(),
        Host: Host.Trim(),
        Port: int.TryParse(PortText, out var p) ? p : 445,
        Username: Guest ? "" : Username.Trim(),
        Domain: Domain.Trim(),
        Guest: Guest,
        InitialPath: string.IsNullOrWhiteSpace(InitialRemotePath) ? "/" : InitialRemotePath.Trim());

    private ConnectSecret BuildSmbSecret() =>
        ConnectSecret.FromPassword(Guest ? "" : Password);

    private S3ConnectionInfo BuildS3Info() => new(
        Id: _editingId ?? Guid.NewGuid().ToString("N"),
        Name: string.IsNullOrWhiteSpace(Name) ? (Endpoint.Trim() is { Length: > 0 } ep ? ep : "S3") : Name.Trim(),
        Endpoint: Endpoint.Trim(),
        Region: Region.Trim(),
        PathStyle: PathStyle,
        AuthMode: S3Auth,
        AccessKeyId: S3Auth == S3AuthMode.Keys ? AccessKeyId.Trim() : "",
        Profile: S3Auth == S3AuthMode.Profile ? Profile.Trim() : "",
        Bucket: Bucket.Trim(),
        InitialPath: string.IsNullOrWhiteSpace(InitialRemotePath) ? "/" : InitialRemotePath.Trim());

    private ConnectSecret BuildS3Secret() =>
        S3Auth == S3AuthMode.Keys
            ? ConnectSecret.FromKeys(SecretKey, string.IsNullOrEmpty(SessionToken) ? null : SessionToken)
            : new ConnectSecret();

    private AzureConnectionInfo BuildAzureInfo() => new(
        Id: _editingId ?? Guid.NewGuid().ToString("N"),
        Name: string.IsNullOrWhiteSpace(Name)
            ? (AzureAccount.Trim() is { Length: > 0 } acct ? acct : "Azure")
            : Name.Trim(),
        Endpoint: Endpoint.Trim(),
        AccountName: AzureAccount.Trim(),
        AuthMode: AzureAuth,
        Container: AzureContainer.Trim(),
        InitialPath: string.IsNullOrWhiteSpace(InitialRemotePath) ? "/" : InitialRemotePath.Trim());

    private ConnectSecret BuildAzureSecret() => AzureAuth switch
    {
        AzureAuthMode.SharedKey => ConnectSecret.FromPassword(AzureAccountKey),
        AzureAuthMode.ConnectionString => ConnectSecret.FromPassword(AzureConnectionString.Trim()),
        AzureAuthMode.Sas => ConnectSecret.FromPassword(AzureSasToken.Trim()),
        _ => new ConnectSecret(),
    };

    [RelayCommand]
    public async Task ConnectAsync()
    {
        ErrorText = "";
        IsHostKeyChanged = false;

        var validationError = Validate();
        if (validationError is not null)
        {
            ErrorText = validationError;
            return;
        }

        if (IsSmb)
        {
            await ConnectSmbAsync();
            return;
        }

        if (IsS3)
        {
            await ConnectS3Async();
            return;
        }

        if (IsAzure)
        {
            await ConnectAzureAsync();
            return;
        }

        var info = BuildInfo();
        // Lock in the editing id so the same GUID is reused if this is an edit.
        _editingId = info.Id;
        var secret = BuildSecret();

        IsConnecting = true;
        try
        {
            await Task.Run(() => ConnectAction(info, secret));
        }
        catch (SshAuthenticationException)
        {
            ErrorText = "Authentication failed. Check your username and password/key.";
            return;
        }
        catch (SocketException ex)
        {
            ErrorText = $"Connection failed: {ex.Message}";
            return;
        }
        catch (SshConnectionException ex)
        {
            ErrorText = $"SSH connection error: {ex.Message}";
            return;
        }
        catch (HostKeyChangedException ex)
        {
            IsHostKeyChanged = true;
            OldFingerprint = ex.OldFingerprint;
            NewFingerprint = ex.NewFingerprint;
            _hostKeyStoreKey = ex.StoreKey;
            ErrorText = $"Host key for '{ex.Host}' has changed. Accept the new key to continue.";
            return;
        }
        catch (ObjectDisposedException)
        {
            ErrorText = "The connection manager was disposed. Restart the application.";
            return;
        }
        catch (SshException ex)
        {
            ErrorText = ex.Message;
            return;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            ErrorText = ex.Message;
            return;
        }
        finally
        {
            IsConnecting = false;
        }

        OnConnectSuccess(info, secret);
    }

    private async Task ConnectSmbAsync()
    {
        var info = BuildSmbInfo();
        _editingId = info.Id;
        var secret = BuildSmbSecret();

        IsConnecting = true;
        try
        {
            await Task.Run(() => SmbConnectAction(info, secret));
        }
        catch (SmbAuthenticationException)
        {
            ErrorText = "Authentication failed. Check your username, password, and domain.";
            return;
        }
        catch (SmbConnectionException ex)
        {
            ErrorText = $"Connection failed: {ex.Message}";
            return;
        }
        catch (SocketException ex)
        {
            ErrorText = $"Connection failed: {ex.Message}";
            return;
        }
        catch (ObjectDisposedException)
        {
            ErrorText = "The connection manager was disposed. Restart the application.";
            return;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            ErrorText = ex.Message;
            return;
        }
        finally
        {
            IsConnecting = false;
        }

        OnSmbConnectSuccess(info, secret);
    }

    private async Task ConnectS3Async()
    {
        var info = BuildS3Info();
        _editingId = info.Id;
        var secret = BuildS3Secret();

        IsConnecting = true;
        try
        {
            await Task.Run(() => S3ConnectAction(info, secret));
        }
        catch (S3AuthenticationException)
        {
            ErrorText = "Authentication failed. Check your access key, secret, or profile.";
            return;
        }
        catch (S3ConnectionException ex)
        {
            ErrorText = $"Connection failed: {ex.Message}";
            return;
        }
        catch (SocketException ex)
        {
            ErrorText = $"Connection failed: {ex.Message}";
            return;
        }
        catch (ObjectDisposedException)
        {
            ErrorText = "The connection manager was disposed. Restart the application.";
            return;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            ErrorText = ex.Message;
            return;
        }
        finally
        {
            IsConnecting = false;
        }

        OnS3ConnectSuccess(info, secret);
    }

    private async Task ConnectAzureAsync()
    {
        var info = BuildAzureInfo();
        _editingId = info.Id;
        var secret = BuildAzureSecret();

        IsConnecting = true;
        try
        {
            await Task.Run(() => AzureConnectAction(info, secret));
        }
        catch (AzureAuthenticationException)
        {
            ErrorText = "Authentication failed. Check your account key, SAS token, or connection string.";
            return;
        }
        catch (AzureConnectionException ex)
        {
            ErrorText = $"Connection failed: {ex.Message}";
            return;
        }
        catch (SocketException ex)
        {
            ErrorText = $"Connection failed: {ex.Message}";
            return;
        }
        catch (ObjectDisposedException)
        {
            ErrorText = "The connection manager was disposed. Restart the application.";
            return;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            ErrorText = ex.Message;
            return;
        }
        finally
        {
            IsConnecting = false;
        }

        OnAzureConnectSuccess(info, secret);
    }

    [RelayCommand]
    public async Task AcceptNewKeyAsync()
    {
        if (!IsHostKeyChanged || string.IsNullOrEmpty(_hostKeyStoreKey))
            return;

        IsHostKeyChanged = false;
        ErrorText = "";

        ForgetKeyAction(_hostKeyStoreKey);
        _hostKeyStoreKey = "";

        var info = BuildInfo();
        var secret = BuildSecret();

        IsConnecting = true;
        try
        {
            await Task.Run(() => ConnectAction(info, secret));
        }
        catch (SshAuthenticationException)
        {
            ErrorText = "Authentication failed after accepting new key.";
            return;
        }
        catch (SocketException ex)
        {
            ErrorText = $"Connection failed: {ex.Message}";
            return;
        }
        catch (SshConnectionException ex)
        {
            ErrorText = $"SSH connection error: {ex.Message}";
            return;
        }
        catch (HostKeyChangedException ex)
        {
            IsHostKeyChanged = true;
            OldFingerprint = ex.OldFingerprint;
            NewFingerprint = ex.NewFingerprint;
            _hostKeyStoreKey = ex.StoreKey;
            ErrorText = $"Host key changed again for '{ex.Host}'. Manual intervention required.";
            return;
        }
        catch (ObjectDisposedException)
        {
            ErrorText = "The connection manager was disposed. Restart the application.";
            return;
        }
        catch (SshException ex)
        {
            ErrorText = ex.Message;
            return;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            ErrorText = ex.Message;
            return;
        }
        finally
        {
            IsConnecting = false;
        }

        OnConnectSuccess(info, secret);
    }

    [RelayCommand]
    public void Cancel() => Cancelled?.Invoke();

    private void OnConnectSuccess(ConnectionInfo info, ConnectSecret secret)
    {
        var stored = ConnectionStore.Pack(info, secret, SavePassword, _codec);
        SaveAction(stored);
        Connected?.Invoke(info);
    }

    private void OnSmbConnectSuccess(SmbConnectionInfo info, ConnectSecret secret)
    {
        var stored = SmbConnectionStore.Pack(info, secret, SavePassword, _codec);
        SmbSaveAction(stored);
        SmbConnected?.Invoke(info);
    }

    private void OnS3ConnectSuccess(S3ConnectionInfo info, ConnectSecret secret)
    {
        var stored = S3ConnectionStore.Pack(info, secret, SavePassword, _codec);
        S3SaveAction(stored);
        S3Connected?.Invoke(info);
    }

    private void OnAzureConnectSuccess(AzureConnectionInfo info, ConnectSecret secret)
    {
        var stored = AzureConnectionStore.Pack(info, secret, SavePassword, _codec);
        AzureSaveAction(stored);
        AzureConnected?.Invoke(info);
    }
}

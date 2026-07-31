using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.Remote;
using Renci.SshNet.Common;

namespace Duetto.ViewModels;

public partial class ConnectDialogViewModel : ObservableObject
{
    // Runs on a background thread; tests inject a fake that opens no sockets.
    public Action<ConnectionInfo, ConnectSecret> ConnectAction { get; set; }

    public Action<StoredConnection> SaveAction { get; set; }

    // Removes a stale host-key pin so the next connect attempt re-pins.
    public Action<string> ForgetKeyAction { get; set; }

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
    [NotifyPropertyChangedFor(nameof(IsPasswordMode), nameof(IsKeyMode))]
    private AuthMode _authMode = AuthMode.Password;

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _keyPath = "";

    [ObservableProperty]
    private string _keyPassphrase = "";

    [ObservableProperty]
    private string _initialRemotePath = "/";

    [ObservableProperty]
    private bool _savePassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = "";

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHostKeyWarning))]
    private bool _isHostKeyChanged;

    [ObservableProperty]
    private string _oldFingerprint = "";

    [ObservableProperty]
    private string _newFingerprint = "";

    private string _hostKeyStoreKey = "";

    public bool IsPasswordMode => AuthMode == AuthMode.Password;
    public bool IsKeyMode => AuthMode == AuthMode.Key;
    public bool HasError => !string.IsNullOrEmpty(ErrorText);
    public bool HasHostKeyWarning => IsHostKeyChanged;

    public bool IsPortValid =>
        int.TryParse(PortText, out var port) && port is >= 1 and <= 65535;

    public event Action<ConnectionInfo>? Connected;
    public event Action? Cancelled;

    // Null for a new connection, set when editing an existing one.
    private string? _editingId;

    public ConnectDialogViewModel(
        ConnectionManager manager,
        ConnectionStore store,
        HostKeyStore hostKeyStore,
        SecretCodec codec)
    {
        ConnectAction = (info, secret) => manager.Connect(info, secret);

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

        ForgetKeyAction = storeKey => hostKeyStore.Forget(storeKey);
        _codec = codec;
    }

    private readonly SecretCodec _codec;

    public void ForEdit(StoredConnection stored)
    {
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

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            return "Host is required";
        if (string.IsNullOrWhiteSpace(Username))
            return "Username is required";
        if (!IsPortValid)
            return "Port must be a number between 1 and 65535";
        if (AuthMode == AuthMode.Key && string.IsNullOrWhiteSpace(KeyPath))
            return "Key file path is required";
        if (AuthMode == AuthMode.Key && !string.IsNullOrEmpty(KeyPath) && !File.Exists(KeyPath))
            return $"Key file not found: {KeyPath}";
        return null;
    }

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
}

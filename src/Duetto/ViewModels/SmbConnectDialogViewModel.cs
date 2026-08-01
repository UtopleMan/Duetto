using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.Remote;

namespace Duetto.ViewModels;

// SMB counterpart of ConnectDialogViewModel: user/password/domain or guest, no SSH key auth and
// no host-key pinning. Persists to the separate SMB store.
public partial class SmbConnectDialogViewModel : ObservableObject
{
    // Runs on a background thread; tests inject a fake that opens no sockets.
    public Action<SmbConnectionInfo, ConnectSecret> ConnectAction { get; set; }

    public Action<StoredSmbConnection> SaveAction { get; set; }

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _host = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPortValid))]
    private string _portText = "445";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _domain = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CredentialsVisible))]
    private bool _guest;

    [ObservableProperty]
    private string _initialPath = "/";

    [ObservableProperty]
    private bool _savePassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = "";

    [ObservableProperty]
    private bool _isConnecting;

    // Guest connections need no username/password; hide those fields.
    public bool CredentialsVisible => !Guest;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public bool IsPortValid =>
        int.TryParse(PortText, out var port) && port is >= 1 and <= 65535;

    public event Action<SmbConnectionInfo>? Connected;
    public event Action? Cancelled;

    // Null for a new connection, set when editing an existing one.
    private string? _editingId;

    public SmbConnectDialogViewModel(SmbConnectionManager manager, SmbConnectionStore store, SecretCodec codec)
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

        _codec = codec;
    }

    private readonly SecretCodec _codec;

    public void ForEdit(StoredSmbConnection stored)
    {
        _editingId = stored.Id;
        Name = stored.Name;
        Host = stored.Host;
        PortText = stored.Port.ToString();
        Username = stored.Username;
        Domain = stored.Domain;
        Guest = stored.Guest;
        InitialPath = stored.InitialPath;
        SavePassword = stored.SavePassword;

        if (!stored.Guest && stored.SavePassword && !string.IsNullOrEmpty(stored.ObfuscatedSecret))
        {
            var secret = SmbConnectionStore.ResolveSecret(stored, _codec);
            if (secret is not null)
                Password = secret.Password ?? "";
        }
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            return "Host is required";
        if (!Guest && string.IsNullOrWhiteSpace(Username))
            return "Username is required (or connect as guest)";
        if (!IsPortValid)
            return "Port must be a number between 1 and 65535";
        return null;
    }

    private SmbConnectionInfo BuildInfo() => new(
        Id: _editingId ?? Guid.NewGuid().ToString("N"),
        Name: string.IsNullOrWhiteSpace(Name) ? Host : Name.Trim(),
        Host: Host.Trim(),
        Port: int.TryParse(PortText, out var p) ? p : 445,
        Username: Guest ? "" : Username.Trim(),
        Domain: Domain.Trim(),
        Guest: Guest,
        InitialPath: string.IsNullOrWhiteSpace(InitialPath) ? "/" : InitialPath.Trim());

    private ConnectSecret BuildSecret() =>
        ConnectSecret.FromPassword(Guest ? "" : Password);

    [RelayCommand]
    public async Task ConnectAsync()
    {
        ErrorText = "";

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

        OnConnectSuccess(info, secret);
    }

    [RelayCommand]
    public void Cancel() => Cancelled?.Invoke();

    private void OnConnectSuccess(SmbConnectionInfo info, ConnectSecret secret)
    {
        var stored = SmbConnectionStore.Pack(info, secret, SavePassword, _codec);
        SaveAction(stored);
        Connected?.Invoke(info);
    }
}

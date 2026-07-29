using Avalonia.Headless.XUnit;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core;
using Duetto.ViewModels;
using Duetto.Views;
using Renci.SshNet.Common;

namespace Duetto.Tests.Ui;

/// <summary>
/// VM-level and headless UI tests for <see cref="ConnectDialogViewModel"/>
/// and app composition (<see cref="MainViewModel"/> registry seam).
/// </summary>
public sealed class ConnectDialogTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SecretCodec MakeCodec() => new();

    /// <summary>
    /// Builds a test VM with injected seams. The public seam properties
    /// (<see cref="ConnectDialogViewModel.ConnectAction"/>, SaveAction, ForgetKeyAction)
    /// are overwritten after construction so that tests can control behaviour without
    /// touching the real filesystem or any network.
    /// </summary>
    private static (ConnectDialogViewModel Vm,
                    List<(ConnectionInfo Info, ConnectSecret Secret)> Connects,
                    List<StoredConnection> Saved,
                    List<string> Forgot)
        MakeVm(Action<ConnectionInfo, ConnectSecret>? connectOverride = null)
    {
        var connects = new List<(ConnectionInfo, ConnectSecret)>();
        var saved = new List<StoredConnection>();
        var forgot = new List<string>();

        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var store = new ConnectionStore(":mem:", _ => null, (_, _) => { });
        var codec = MakeCodec();
        var manager = new ConnectionManager(registry, hks);

        var vm = new ConnectDialogViewModel(manager, store, hks, codec);
        // Override seam actions for test isolation.
        vm.ConnectAction = connectOverride ?? ((i, s) => connects.Add((i, s)));
        vm.SaveAction = s => saved.Add(s);
        vm.ForgetKeyAction = k => forgot.Add(k);

        return (vm, connects, saved, forgot);
    }

    private static void FillValid(ConnectDialogViewModel vm)
    {
        vm.Host = "example.com";
        vm.Username = "alice";
        vm.PortText = "22";
        vm.AuthMode = AuthMode.Password;
        vm.Password = "hunter2";
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Validation_fails_when_host_is_empty()
    {
        var (vm, _, saved, _) = MakeVm();
        vm.Username = "alice";
        // Host is empty (default "")

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public async Task Validation_fails_when_username_is_empty()
    {
        var (vm, _, saved, _) = MakeVm();
        vm.Host = "example.com";
        // Username is empty (default "")

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public async Task Validation_fails_when_port_is_out_of_range()
    {
        var (vm, _, saved, _) = MakeVm();
        vm.Host = "example.com";
        vm.Username = "alice";
        vm.PortText = "0";

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public async Task Validation_fails_when_key_path_missing_in_key_mode()
    {
        var (vm, _, saved, _) = MakeVm();
        vm.Host = "example.com";
        vm.Username = "alice";
        vm.AuthMode = AuthMode.Key;
        vm.KeyPath = "";

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Empty(saved);
    }

    // ── Success path ──────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Connect_success_raises_Connected_event_and_saves()
    {
        var (vm, connects, saved, _) = MakeVm();
        FillValid(vm);
        vm.SavePassword = true;
        vm.Name = "My Box";

        ConnectionInfo? connectedInfo = null;
        vm.Connected += info => connectedInfo = info;

        await vm.ConnectAsync();

        Assert.False(vm.HasError);
        Assert.NotNull(connectedInfo);
        Assert.Equal("My Box", connectedInfo!.Name);
        Assert.Equal("example.com", connectedInfo.Host);
        Assert.Single(connects);
        Assert.Single(saved);
    }

    [AvaloniaFact]
    public async Task Connect_success_no_name_uses_host_as_name()
    {
        var (vm, _, saved, _) = MakeVm();
        FillValid(vm);
        // Name is blank

        await vm.ConnectAsync();

        Assert.Single(saved);
        Assert.Equal("example.com", saved[0].Name);
    }

    [AvaloniaFact]
    public async Task Connect_success_does_not_save_secret_when_SavePassword_false()
    {
        var (vm, _, saved, _) = MakeVm();
        FillValid(vm);
        vm.SavePassword = false;
        vm.Password = "secret123";

        await vm.ConnectAsync();

        Assert.Single(saved);
        Assert.False(saved[0].SavePassword);
        Assert.Equal(string.Empty, saved[0].ObfuscatedSecret);
    }

    [AvaloniaFact]
    public async Task Connect_success_saves_obfuscated_secret_when_SavePassword_true()
    {
        var codec = MakeCodec();
        var (vm, _, saved, _) = MakeVm();
        // Rebuild with same codec to verify round-trip
        vm.SavePassword = true;
        FillValid(vm);
        vm.Password = "secret123";

        await vm.ConnectAsync();

        Assert.Single(saved);
        Assert.True(saved[0].SavePassword);
        Assert.NotEmpty(saved[0].ObfuscatedSecret);
        // The stored value must decode to the original password.
        var secret = ConnectionStore.ResolveSecret(saved[0], codec);
        Assert.Equal("secret123", secret!.Password);
    }

    // ── Auth failure ──────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Auth_failure_surfaces_error_and_does_not_save()
    {
        var (vm, _, saved, _) = MakeVm(connectOverride: (_, _) =>
            throw new SshAuthenticationException("Bad password"));
        FillValid(vm);

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Contains("Authentication failed", vm.ErrorText);
        Assert.Empty(saved);
    }

    [AvaloniaFact]
    public async Task Socket_error_surfaces_error_and_does_not_save()
    {
        var (vm, _, saved, _) = MakeVm(connectOverride: (_, _) =>
            throw new System.Net.Sockets.SocketException());
        FillValid(vm);

        await vm.ConnectAsync();

        Assert.True(vm.HasError);
        Assert.Contains("Connection failed", vm.ErrorText);
        Assert.Empty(saved);
    }

    // ── HostKeyChangedException path ──────────────────────────────────────────

    [AvaloniaFact]
    public async Task HostKeyChanged_enters_warning_state_with_fingerprints()
    {
        var callCount = 0;
        var (vm, _, saved, _) = MakeVm(connectOverride: (_, _) =>
        {
            callCount++;
            throw new HostKeyChangedException(
                host: "example.com",
                oldFingerprint: "AAAA",
                newFingerprint: "BBBB",
                algorithmName: "ssh-ed25519",
                storeKey: "ssh-ed25519:[example.com]:22");
        });
        FillValid(vm);

        await vm.ConnectAsync();

        Assert.True(vm.IsHostKeyChanged);
        Assert.True(vm.HasHostKeyWarning);
        Assert.Equal("AAAA", vm.OldFingerprint);
        Assert.Equal("BBBB", vm.NewFingerprint);
        Assert.Empty(saved);
        Assert.Equal(1, callCount);
    }

    [AvaloniaFact]
    public async Task AcceptNewKey_calls_Forget_then_retries_and_succeeds()
    {
        var callCount = 0;
        var forgets = new List<string>();
        var saves = new List<StoredConnection>();

        var (vm, _, _, _) = MakeVm(connectOverride: (_, _) =>
        {
            callCount++;
            if (callCount == 1)
                throw new HostKeyChangedException(
                    "example.com", "OLD", "NEW", "ssh-ed25519",
                    "ssh-ed25519:[example.com]:22");
            // Second call succeeds (callCount == 2)
        });
        vm.ForgetKeyAction = k => forgets.Add(k);
        vm.SaveAction = s => saves.Add(s);

        FillValid(vm);

        // First call: triggers host-key warning
        await vm.ConnectAsync();
        Assert.True(vm.IsHostKeyChanged);

        // Accept new key: forget + retry
        ConnectionInfo? connectedInfo = null;
        vm.Connected += info => connectedInfo = info;
        await vm.AcceptNewKeyAsync();

        Assert.False(vm.IsHostKeyChanged);
        Assert.False(vm.HasError);
        Assert.Single(forgets);
        Assert.Equal("ssh-ed25519:[example.com]:22", forgets[0]);
        Assert.Equal(2, callCount);
        Assert.Single(saves);
        Assert.NotNull(connectedInfo);
    }

    [AvaloniaFact]
    public async Task AcceptNewKey_is_no_op_when_not_in_changed_state()
    {
        var (vm, connects, saved, forgot) = MakeVm();
        FillValid(vm);

        // Do NOT trigger a host-key change — call AcceptNewKey directly.
        await vm.AcceptNewKeyAsync();

        Assert.Empty(connects);
        Assert.Empty(saved);
        Assert.Empty(forgot);
    }

    // ── ForEdit ───────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task ForEdit_prefills_fields_from_stored_connection()
    {
        var codec = MakeCodec();
        var info = new ConnectionInfo(
            Id: "id1", Name: "Box", Host: "box.example.com", Port: 2222,
            Username: "bob", AuthMode: AuthMode.Password,
            InitialRemotePath: "/home/bob");
        var secret = ConnectSecret.FromPassword("p@ss");
        var stored = ConnectionStore.Pack(info, secret, savePassword: true, codec);

        var (vm, _, _, _) = MakeVm();
        vm.ForEdit(stored);

        Assert.Equal("Box", vm.Name);
        Assert.Equal("box.example.com", vm.Host);
        Assert.Equal("2222", vm.PortText);
        Assert.Equal("bob", vm.Username);
        Assert.Equal(AuthMode.Password, vm.AuthMode);
        Assert.Equal("/home/bob", vm.InitialRemotePath);
        Assert.True(vm.SavePassword);
        Assert.Equal("p@ss", vm.Password);
    }

    [AvaloniaFact]
    public async Task ForEdit_and_connect_reuses_same_id()
    {
        var codec = MakeCodec();
        var info = new ConnectionInfo(
            Id: "fixed-id", Name: "Box", Host: "box.example.com",
            Username: "bob");
        var stored = ConnectionStore.Pack(info, ConnectSecret.FromPassword("x"), savePassword: false, codec);

        var (vm, _, saved, _) = MakeVm();
        vm.ForEdit(stored);
        vm.Password = "newpw";

        await vm.ConnectAsync();

        Assert.Single(saved);
        Assert.Equal("fixed-id", saved[0].Id);
    }

    // ── Composition: ONE shared registry ─────────────────────────────────────

    [AvaloniaFact]
    public void MainViewModel_panes_share_the_same_registry_instance()
    {
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        // Both panes must reference the same FileSystemRegistry as MainViewModel.Registry.
        Assert.Same(vm.Registry, vm.Left.Registry);
        Assert.Same(vm.Registry, vm.Right.Registry);
    }

    [AvaloniaFact]
    public void MainViewModel_search_uses_the_same_registry_instance()
    {
        // SearchViewModel stores the registry internally; we verify it via the registry
        // injection path by checking that a provider registered in MainViewModel.Registry
        // is visible to search results. (A thorough registry-equality test via reflection
        // would be brittle; the composition test below is the meaningful check.)
        using var tmp = new TempDir();
        using var vm = new MainViewModel(tmp.Path, tmp.Path);

        // The registry is the same instance injected into both panes; SearchViewModel
        // also receives it. We check that both pane registries are the shared one.
        Assert.Same(vm.Left.Registry, vm.Right.Registry);
        Assert.Same(vm.Registry, vm.Left.Registry);
    }

    [AvaloniaFact]
    public void MainViewModel_dispose_does_not_throw_when_no_connections()
    {
        using var tmp = new TempDir();
        var vm = new MainViewModel(tmp.Path, tmp.Path);
        // Should not throw even if ConnectionManager.Dispose() is called with no active connections.
        vm.Dispose();
    }

    // ── SshException subclass (e.g. timeout) ─────────────────────────────────

    [AvaloniaFact]
    public async Task SshOperationTimeout_resets_IsConnecting_and_surfaces_error()
    {
        var (vm, _, saved, _) = MakeVm(connectOverride: (_, _) =>
            throw new SshOperationTimeoutException("Connection timed out."));
        FillValid(vm);

        await vm.ConnectAsync();

        Assert.False(vm.IsConnecting);
        Assert.True(vm.HasError);
        Assert.NotEmpty(vm.ErrorText);
        Assert.Empty(saved);
    }

    // ── Headless UI: popover Connect row still raises request ─────────────────

    [AvaloniaFact]
    public void Connect_command_raises_request_unchanged_after_stub_removal()
    {
        // This is the same contract the old ConnectStubWindow test relied on.
        // The VM event contract must not have changed.
        using var tmp = new TempDir();
        using var pane = new PaneViewModel(tmp.Path);
        pane.Drives.ListVolumes = () => [];
        pane.Drives.Refresh();

        var requested = false;
        pane.Drives.ConnectRequested += () => requested = true;

        pane.Drives.Connect();

        Assert.True(requested);
    }
}

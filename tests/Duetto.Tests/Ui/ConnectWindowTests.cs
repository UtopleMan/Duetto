using Avalonia.Headless.XUnit;
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core.Remote;
using Duetto.ViewModels;
using Duetto.Views;

namespace Duetto.Tests.Ui;

// Constructs the real ConnectWindow so the (compiled) XAML actually loads and the protocol
// dropdown wiring runs — catches resource/binding/name errors the VM-only tests can't.
public sealed class ConnectWindowTests
{
    private static ConnectDialogViewModel MakeVm()
    {
        var registry = new FileSystemRegistry();
        var hks = new HostKeyStore();
        var store = new ConnectionStore(":mem:", _ => null, (_, _) => { });
        var smbStore = new SmbConnectionStore(":mem:", _ => null, (_, _) => { });
        var manager = new ConnectionManager(registry, hks);
        var smbManager = new SmbConnectionManager(registry, new FakeSmbFactory(new FakeSmbClientAdapter()));
        var s3Store = new S3ConnectionStore(":mem:", _ => null, (_, _) => { });
        var s3Manager = new S3ConnectionManager(registry);
        return new ConnectDialogViewModel(manager, store, hks, new SecretCodec(), smbManager, smbStore, s3Manager, s3Store, new AzureConnectionManager(registry), new AzureConnectionStore(":mem:", _ => null, (_, _) => { }));
    }

    [AvaloniaFact]
    public void Window_loads_for_sftp_default()
    {
        var vm = MakeVm();
        var window = new ConnectWindow(vm);
        window.Show();

        Assert.Same(vm, window.DataContext);
        Assert.True(vm.IsSftp);
        Assert.True(vm.SftpAuthVisible);
        Assert.False(vm.SmbFieldsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void Window_loads_for_smb_when_editing_an_smb_connection()
    {
        var vm = MakeVm();
        vm.ForEdit(new StoredSmbConnection
        {
            Id = "s1", Name = "NAS", Host = "nas.local", Port = 445,
            Username = "u", Domain = "WORKGROUP", InitialPath = "/media",
        });

        var window = new ConnectWindow(vm);
        window.Show();

        Assert.Equal(ConnectProtocol.Smb, vm.Protocol);
        Assert.True(vm.SmbFieldsVisible);
        Assert.False(vm.SftpAuthVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void Switching_protocol_on_the_vm_updates_field_visibility()
    {
        var vm = MakeVm();
        var window = new ConnectWindow(vm);
        window.Show();

        Assert.True(vm.SftpAuthVisible);
        Assert.False(vm.SmbFieldsVisible);

        vm.Protocol = ConnectProtocol.Smb;

        Assert.True(vm.SmbFieldsVisible);
        Assert.False(vm.SftpAuthVisible);
        Assert.Equal("445", vm.PortText);

        window.Close();
    }

    [AvaloniaFact]
    public void Window_loads_for_s3_and_binds_the_s3_fields()
    {
        var vm = MakeVm();
        var window = new ConnectWindow(vm);
        window.Show();

        vm.Protocol = ConnectProtocol.S3;

        Assert.True(vm.IsS3);
        Assert.True(vm.S3FieldsVisible);
        Assert.True(vm.S3KeysVisible);
        Assert.False(vm.HostPortVisible);

        window.Close();
    }
}

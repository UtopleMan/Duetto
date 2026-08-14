using Renci.SshNet.Common;

namespace Duetto.Core.Remote;

public sealed class HostKeyStore
{
    private readonly Dictionary<string, string> _pins = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHostKeyPersistence _persistence;
    private readonly object _lock = new();

    public HostKeyStore(IHostKeyPersistence? persistence = null)
    {
        _persistence = persistence ?? new NullHostKeyPersistence();

        foreach (var (key, fp) in _persistence.LoadAll())
            _pins[key] = fp;
    }

    public static string MakeStoreKey(string algoName, string host, int port) =>
        $"{algoName}:[{host}]:{port}";

    public void HandleHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        string host;
        int port;
        if (sender is Renci.SshNet.IBaseClient c)
        {
            host = c.ConnectionInfo.Host;
            port = c.ConnectionInfo.Port;
        }
        else
        {
            host = "<unknown>";
            port = 22;
        }

        e.CanTrust = false;
        e.CanTrust = Verify(host, port, e.HostKeyName, e.FingerPrintSHA256);
    }

    public bool Verify(string host, int port, string algoName, string fingerprint)
    {
        var storeKey = MakeStoreKey(algoName, host, port);

        lock (_lock)
        {
            if (_pins.TryGetValue(storeKey, out var stored))
            {
                if (stored == fingerprint)
                    return true;

                throw new HostKeyChangedException(host, stored, fingerprint, algoName, storeKey);
            }

            var hostSuffix = $":[{host}]:{port}";
            var existingPin = _pins
                .FirstOrDefault(kv => kv.Key.EndsWith(hostSuffix, StringComparison.OrdinalIgnoreCase));

            if (existingPin.Key is not null)
            {
                throw new HostKeyChangedException(
                    host,
                    oldFingerprint: existingPin.Value,
                    newFingerprint: fingerprint,
                    algorithmName: algoName,
                    storeKey: existingPin.Key);
            }

            _pins[storeKey] = fingerprint;
            _persistence.Save(storeKey, fingerprint);
            return true;
        }
    }

    public string? GetPinned(string storeKey)
    {
        lock (_lock)
            return _pins.TryGetValue(storeKey, out var fp) ? fp : null;
    }

    public bool Forget(string storeKey)
    {
        lock (_lock)
        {
            if (!_pins.Remove(storeKey))
                return false;

            _persistence.Remove(storeKey);
            return true;
        }
    }
}

public interface IHostKeyPersistence
{
    IReadOnlyDictionary<string, string> LoadAll();

    void Save(string storeKey, string fingerprint);

    void Remove(string storeKey);
}

public sealed class NullHostKeyPersistence : IHostKeyPersistence
{
    public IReadOnlyDictionary<string, string> LoadAll() =>
        new Dictionary<string, string>();

    public void Save(string storeKey, string fingerprint) { }

    public void Remove(string storeKey) { }
}

using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

// Hands every connection the same in-memory adapter, so a src + dest provider that share a
// connection id also share one blob store (mirrors one real client reaching all its containers).
internal sealed class FakeAzureClientFactory(FakeAzureClientAdapter adapter) : IAzureClientFactory
{
    public IAzureClientAdapter Create(AzureConnectionInfo info, ConnectSecret secret) => adapter;
}

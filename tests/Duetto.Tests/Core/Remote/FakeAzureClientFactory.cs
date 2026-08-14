using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

internal sealed class FakeAzureClientFactory(FakeAzureClientAdapter adapter) : IAzureClientFactory
{
    public IAzureClientAdapter Create(AzureConnectionInfo info, ConnectSecret secret) => adapter;
}

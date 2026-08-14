using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

internal sealed class FakeS3ClientFactory(FakeS3ClientAdapter adapter) : IS3ClientFactory
{
    public IS3ClientAdapter Create(S3ConnectionInfo info, ConnectSecret secret) => adapter;
}

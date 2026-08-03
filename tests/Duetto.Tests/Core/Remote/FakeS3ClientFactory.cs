using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

// Hands every connection the same in-memory adapter, so a src + dest provider that share a
// connection id also share one object store (mirrors one real S3 client reaching all its buckets).
internal sealed class FakeS3ClientFactory(FakeS3ClientAdapter adapter) : IS3ClientFactory
{
    public IS3ClientAdapter Create(S3ConnectionInfo info, ConnectSecret secret) => adapter;
}

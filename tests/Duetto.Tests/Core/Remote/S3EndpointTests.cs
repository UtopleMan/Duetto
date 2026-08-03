using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class S3EndpointTests
{
    [Theory]
    [InlineData("minio.example.ts.net", "https://minio.example.ts.net")]
    [InlineData("  minio.example.ts.net  ", "https://minio.example.ts.net")]
    [InlineData("http://host:9000", "http://host:9000")]
    [InlineData("https://s3.example.com", "https://s3.example.com")]
    [InlineData("", "")]
    public void NormalizeEndpoint_adds_https_scheme_only_when_missing(string input, string expected)
    {
        Assert.Equal(expected, RealS3ClientAdapter.NormalizeEndpoint(input));
    }

    // Regression: a bare-host endpoint used to throw AmazonClientException ("not a valid URL") that
    // escaped every catch clause and crashed the app. It must now surface as a translated
    // S3ConnectionException (which the connect dialog catches and shows).
    [Fact]
    public void Bare_host_endpoint_surfaces_S3ConnectionException_not_a_raw_sdk_exception()
    {
        var info = new S3ConnectionInfo("t", "t", Endpoint: "minio.example.invalid",
            AuthMode: S3AuthMode.Keys, AccessKeyId: "admin");
        using var conn = new S3Connection(info, ConnectSecret.FromKeys("secret"));

        Assert.Throws<S3ConnectionException>(() => conn.Connect());
    }
}

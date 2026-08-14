using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class AzureEndpointTests
{
    [Theory]
    [InlineData("blob.example.ts.net", "https://blob.example.ts.net")]
    [InlineData("  blob.example.ts.net  ", "https://blob.example.ts.net")]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1", "http://127.0.0.1:10000/devstoreaccount1")]
    [InlineData("https://acct.blob.core.windows.net", "https://acct.blob.core.windows.net")]
    [InlineData("", "")]
    public void NormalizeEndpoint_adds_https_scheme_only_when_missing(string input, string expected)
    {
        Assert.Equal(expected, RealAzureClientAdapter.NormalizeEndpoint(input));
    }

    [Fact]
    public void Malformed_connection_string_surfaces_AzureConnectionException_not_a_raw_sdk_exception()
    {
        var info = new AzureConnectionInfo("t", "t", AuthMode: AzureAuthMode.ConnectionString);
        using var conn = new AzureConnection(info, ConnectSecret.FromPassword("this-is-not-a-valid-connection-string"));

        Assert.Throws<AzureConnectionException>(() => conn.Connect());
    }
}

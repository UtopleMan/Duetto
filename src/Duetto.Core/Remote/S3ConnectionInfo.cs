namespace Duetto.Core.Remote;

public sealed record S3ConnectionInfo(
    string Id,
    string Name,
    string Endpoint = "",
    string Region = "",
    bool PathStyle = false,
    S3AuthMode AuthMode = S3AuthMode.Keys,
    string AccessKeyId = "",
    string Profile = "",
    string Bucket = "",
    string InitialPath = "/");

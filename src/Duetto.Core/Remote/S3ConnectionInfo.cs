namespace Duetto.Core.Remote;

// Secrets (secret access key + optional session token) are NOT stored here — they are supplied at
// connect time via ConnectSecret. Endpoint is blank for real AWS (Region then selects the
// endpoint); a non-blank Endpoint (e.g. http://127.0.0.1:9000) targets an S3-compatible server and
// usually needs PathStyle=true. Bucket blank means the root lists all buckets; a set Bucket scopes
// the root to that one bucket (and is required for Anonymous auth).
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

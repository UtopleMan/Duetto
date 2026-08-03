namespace Duetto.Core.Remote;

// Keys: static access-key-id + secret-access-key, optionally a temporary STS session token.
// Profile: named profile resolved from the shared AWS credentials file (~/.aws/credentials).
// Anonymous: no credentials (public read-only); a Bucket is required because anonymous principals
// cannot call ListBuckets.
public enum S3AuthMode
{
    Keys,
    Profile,
    Anonymous,
}

# Remote connections (S3 / S3-compatible)

Duetto can browse and transfer files over Amazon S3 and any S3-compatible object
store (MinIO, Cloudflare R2, Wasabi, Backblaze B2, DigitalOcean Spaces, on-prem
Ceph) alongside the local filesystem, using the official
[AWS SDK for .NET](https://github.com/aws/aws-sdk-net) — no OS mount required.

## Adding a connection

Open the drive popover by clicking the volume chip in the path bar, choose
**Connect…** (⌘K / Ctrl K), then set **Protocol** to **S3 / S3-compatible**.
Fill in:

| Field | Notes |
|---|---|
| Name | Display name for the connection (shown in the popover) |
| Endpoint | **Leave blank for real AWS** (the Region then selects the endpoint). For an S3-compatible server, enter its URL, e.g. `http://127.0.0.1:9000` (MinIO). The URL carries its own port — there is no separate port field |
| Region | AWS region (e.g. `us-east-1`). Ignored by most S3-compatible servers, but MinIO/R2 still accept a value |
| Use path-style addressing | Check for MinIO / on-prem servers that don't support virtual-hosted-style buckets. AWS uses virtual-hosted style (unchecked) |
| Credentials | **Access keys** (access-key ID + secret, optional session token), **AWS profile** (a named profile from `~/.aws/credentials`), or **Anonymous** (public read-only) |
| Bucket | **Leave blank to list all buckets** at the connection root. Set a bucket to scope the root to it — required for **Anonymous** (anonymous credentials cannot list buckets) and useful for credentials scoped to a single bucket |
| Initial path | Directory to open on connect. Leave `/` to land on the bucket list; use `/bucket` or `/bucket/prefix` to open a specific place |
| Save secret | When checked, the secret access key (and session token) are stored obfuscated in `s3-connections.json` (see caveat below). Profile and Anonymous store no secret |

Click **Connect**.

The **root of a connection is the list of buckets** — `s3://<id>/` shows each
bucket as a folder; open one to browse its objects (`s3://<id>/<bucket>/<key>`).
When a **Bucket** is set, the root is scoped to that single bucket.

Saved connections appear in the **CONNECTED SHARES** section of the drive
popover, tagged **S3** (SFTP/SMB connections are tagged **SFTP**/**SMB** in the
same list). Click a connection to connect and navigate to it. A **Disconnect**
row appears when a remote pane is active.

## Where configuration lives

| Platform | Directory |
|---|---|
| macOS | `~/Library/Application Support/Duetto/` |
| Linux | `$XDG_CONFIG_HOME/duetto/` or `~/.config/duetto/` |
| Windows | `%APPDATA%\Duetto\` |

S3 profiles are stored separately from SFTP and SMB:

- `s3-connections.json` — saved S3 profiles (name, endpoint, region, path-style
  flag, auth mode, access-key ID, profile name, bucket, initial path,
  save-secret flag, obfuscated secret + session token).

## Security caveat

Saved S3 secrets are **obfuscated** using a machine-derived key (AES-256-CBC,
SHA-256 of a machine identifier). This is reversible obfuscation, **not** secure
encrypted storage. Anyone with read access to `s3-connections.json` and the same
machine identity can recover the plaintext. For sensitive credentials, leave
**Save secret** unchecked and enter the key at connect time, or use an **AWS
profile** so the credentials live in `~/.aws/credentials` instead.

## Object-store semantics

S3 is object storage, not a filesystem, so a few operations behave differently
from local / SFTP / SMB:

- **Folders are key prefixes.** Creating a folder writes a zero-byte `prefix/`
  marker object so the empty folder is visible.
- **Delete is permanent** — there is no trash on S3.
- **Rename / move a file** is a server-side copy + delete. When both panes are on
  the same S3 connection, copy and move are offloaded to the server
  (`CopyObject`) with no bytes crossing the client. Renaming or moving a *folder*
  in place is not supported (bulk key-copy); move a folder's *contents* between
  panes instead, which the transfer engine walks file-by-file.
- **No `.part` staging** — an S3 upload only becomes visible once it completes, so
  a failed transfer never exposes a partial object.
- **No modification-time preservation** — S3 owns each object's `LastModified`.
- **Large uploads** use multipart automatically; large single-part server-side
  copies (over 5 GiB) fall back to streaming through the client.

Anonymous connections are read-only in practice (writes return access-denied) and
require a **Bucket**, because anonymous credentials cannot list buckets.

## Running the S3 integration tests

The unit tests use an in-memory fake, so `dotnet test` needs no server. To
exercise the real client end-to-end against a throwaway MinIO container:

```sh
scripts/smoke.sh
```

This brings up `docker-compose.yml` (MinIO with a `duetto` bucket and anonymous
download enabled, plus the Samba and SFTP backends), runs the
`Category=Integration` tests (`S3IntegrationTests` + `SmbIntegrationTests` +
`SftpIntegrationTests`), and tears the containers down. Requires Docker and a
free host port 9000.

To run just the S3 integration tests against your own endpoint:

```sh
DUETTO_S3_TEST=1 \
DUETTO_S3_TEST_ENDPOINT=http://127.0.0.1:9000 \
DUETTO_S3_TEST_ACCESS=duetto \
DUETTO_S3_TEST_SECRET=duettosecret \
DUETTO_S3_TEST_BUCKET=duetto \
  dotnet test tests/Duetto.Tests/Duetto.Tests.csproj \
    --filter "Category=Integration&FullyQualifiedName~S3"
```

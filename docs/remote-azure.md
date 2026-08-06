# Remote connections (Azure Blob Storage)

Duetto can browse and transfer files over Azure Blob Storage and any
Blob-API-compatible service (the [Azurite](https://github.com/Azure/Azurite)
emulator, Azure Blob Storage on IoT Edge) alongside the local filesystem, using
the official [Azure.Storage.Blobs](https://github.com/Azure/azure-sdk-for-net)
SDK — no OS mount required.

## Adding a connection

Open the drive popover by clicking the volume chip in the path bar, choose
**Connect…** (⌘K / Ctrl K), then set **Protocol** to **Azure Blob Storage**.
Fill in:

| Field | Notes |
|---|---|
| Name | Display name for the connection (shown in the popover) |
| Endpoint | **Leave blank for real Azure** (`https://{account}.blob.core.windows.net` is used). For an emulator or on-prem service, enter the full URL with the account in the path, e.g. `http://127.0.0.1:10000/devstoreaccount1` (Azurite). The URL carries its own port — there is no separate port field |
| Storage account | The account name. Required for **Account key**; also used to build the default endpoint. Hidden for **Connection string** (the string carries the account) |
| Credentials | **Account key** (account name + shared key), **Connection string** (a full connection string, incl. `UseDevelopmentStorage=true` for Azurite), **SAS** (a token or full SAS URL), or **Anonymous** (public read-only) |
| Container | **Leave blank to list all containers** at the connection root. Set a container to scope the root to it — required for **Anonymous** (anonymous credentials cannot list containers) |
| Initial path | Directory to open on connect. Leave `/` to land on the container list; use `/container` or `/container/prefix` to open a specific place |
| Save secret | When checked, the credential (account key / connection string / SAS) is stored obfuscated in `azure-connections.json` (see caveat below). Anonymous stores no secret |

Click **Connect**.

The **root of a connection is the list of containers** — `azure://<id>/` shows
each container as a folder; open one to browse its blobs
(`azure://<id>/<container>/<blob>`). When a **Container** is set, the root is
scoped to that single container.

Saved connections appear in the **CONNECTED SHARES** section of the drive
popover, tagged **Azure** (SFTP/SMB/S3 connections are tagged
**SFTP**/**SMB**/**S3** in the same list). Click a connection to connect and
navigate to it. A **Disconnect** row appears when a remote pane is active.

## Where configuration lives

| Platform | Directory |
|---|---|
| macOS | `~/Library/Application Support/Duetto/` |
| Linux | `$XDG_CONFIG_HOME/duetto/` or `~/.config/duetto/` |
| Windows | `%APPDATA%\Duetto\` |

Azure profiles are stored separately from SFTP, SMB, and S3:

- `azure-connections.json` — saved Azure profiles (name, endpoint, account name,
  auth mode, container, initial path, save-secret flag, obfuscated secret).

## Security caveat

Saved Azure secrets are **obfuscated** using a machine-derived key (AES-256-CBC,
SHA-256 of a machine identifier). This is reversible obfuscation, **not** secure
encrypted storage. Anyone with read access to `azure-connections.json` and the
same machine identity can recover the plaintext. For sensitive credentials, leave
**Save secret** unchecked and enter it at connect time, or prefer a scoped,
short-lived **SAS** over the account key.

## Object-store semantics

Blob storage is object storage, not a filesystem, so a few operations behave
differently from local / SFTP / SMB:

- **Folders are blob-name prefixes.** Creating a folder writes a zero-byte
  `prefix/.duettokeep` keep blob (hidden from listings) so the empty folder is
  visible. A bare `prefix/` marker is not portable — some services (Azurite)
  strip the trailing slash — so a real child blob is the reliable marker.
- **Delete is permanent** — there is no trash on Blob storage.
- **Rename / move a file** is a server-side copy + delete. When both panes are on
  the same Azure connection, copy and move are offloaded to the server (Copy
  Blob) with no bytes crossing the client. Renaming or moving a *folder* in place
  is not supported (bulk copy); move a folder's *contents* between panes instead,
  which the transfer engine walks file-by-file.
- **No `.part` staging** — an upload only becomes visible once it completes, so a
  failed transfer never exposes a partial blob.
- **No modification-time preservation** — the service owns each blob's
  `LastModified`.
- **Server-side copy needs a shared key.** Copy Blob reads the source by URL, so
  it uses a short-lived read SAS minted from the account key. **SAS** and
  **Anonymous** connections cannot mint one, so their moves stream through the
  client instead (always correct, just not offloaded).

Anonymous connections are read-only in practice (writes return access-denied) and
require a **Container**, because anonymous credentials cannot list containers.

## Running the Azure integration tests

The unit tests use an in-memory fake, so `dotnet test` needs no server. To
exercise the real client end-to-end against a throwaway Azurite container:

```sh
scripts/smoke.sh
```

This brings up `docker-compose.yml` (Azurite on port 10000, plus the MinIO,
Samba, and SFTP backends), runs the `Category=Integration` tests
(`AzureIntegrationTests` + `S3IntegrationTests` + `SmbIntegrationTests` +
`SftpIntegrationTests`), and tears the containers down. Requires Docker and a
free host port 10000. The Azure tests create their `duetto` container on first
run.

To run just the Azure integration tests against your own endpoint:

```sh
DUETTO_AZURE_TEST=1 \
DUETTO_AZURE_TEST_ENDPOINT=http://127.0.0.1:10000/devstoreaccount1 \
DUETTO_AZURE_TEST_ACCOUNT=devstoreaccount1 \
DUETTO_AZURE_TEST_KEY=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw== \
DUETTO_AZURE_TEST_CONTAINER=duetto \
  dotnet test tests/Duetto.Tests/Duetto.Tests.csproj \
    --filter "Category=Integration&FullyQualifiedName~Azure"
```

> The Azurite emulator may validate a slightly older REST API version than the
> SDK negotiates; the bundled compose service runs it with
> `--skipApiVersionCheck`. Real Azure needs no such flag.

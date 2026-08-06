# Azure Blob Storage remote backend

Add Azure Blob Storage as a remote backend in Duetto, mirroring the existing **S3**
backend end-to-end (Core provider + client-adapter seam + connection manager/store +
Connect dialog UI + saved-share popover + docs + tests). Container ≈ bucket, blob ≈
object key. Integration-tested against the **Azurite** emulator.

## Locked decisions
- **SDK:** `Azure.Storage.Blobs` (latest 12.x) added to `Duetto.Core`.
- **Scheme:** `azure`. Provider-local paths are `"/container/blob"` (mirrors S3
  `"/bucket/key"`). Persistence file: `azure-connections.json`.
- **Auth modes** (`AzureAuthMode`): `SharedKey` (account name + key + endpoint),
  `ConnectionString`, `Sas` (token or SAS URL), `Anonymous`. Secret carried in
  `ConnectSecret.Password` (key / SAS / connection string); Anonymous has none.
- **Container scoping:** optional single configured container (empty = list all
  containers at root), mirroring S3 `ConfiguredBucket`.
- **Server-side copy:** implement `IServerSideCopy` via Azure Copy Blob; domain =
  account (endpoint+account) via `IBackendIdentity.BackendKey`.
- **Custom endpoint:** required field (Azurite/on-prem/sovereign clouds). Default for
  real Azure is `https://{account}.blob.core.windows.net`; account-in-path style is
  used when a custom endpoint is given.
- **Test target:** Azurite `mcr.microsoft.com/azure-storage/azurite`, blob port
  **10000**, account `devstoreaccount1`, well-known key
  `Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==`,
  endpoint `http://127.0.0.1:10000/devstoreaccount1`. Integration tests create their
  own container. (The user-linked `mcr.microsoft.com/azure-blob-storage` IoT-Edge
  image — port 11002, `LOCAL_STORAGE_ACCOUNT_NAME/KEY` — is a documented alternative.)
- **Template to copy:** every `S3*` file in `src/Duetto.Core/Remote` and its tests.
  The Explore recipe (Layers 1–9) enumerates the exact shared integration points.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**. Branch:
`feature/azure-blob-backend`. Do NOT commit secrets; the Azurite key above is
Microsoft's public well-known emulator key (safe to hardcode in tests/compose).

## Phase 1: Core config + persistence (no network)
Status: Complete

- [x] Add `Azure.Storage.Blobs` (12.29.1) PackageReference to `src/Duetto.Core/Duetto.Core.csproj`.
- [x] `AzureAuthMode.cs` — enum `SharedKey | ConnectionString | Sas | Anonymous`.
- [x] `AzureConnectionInfo.cs` — sealed record: Id, Name, Endpoint, AccountName,
      AuthMode, Container, InitialPath (mirror `S3ConnectionInfo`).
- [x] `AzureConnectionException.cs` — recoverable transport exception +
      `AzureAuthenticationException` (mirror `S3ConnectionException.cs`).
- [x] `AzureEntry.cs` — sealed record: Name, FullName (`/container/blob`), IsDirectory,
      IsReadOnly, Length (−1 dirs), LastWriteTimeUtc (mirror `S3Entry`).
- [x] `AppPaths.cs` — add `AzureConnectionsJsonPath => Path.Combine(ConfigDir, "azure-connections.json")`.
- [x] `AzureConnectionStore.cs` — `StoredAzureConnection` DTO +
      Load/Save/ResolveInfo/ResolveSecret/Pack; secret (account key / SAS / connection
      string) obfuscated via existing `SecretCodec` (mirror `S3ConnectionStore`).
- [x] Reuse `ConnectSecret` (Password = key/SAS/connection string). No new secret type.
- [x] Test: `AzureConnectionStoreTests.cs` — JSON round-trip + secret
      obfuscation/decrypt for each credentialed mode + Anonymous; corruption-resilient load.

### Verification Plan
- `dotnet build src/Duetto.Core/Duetto.Core.csproj -c Release` → Build succeeded, 0 errors.
- `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~AzureConnectionStore"` → all pass.

### Phase Summary
Done. Build clean; `AzureConnectionStoreTests` = **7 passed** (theory covers SharedKey/
ConnectionString/Sas obfuscation, no-save, Anonymous). Key decisions: single-field
`SecretPayload {"s":...}` (Azure has one secret value, unlike S3's key+token); secret
persisted for all modes except Anonymous; `ConnectSecret.Password` carries the account
key / SAS / connection string (no new secret type). `AzureConnectionInfo.Endpoint`
blank ⇒ adapter will build `https://{AccountName}.blob.core.windows.net`; custom
endpoint (Azurite `http://127.0.0.1:10000/devstoreaccount1`) uses account-in-path.
Package pinned `Azure.Storage.Blobs` 12.29.1.

## Phase 2: Client-adapter seam, provider, connection manager (unit-testable)
Status: Complete

- [ ] `IAzureClientAdapter.cs` + `IAzureClientFactory` + `DefaultAzureClientFactory`
      (mirror `IS3ClientAdapter.cs`): Connect/Disconnect/IsConnected, ListContainers,
      ListBlobs(container, prefix), StatBlob, PrefixExists, PutEmptyBlob, OpenRead,
      OpenWrite, DeleteBlob, DeletePrefix, CopyBlob(src,dst,onBytes,token), EnumerateRecursive.
- [ ] `RealAzureClientAdapter.cs` — `Azure.Storage.Blobs`; build `BlobServiceClient`
      switching on `AzureAuthMode` (SharedKey→`StorageSharedKeyCredential`;
      ConnectionString→ctor; Sas→`AzureSasCredential`/SAS URL; Anonymous→no creds).
      Translate `RequestFailedException` → AzureAuthenticationException /
      FileNotFoundException / IOException / AzureConnectionException. Eager Connect
      validation (list containers or probe configured container).
- [ ] `AzureFileStream.cs` — write-spool to temp, upload on close (mirror `S3FileStream`).
- [ ] `AzureConnection.cs` — Connect/Disconnect/IsConnected/ConfiguredContainer/ConnId/
      WithReconnect (mirror `S3Connection`).
- [ ] `AzureFileSystemProvider.cs` — implements `IFileSystemProvider` (all 15 methods),
      `IBackendIdentity`, `IServerSideCopy`, `IDisposable`; `AzureCapabilities`; serialize
      calls with a lock (mirror `S3FileSystemProvider`).
- [ ] `AzureConnectionManager.cs` — pool; register/unregister providers with
      `FileSystemRegistry` under scheme `"azure"`; optional `IAzureClientFactory` ctor arg.
- [ ] Tests: `FakeAzureClientAdapter.cs`, `FakeAzureClientFactory.cs`,
      `AzureFileSystemProviderContractTests.cs`, `AzureConnectionManagerTests.cs`,
      `AzureFileStreamTests.cs`, `AzureBackendIdentityTests.cs`,
      `AzureServerSideCopyProviderTests.cs`, `AzureEndpointTests.cs`.

### Verification Plan
- `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~Azure&Category!=Integration"` → all pass, and covers all 15 provider methods + server-side copy via fakes.

### Phase Summary
Done. **39 Azure unit tests pass** (store 7, provider contract 13, server-side copy 3,
manager 4, backend identity 4, file stream 2, endpoint 6). Files created:
`IAzureClientAdapter` (+`IAzureClientFactory`/`DefaultAzureClientFactory`),
`RealAzureClientAdapter`, `AzureFileStream`, `AzureConnection`,
`AzureFileSystemProvider`, `AzureConnectionManager`; test doubles
`FakeAzureClientAdapter`/`FakeAzureClientFactory`.

**Key implementation notes for future agents:**
- SDK gotcha: the **sync** `BlobContainerClient.GetBlobs(...)`/`GetBlobsByHierarchy(...)`
  in 12.29.1 have NO default args — pass `BlobTraits.None, BlobStates.None, prefix[, CancellationToken.None]`
  positionally (named `prefix:` fails to compile).
- Server-side copy (`CopyBlob`) uses `StartCopyFromUri` with a short-lived read SAS minted
  via `GenerateSasUri`; it returns **false** when `!CanGenerateSasUri` (SAS/Anonymous
  auth), and the provider then streams (Move/ReplaceFile fall back to OpenRead→OpenWrite).
- Error translation: `RequestFailedException` Status 0 → `AzureConnectionException`
  (recoverable, DNS/connect); 401/403 → `AzureAuthenticationException`; 404 → `FileNotFoundException`;
  else `IOException`. Malformed connection string throws `FormatException` at build → caught → `AzureConnectionException`.
- `NormalizeEndpoint` (internal, tested via existing InternalsVisibleTo) adds `https://`
  only when no scheme, mirroring S3.

## Phase 3: UI wiring (Connect dialog + saved-share popover + panes)
Status: Complete

- [ ] `ConnectDialogViewModel.cs` — add `AzureBlob` to `ConnectProtocol`; `IsAzure`,
      `AzureFieldsVisible`, per-auth visibility (`AzureSharedKeyVisible`,
      `AzureConnStringVisible`, `AzureSasVisible`), `AzureAuth` property; `ValidateAzure`,
      `BuildAzureInfo`, `BuildAzureSecret`, `ForEdit(StoredAzureConnection)`,
      `ConnectAzureAsync`, `OnAzureConnectSuccess`.
- [ ] `ConnectWindow.axaml` — add "Azure Blob" ComboBoxItem; Azure fields section
      (endpoint, account, container, initial path) + auth-mode radios (Account key /
      Connection string / SAS / Anonymous) + per-mode secret fields.
- [ ] `ConnectWindow.axaml.cs` — protocol index ↔ enum in ctor + `OnProtocolChanged`;
      Azure auth-mode radio click handlers.
- [ ] `DrivePopoverViewModel.cs` — `ShareRowViewModel(StoredAzureConnection)` overload
      (scheme `azure`, endpoint subtitle); `IsAzure`; `SchemeLabel` `azure`→"Azure";
      `RebuildShareRows` iterates `ListAzureConnections`; `EditShare`/`RemoveShare`
      branches; seams `ListAzureConnections`/`IsAzureConnected`.
- [ ] `MainViewModel.cs` — `AzureConnectionManager`/`AzureConnectionStore` props + ctor
      defaults from `AppPaths.AzureConnectionsJsonPath`; `OpenAzureConnectDialog` seam;
      `ConnectToAzureShare()`; wire seams in `WirePopoverSeams`.
- [ ] `PaneView.axaml.cs` — `Edit/RemoveAzureShareRequested`; `ActivateShare` azure
      branch; `DisconnectCurrentPane` `"azure"` branch; `OpenAzureConnectDialog(+Core)`;
      `RemoveAzureConnection`.
- [ ] Tests: `AzureConnectDialogTests.cs` (field visibility, validation, secret build
      per auth mode), `AzureConnectToShareTests.cs` (popover row activate/navigate/remove).

### Verification Plan
- `dotnet build -c Release` (whole solution) → 0 errors.
- `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~AzureConnect"` → pass.
- `dotnet run --project src/Duetto -- --smoke` → exits 0 (headless app boots with new VM wiring).

### Phase Summary
Done. Full non-integration suite **713 passed / 0 failed**; app `--smoke` exits 0; 52
Azure tests total (39 core + 13 UI: `AzureConnectDialogTests` 7, `AzureConnectToShareTests`
+ `AzureSharesPopoverMergeTests` 7). Shared files touched: `ConnectDialogViewModel`
(enum `AzureBlob`, 4-mode auth, visibility props, ForEdit/Validate/Build/Connect),
`ConnectWindow.axaml`(+`.cs`) (combo item index 3, Azure fields + 4 auth radios),
`DrivePopoverViewModel` (azure ShareRow + seams + edit/remove branches),
`MainViewModel` (managers/stores + `ConnectToAzureShare` + `OpenAzureConnectDialog`
seam + popover wiring + Dispose), `PaneView.axaml.cs` (activate/disconnect/open/remove),
`MainWindow.axaml.cs`. The `ConnectDialogViewModel` ctor gained two params — all 6 call
sites (2 prod + 4 tests) updated.

**Notes for future agents:**
- `ConnectSecret.Password` carries the single Azure secret; the mode decides which
  UI field feeds it (`AzureAccountKey`/`AzureConnectionString`/`AzureSasToken`).
- `HostPortVisible` changed from `!IsS3` to `IsSftp || IsSmb` (also hides host/port for Azure).
- Scheme string is `"azure"` everywhere (registry, path prefix, disconnect/remove branches).

## Phase 4: Azurite harness + integration tests
Status: Not started

- [ ] `docker-compose.yml` — add `azurite` service (`mcr.microsoft.com/azure-storage/azurite`,
      `command: azurite-blob --blobHost 0.0.0.0`, ports `10000:10000`).
- [ ] `AzureIntegrationTests.cs` — `Category=Integration`, gated on `DUETTO_AZURE_TEST=1`;
      reads `DUETTO_AZURE_TEST_ENDPOINT/ACCOUNT/KEY/CONTAINER`; creates the container if
      absent; exercises connect → list → upload → download → copy/move → delete against
      real Azurite (mirror `S3IntegrationTests`).
- [ ] `scripts/smoke.sh` — wait for `127.0.0.1:10000`; add `DUETTO_AZURE_TEST_*` env
      (endpoint `http://127.0.0.1:10000/devstoreaccount1`, account `devstoreaccount1`,
      well-known key, container `duetto`) to the `dotnet test` invocation.

### Verification Plan
- `docker compose up -d azurite` then
  `DUETTO_AZURE_TEST=1 DUETTO_AZURE_TEST_ENDPOINT=http://127.0.0.1:10000/devstoreaccount1 DUETTO_AZURE_TEST_ACCOUNT=devstoreaccount1 DUETTO_AZURE_TEST_KEY=<well-known> DUETTO_AZURE_TEST_CONTAINER=duetto dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~AzureIntegration"` → pass.
- `dotnet test … --filter "FullyQualifiedName~AzureIntegration"` with no env → tests skip (not fail).
- `scripts/smoke.sh` → "Smoke test passed" (all backends incl. Azure).

### Phase Summary
_(write when phase completes)_

## Phase 5: Docs + README + CHANGELOG
Status: Not started

- [ ] `docs/remote-azure.md` — mirror `docs/remote-s3.md` (intro, adding a connection,
      config location + `azure-connections.json`, security caveat, blob semantics:
      containers/prefixes, permanent delete, no mtime, copy limits, integration tests).
- [ ] `README.md` — add Azure Blob to the remote features list + link the doc.
- [ ] `CHANGELOG.md` — add an entry under the next unreleased version.

### Verification Plan
- `grep -q "azure-connections.json" docs/remote-azure.md && grep -qi "Azure Blob" README.md` → both match.
- `test -f docs/remote-azure.md` and README links resolve (`grep -o "docs/remote-azure.md" README.md`).

### Phase Summary
_(write when phase completes)_

## Final Recap
_(write when all phases complete)_

## Deployment Plan
_(write when all phases complete)_

# Add S3 Server Connection Support

Add S3 (and S3-compatible: MinIO, R2, Wasabi, B2) as a third remote backend in
Duetto, alongside SFTP and SMB, following the existing per-protocol pattern
(`{Proto}ConnectionInfo` + `{Proto}ConnectionManager` + `{Proto}ConnectionStore`,
shared `SecretCodec`/`ConnectSecret`, one protocol-aware connect dialog, provider
registered in `FileSystemRegistry` under a scheme). Scheme: `s3://{connId}/{bucket}/{key…}`.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

Follow the codebase conventions in `AGENTS.md` (primary constructors, file-scoped
namespaces, no `_` field prefixes, `CancellationToken` on async, records for DTOs,
comments explain *why* only). Mirror the SMB implementation closely — it is the
nearest sibling and every layer already has an SMB analogue to copy.

Build (no solution file exists — build per project):
- Core: `dotnet build src/Duetto.Core/Duetto.Core.csproj`
- App: `dotnet build src/Duetto/Duetto.csproj`
- Tests: `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj`

Integration tests self-skip unless their env var is set, so the full test run is
always safe to execute.

---

## Decisions (locked with the user)

- **SDK:** `AWSSDK.S3` (latest stable 4.x). Works against AWS and any S3-compatible
  endpoint via `ServiceURL` + `ForcePathStyle`.
- **Root namespace:** list **all buckets** at the connection root (like SMB lists
  shares); each bucket is a top-level folder. `Bucket` field is **optional**:
  - blank (Keys/Profile auth) → root lists all buckets (needs `ListBuckets` perm),
  - set → root is that single bucket (scoped creds, or required for Anonymous).
- **Auth modes** (`S3AuthMode` selector, like SFTP's password/key radios):
  - **Keys** — access-key-id + secret-access-key + **optional session token** (STS/temporary).
  - **Profile** — named profile from `~/.aws/credentials` (`CredentialProfileStoreChain`).
  - **Anonymous** — no creds, read-only in practice; **requires `Bucket`** (anonymous
    principals cannot call `ListBuckets`).
- **In-scope capabilities:** server-side copy/move (`CopyObject`), empty-folder
  markers (zero-byte `prefix/` object), recursive search, save-secret-to-disk.

## Assumptions (baked into the design)

- **Connection fields:** Name, Endpoint (blank = real AWS), Region, PathStyle (bool),
  AuthMode, AccessKeyId, SessionToken, Profile, Bucket (optional), InitialPath (`/`).
  **No port field** — the endpoint URL carries its own port.
- **Capabilities:** `CanRename=false` (move = server `CopyObject` + delete),
  `HasTrash=false` (delete permanent), `AtomicRename=false` + no `.part` staging (a
  PUT/multipart object only becomes visible when complete, so no partial is exposed),
  `CanCreateEmptyDir=true` (marker object), `SupportsSearch=true`,
  `PreservesMTime=false`, `ReportsCapacity=false`, `HasPermissions=false`,
  `CaseSensitive=true`, `Separator='/'`.
- **Write path:** `OpenWrite` spools to a temp file; on close, upload via
  `TransferUtility` (auto multipart for large objects). `OpenRead` streams `GetObject`.
- **Server-side copy domain (`BackendKey`):** keyed on endpoint + credentials identity
  (the connection), **not** bucket — S3 `CopyObject` works cross-bucket within one
  account/endpoint. So a server-side copy is valid between any two paths of the same
  S3 connection. Returns `null` for the bucket-list root.
- **Secret storage:** extend `ConnectSecret` with an optional `SessionToken` (additive;
  SFTP/SMB unaffected). `StoredS3Connection.obfuscatedSecret` encodes secret-key +
  optional session token (JSON, then `SecretCodec`). Only saved in Keys mode when
  "save secret" is checked. Profile/Anonymous store nothing.
- **Scheme string:** `"s3"` everywhere (`FileSystemRegistry`, path building, popover row).

---

## Phase 1: Connection model, persistence & dependency
Status: Complete

- [x] Add `AWSSDK.S3` to `src/Duetto.Core/Duetto.Core.csproj` — pinned **4.0.101.6**
  (pulled `AWSSDK.Core` 4.0.100.9) via `dotnet add`.
- [x] `src/Duetto.Core/Remote/S3AuthMode.cs` — `enum S3AuthMode { Keys, Profile, Anonymous }`.
- [x] `src/Duetto.Core/Remote/S3ConnectionInfo.cs` — record: `Id, Name, Endpoint="",
  Region="", PathStyle=false, AuthMode=S3AuthMode.Keys, AccessKeyId="", Profile="",
  Bucket="", InitialPath="/"`. Secret + session token are **not** stored here (supplied
  via `ConnectSecret` at connect time), mirroring `SmbConnectionInfo`.
- [x] Extend `src/Duetto.Core/Remote/ConnectSecret.cs` with optional `SessionToken`
  (nullable, default null) + a `FromKeys(secret, sessionToken)` factory. Existing
  `FromPassword`/`FromKey` untouched.
- [x] `src/Duetto.Core/Remote/S3ConnectionStore.cs` — `StoredS3Connection` DTO (json:
  `id,name,endpoint,region,pathStyle,authMode,accessKeyId,profile,bucket,initialPath,
  savePassword,obfuscatedSecret`) + `Load()/Save()` (atomic temp-then-move) +
  `ResolveInfo`, `ResolveSecret`, `Resolve`, `Pack`. Secret-key + session token packed as
  JSON (`{s,t}`) then obfuscated into `obfuscatedSecret`.
- [x] `src/Duetto.Core/Remote/S3ConnectionException.cs` — both `S3ConnectionException`
  (recoverable) + `S3AuthenticationException` (never retried) in one file.
- [x] Add `S3ConnectionsJsonPath` (`s3-connections.json`) to `AppPaths.cs`.

### Verification Plan
- `dotnet build src/Duetto.Core/Duetto.Core.csproj` → succeeds (AWSSDK.S3 restores).
- `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~S3ConnectionStore"` → green (round-trip Pack/Load, secret + session token survive, Anonymous/Profile store no secret).

### Phase Summary
Done. Added `AWSSDK.S3` **4.0.101.6** (+ `AWSSDK.Core` 4.0.100.9). Created the S3
connection model + persistence layer mirroring the SMB trio:
`S3AuthMode`, `S3ConnectionInfo` (record), `S3ConnectionStore` + `StoredS3Connection`
DTO, `S3ConnectionException`/`S3AuthenticationException`; extended shared `ConnectSecret`
with a nullable `SessionToken` + `FromKeys(secret, token)`; added
`AppPaths.S3ConnectionsJsonPath`.

Key decisions made here (carry into later phases):
- **Secret encoding:** secret access key + optional session token are serialized to JSON
  `{"s":…,"t":…}` and obfuscated into the single `ObfuscatedSecret` field via the shared
  `SecretCodec`. `ResolveSecret` decrypts + JSON-parses back to `ConnectSecret.FromKeys`.
- **Non-Keys auth:** `ResolveSecret` returns a **non-null empty** `ConnectSecret` for
  Profile/Anonymous (no prompt needed); returns `null` only for Keys when the secret was
  not saved / failed to decrypt. `Pack` persists a secret **only** for `S3AuthMode.Keys`.
- **Verified:** `dotnet build` Core clean (0 warn/err); the new
  `tests/Duetto.Tests/Core/Remote/S3ConnectionStoreTests.cs` — **6/6 passing** (round-trip
  incl. session token, no-save, Profile, Anonymous). Pre-existing warnings in
  `MainViewModel`/`DrivePopoverViewModel`/`CliInstall`/`ShellRunnerTests` are unrelated.
- **Note:** the store test (a Phase 5 item) was written now to satisfy Phase 1
  verification — check it off in Phase 5 rather than re-writing.

Next (Phase 2): `IS3ClientAdapter` + `RealS3ClientAdapter` over `AmazonS3Client`
(build config from Endpoint/Region/PathStyle; credentials per `S3AuthMode`:
`BasicAWSCredentials`/`SessionAWSCredentials`/`CredentialProfileStoreChain`/
`AnonymousAWSCredentials`), `S3Entry`, `S3FileStream` (temp-spool + `TransferUtility`),
and `FakeS3ClientAdapter`.

## Phase 2: S3 client adapter & streams
Status: Complete

- [x] `src/Duetto.Core/Remote/S3Entry.cs` — record `(Name, FullName, IsDirectory,
  IsReadOnly, Length, LastWriteTimeUtc)`, mirroring `SmbEntry` (Length -1 for dirs).
- [x] `src/Duetto.Core/Remote/IS3ClientAdapter.cs` — SDK abstraction: `Connect()`
  (validate creds/endpoint), `ListBuckets()`, `ListObjects(bucket,prefix)` (delimiter
  `/` → folders + files), `StatObject`, `PrefixExists`, `PutEmptyObject` (empty file +
  `prefix/` marker), `OpenRead`, `OpenWrite`, `DeleteObject`, `DeletePrefix` (recursive),
  `CopyObject(…,onBytes,token)`, `EnumerateRecursive`. Also `IS3ClientFactory` +
  `DefaultS3ClientFactory` (for Phase 3 manager injection).
- [x] `src/Duetto.Core/Remote/RealS3ClientAdapter.cs` — wraps `AmazonS3Client`.
  `AmazonS3Config` from Endpoint (`ServiceURL` non-blank) + `ForcePathStyle` +
  `RegionEndpoint` (blank endpoint). Credentials per `S3AuthMode`: `BasicAWSCredentials`
  / `SessionAWSCredentials` (token) / `CredentialProfileStoreChain` (Profile) /
  `AnonymousAWSCredentials`. Paginated list; `TransferUtility` upload; `Translate`
  maps `AmazonS3Exception`→`S3AuthenticationException`(403/AccessDenied/InvalidAccessKeyId
  /SignatureDoesNotMatch) / `FileNotFoundException`(404) / `IOException`; other SDK/HTTP
  faults → `S3ConnectionException`.
- [x] `src/Duetto.Core/Remote/S3FileStream.cs` — write-only spool: `ForWrite(upload)`
  writes to a temp file, on `Dispose` rewinds it and hands the stream to `upload` (which
  runs the `TransferUtility` PUT), then deletes the temp file.
- [x] `tests/Duetto.Tests/Core/Remote/FakeS3ClientAdapter.cs` — in-memory bucket/key map
  implementing `IS3ClientAdapter`, replicating S3 delimiter listing.

### Verification Plan
- `dotnet build src/Duetto.Core/Duetto.Core.csproj` → succeeds (AWSSDK v4 API).
- `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj --filter "FullyQualifiedName~S3FileStream"` → green (spool→upload round-trip + fake write/read + one-level listing).

### Phase Summary
Done. Built the S3 client-adapter layer over **AWSSDK v4 (async-only → awaited + blocked,
the provider is synchronous)**: `S3Entry`, `IS3ClientAdapter` (+ `IS3ClientFactory`/
`DefaultS3ClientFactory`), `RealS3ClientAdapter`, `S3FileStream`, and the test
`FakeS3ClientAdapter`.

Key decisions / deviations (carry into later phases):
- **Read has no custom stream.** `S3FileStream` is write-only (the part with real
  logic — temp-spool + upload-on-close). `OpenRead` returns the SDK `GetObject`
  `ResponseStream` directly (a `MemoryStream` in the fake). The planned `ForRead` was
  unnecessary.
- **`Connect()` validates eagerly:** `ListBuckets` when no bucket is configured, else a
  1-key probe of the bucket (creds scoped to one bucket, and Anonymous, can't
  `ListBuckets`). This surfaces auth/endpoint errors at connect time (Phase 4 dialog).
- **`CopyObject` returns `false` when the source exceeds 5 GiB** (single-part copy
  limit; multipart copy not implemented) so the provider streams instead. Reports the
  full object size once via `onBytesCopied`.
- **Adapter method names** (final): `ListBuckets, ListObjects, StatObject, PrefixExists,
  PutEmptyObject, OpenRead, OpenWrite, DeleteObject, DeletePrefix, CopyObject,
  EnumerateRecursive` — Phase 3 provider maps `IFileSystemProvider` onto these.
- **Entry `FullName`** is provider-local `"/bucket/key"`, built by the adapter (it knows
  the bucket). Folders (S3 common prefixes) carry `Length = -1`, `mtime = default`.
- **AWSSDK v4 nullable surface handled:** `S3Object.Size`/`LastModified` are nullable,
  `ListObjectsV2Response.IsTruncated` is `bool?`, `MaxKeys` is `int?`.
- **Verified:** Core build clean (0 warn/err); S3 tests **10/10** (6 store + 4 stream/fake).
- **Note:** `FakeS3ClientAdapter` (a Phase 5 item) was written now for verification —
  check it off in Phase 5, don't re-create.

Next (Phase 3): `S3FileSystemProvider` (`IFileSystemProvider`+`IBackendIdentity`+
`IServerSideCopy`) over the adapter — path split `/bucket/key`, root = bucket list (or
single bucket), mkdir = marker, move/rename = copy+delete, `BackendKey = s3://{connId}`,
`TryServerSideCopy` → `CopyObject` — plus `S3Connection` + `S3ConnectionManager`
(register scheme `"s3"`), and `S3Capabilities`.

## Phase 3: Provider, capabilities, manager & registry
Status: Complete

- [x] `src/Duetto.Core/Remote/S3FileSystemProvider.cs` implementing
  `IFileSystemProvider, IBackendIdentity, IServerSideCopy, IDisposable`. Root `/` → bucket
  list (or the single configured bucket); `/bucket/key…` → object ops via `Split` +
  `PrefixFor`. `CreateDirectory` = zero-byte `prefix/` marker; `Delete(dir)` = recursive
  `DeletePrefix`; `EnumerateRecursive` = **level-by-level `ListObjects`** so folder entries
  are yielded (engine needs them to recreate dirs at the dest).
- [x] Static `S3Capabilities` (CanRename=false, AtomicRename=false, HasTrash=false,
  PreservesMTime=false, CanCreateEmptyDir=true, SupportsSearch=true, `/` separator).
- [x] `IBackendIdentity.BackendKey(path)` → `s3://{connId}` for any object path, `null`
  for the bucket-list root.
- [x] `IServerSideCopy.TryServerSideCopy` → adapter `CopyObject`; returns `false` at a
  bucket-level path or when the object exceeds the single-part copy limit.
- [x] `src/Duetto.Core/Remote/S3Connection.cs` — holds the adapter; `WithReconnect<T>`
  rebuilds the client on `S3ConnectionException` (not on auth failure). Exposes `ConnId`
  + `ConfiguredBucket`. Added `IsConnected`/`Disconnect` to `IS3ClientAdapter` (+ impls).
- [x] `src/Duetto.Core/Remote/S3ConnectionManager.cs` — mirror `SmbConnectionManager`,
  scheme `"s3"`; evict-outside-lock; `Register`/`Unregister("s3", id)`.

### Verification Plan
- `dotnet build src/Duetto.Core/Duetto.Core.csproj` → succeeds.
- `dotnet test … --filter "FullyQualifiedName~S3FileSystemProvider|~S3BackendIdentity|~S3ServerSideCopy|~S3ConnectionManager"` → green.

### Phase Summary
Done. Built the provider layer: `S3FileSystemProvider`
(`IFileSystemProvider`+`IBackendIdentity`+`IServerSideCopy`), `S3Connection`,
`S3ConnectionManager` (scheme `"s3"`), and added `IsConnected`/`Disconnect` to the adapter.

Key decisions / deviations (carry forward):
- **Moves go server-side without `CanRename`.** Read of `TransferEngine`: the native-move
  shortcut is gated on `CanRename` (skipped for S3), but `CopyFile`→`TryServerSideCopyInto`
  runs regardless and uses `IServerSideCopy` + `BackendKey` equality. So an S3→S3 move on
  one connection offloads to `CopyObject` (0 client bytes) then deletes the source. Proven
  by an engine-level test: `CopyCount ≥ 1`, `ReadCount == 0`.
- **`EnumerateRecursive` yields folders too** (recursive `ListObjects`, not the flat
  adapter enumerate) — required so tree copies to a real filesystem recreate directories.
- **File-only `Rename`/`Move`/`ReplaceFile`.** In-place rename/move of an S3 *folder*
  throws `NotSupportedException` (bulk key-copy is out of scope). Moving a folder *between
  panes* still works — the engine walks it file-by-file. Documented as a known limitation.
- **`SetLastWriteTimeUtc` throws** `NotSupportedException` (`PreservesMTime=false`, so the
  engine never calls it).
- **`BackendKey = s3://{connId}`** (connection-scoped). Cross-bucket server-side copy
  within one connection is allowed; a different connection falls back to streaming (always
  correct). Cross-connection same-endpoint offload was intentionally not attempted.
- **Bucket-list root** has no `BackendKey`; single-bucket mode lists only the configured
  bucket at root and skips `ListBuckets` (works for scoped/anonymous creds).
- **Verified:** Core build clean; S3 tests **34/34**; **full suite 621/621** (no
  regressions). New tests: `S3FileSystemProviderContractTests` (14),
  `S3BackendIdentityTests` (4), `S3ServerSideCopyProviderTests` (3, incl. engine move),
  `S3ConnectionManagerTests` (4); plus `FakeS3ClientFactory` + copy/read counters on the
  fake. (These are Phase 5 items pulled forward — check them off there, don't re-create.)

Next (Phase 4): UI wiring — `ConnectProtocol.S3`, dialog fields/validation/connect for the
three auth modes, `ConnectWindow` XAML + code-behind, `DrivePopoverViewModel` third scheme,
`MainViewModel.ConnectToS3Share` + seams + `AppPaths` init.

## Phase 4: UI / ViewModel wiring
Status: Complete

- [x] `src/Duetto/ViewModels/ConnectDialogViewModel.cs`: add `S3` to `ConnectProtocol`;
  add observable fields `Endpoint, Region, PathStyle, S3AuthMode, AccessKeyId,
  SessionToken, Profile, Bucket`; add `IsS3` + visibility props (`S3FieldsVisible`,
  `S3KeysVisible`, `S3ProfileVisible`, `S3AnonymousVisible`); extend `Validate()`
  (Keys→access-key-id+secret; Profile→profile name; Anonymous→bucket required);
  add `BuildS3Info()`/`BuildS3Secret()`, `ConnectS3Async()` (+ S3 exception handling),
  `OnS3ConnectSuccess()`, `ForEdit(StoredS3Connection)`; route in `ConnectAsync()`
  (`if (IsS3) { await ConnectS3Async(); return; }`). Inject `S3ConnectionManager` +
  `S3ConnectionStore` into the constructor and wire `S3ConnectAction`/`S3SaveAction`.
- [x] `src/Duetto/Views/ConnectWindow.axaml` + `.axaml.cs`: "S3 / S3-compatible"
  `ComboBoxItem`; 3-way ComboBox index ↔ `ConnectProtocol`; S3 field rows with `IsVisible`
  bindings (Host/Port gated on `HostPortVisible`); `vm.S3Connected += _ => Close();`;
  S3 auth-radio click handlers.
- [x] `src/Duetto/ViewModels/DrivePopoverViewModel.cs`: third `ShareRowViewModel` ctor
  (`Scheme="s3"`, `IsS3`, `SchemeLabel` switch); routed `EditShare`/`RemoveShare`; S3 loop
  in `RebuildShareRows()`; `ListS3Connections`/`IsS3Connected` seams +
  `EditS3ShareRequested`/`RemoveS3ShareRequested`.
- [x] `src/Duetto/ViewModels/MainViewModel.cs`: `S3ConnectionManager`/`S3ConnectionStore`
  props + ctor params + production init (`AppPaths.S3ConnectionsJsonPath`);
  `ConnectToS3Share`; `OpenS3ConnectDialog` seam; S3 popover seams; `Dispose`.
- [x] `src/Duetto/Views/PaneView.axaml.cs` + `MainWindow.axaml.cs`: DataContext handlers,
  `BuildConnectDialog`/`OpenRemoteConnectDialog` S3 deps + `S3Connected`, `ActivateShare`
  S3 branch, `DisconnectCurrentPane` `"s3"`, `OpenS3ConnectDialog(Core)` + `RemoveS3Connection`.

### Verification Plan
- `dotnet build src/Duetto/Duetto.csproj` → succeeds.
- `dotnet test … --filter "FullyQualifiedName~S3ConnectDialog|~S3ConnectToShare|~S3SharesPopover"` → green.

### Phase Summary
Done. Full UI wiring for the third protocol, mirroring SMB across the dialog VM, XAML +
code-behind, drive popover, `MainViewModel`, and both dialog-hosting code-behinds
(`PaneView`, `MainWindow`).

Decisions:
- **No port field for S3** — the endpoint URL carries the port, so the Host/Port row is
  hidden (`HostPortVisible`). Access-key-id and secret get **their own** S3 fields rather
  than reusing the shared Username/Password, keeping the S3 form independent of the
  SFTP↔SMB port-swap logic.
- Auth is an `S3Auth` radio group (Keys / Profile / Anonymous); `ValidateS3` enforces
  **Anonymous → Bucket required**.
- The `ConnectDialogViewModel` ctor grew to 8 args; the 3 existing dialog-ctor test
  call-sites were updated.

Verified: app `dotnet build` clean; **full suite 635/635**. New UI tests:
`S3ConnectDialogTests` (6), `S3ConnectToShareTests` (5) + `S3SharesPopoverMergeTests` (2),
one `ConnectWindow` real-window S3-load test; added `FakeS3ClientAdapter.NextConnectThrow`.
(Phase 5 items pulled forward — check them off there.)

Next (Phase 5): the unit + UI suites are already written; only the gated
`S3IntegrationTests` (real MinIO) remains. Phase 6: fixtures/docs/changelog.

## Phase 5: Tests
Status: Complete

- [x] Unit (mirror the SMB set, using `FakeS3ClientAdapter`):
  `S3ConnectionStoreTests` (6), `S3ConnectionManagerTests` (4),
  `S3FileSystemProviderContractTests` (14), `S3FileStreamTests` (4),
  `S3BackendIdentityTests` (4), `S3ServerSideCopyProviderTests` (3). (Connection reconnect
  behaviour is exercised via the manager + provider tests; no separate `S3ConnectionTests`.)
- [x] UI: `S3ConnectDialogTests` (6), `S3ConnectToShareTests` (5) +
  `S3SharesPopoverMergeTests` (2), plus a `ConnectWindow` real-window S3-load test.
- [x] `tests/Duetto.Tests/Core/Remote/S3IntegrationTests.cs` — `[Trait("Category",
  "Integration")]`, gated on `DUETTO_S3_TEST`; endpoint/access/secret/bucket via env
  (defaults `http://127.0.0.1:9000` / `duetto` / `duettosecret` / `duetto`); covers list
  buckets, full lifecycle (mkdir marker, create/write/read, stat, rename, recursive
  enumerate, delete), server-side `CopyObject`, and an **anonymous read**.

### Verification Plan
- `dotnet test tests/Duetto.Tests/Duetto.Tests.csproj` → all green (integration tests
  self-skip because `DUETTO_S3_TEST` is unset).
- Integration: `docker compose up -d minio minio-setup` then
  `DUETTO_S3_TEST=1 dotnet test … --filter "Category=Integration&FullyQualifiedName~S3"` → green.

### Phase Summary
Done. Wrote `S3IntegrationTests` (the last remaining item — the unit/UI suites were
built alongside phases 1–4). **Ran them live against real MinIO: 4/4 pass** — bucket
listing, full object lifecycle, server-side `CopyObject` (2 MiB, exact bytes), and
anonymous public read.

One real-adapter fix surfaced by the anonymous test: `RealS3ClientAdapter.Connect` now
**skips eager validation for `S3AuthMode.Anonymous`**. A MinIO `download` policy grants
`GetObject` only (no `ListBucket`), so the previous bucket probe threw `AccessDenied` on
anonymous connect. Anonymous now connects without validation and authorizes per object
read. (Unit tests use the fake adapter, so unaffected.)

Verified: **full suite 639/639** with the gate unset (4 integration tests self-skip as
passes); **4/4 S3 integration tests green** against `docker compose up -d minio`.

Next (Phase 6): `scripts/smoke.sh` S3 env wiring, `docs/remote-s3.md`, README + CHANGELOG.

## Phase 6: Fixtures, docs & changelog
Status: Complete

- [x] `scripts/smoke.sh`: added the S3 env block (`DUETTO_S3_TEST=1`,
  `DUETTO_S3_TEST_ENDPOINT=http://127.0.0.1:9000`, `..._ACCESS=duetto`,
  `..._SECRET=duettosecret`, `..._BUCKET=duetto`) to the `Category=Integration` run;
  updated the banner to "SMB + SFTP + S3". (MinIO service + 9000 wait already present.)
- [x] `docs/remote-s3.md` — connection fields, three auth modes, bucket-as-root vs
  single-bucket, config location, object-store semantics (permanent delete, rename/move =
  server copy+delete, no `.part`, no mtime, multipart, folder-rename limitation), security
  caveat, and integration-test instructions.
- [x] `README.md` — added an S3 feature bullet linking `docs/remote-s3.md`.
- [x] `CHANGELOG.md` — new `## Unreleased` section with S3 backend + server-side copy bullets.

### Verification Plan
- `bash scripts/smoke.sh` → brings up compose, runs SMB+SFTP+S3 integration tests, all green, tears down.
- `dotnet build src/Duetto/Duetto.csproj && dotnet test tests/Duetto.Tests/Duetto.Tests.csproj` → green.

### Phase Summary
Done. `scripts/smoke.sh` now runs the S3 integration tests against the existing MinIO
service (banner updated); `docs/remote-s3.md` written; `README.md` + `CHANGELOG.md`
(new `## Unreleased`) updated.

Verified: `bash -n scripts/smoke.sh` OK; **the S3 block of smoke.sh runs 4/4 green** via
`docker compose up -d minio` with smoke's exact env values; app `dotnet build` clean;
**full suite 639/639**. Note: running the *whole* `scripts/smoke.sh` also needs a free
host **port 445** (Samba) and **2222** (SFTP); the S3 portion only needs **9000** (MinIO).

## Final Recap

S3 (and S3-compatible: MinIO, R2, Wasabi, B2) is now a first-class remote backend
alongside SFTP and SMB, addressed as `s3://{connId}/{bucket}/{key}`.

- **Core** (`src/Duetto.Core/Remote/`): `AWSSDK.S3` 4.0.101.6; `S3AuthMode`,
  `S3ConnectionInfo`, `S3ConnectionStore`/`StoredS3Connection`, `S3Connection`,
  `S3ConnectionManager` (registry scheme `"s3"`), `IS3ClientAdapter` +
  `RealS3ClientAdapter` (AWS SDK) + `S3FileStream` (temp-spool → `TransferUtility`),
  `S3FileSystemProvider` (`IFileSystemProvider`+`IBackendIdentity`+`IServerSideCopy`),
  `S3ConnectionException`/`S3AuthenticationException`; `ConnectSecret` gained
  `SessionToken`/`FromKeys`; `AppPaths.S3ConnectionsJsonPath`.
- **Auth**: access keys (+ optional STS session token), AWS profile, or anonymous.
  Anonymous requires a bucket (can't `ListBuckets`) and skips eager connect validation.
- **Namespace**: root lists all buckets, or a single configured bucket; folders are key
  prefixes with zero-byte markers for empties.
- **Capabilities**: no rename/trash/mtime/atomic-rename; move = server-side `CopyObject`
  (cross-bucket within a connection) + delete, offloaded via the existing `TransferEngine`
  path with zero client bytes; recursive search; multipart uploads; folder in-place
  rename/move intentionally unsupported.
- **UI**: `ConnectProtocol.S3` in the one protocol-aware Connect dialog (endpoint, region,
  path-style, auth-mode radios, keys/profile/bucket), `ConnectWindow` XAML, drive-popover
  merge (tagged **S3**), `MainViewModel.ConnectToS3Share` + seams, `PaneView`/`MainWindow`
  wiring.
- **Tests**: 38 unit + UI tests (store, streams, provider contract, backend identity,
  server-side copy incl. an engine-level move proving 0 client reads, manager, dialog,
  connect-to-share, popover merge, window load) + 4 gated MinIO integration tests
  (**verified live 4/4**). Full suite **639/639**.

## Deployment Plan

This ships in the **next release after v1.1.0** (a minor bump — new backward-compatible
feature — so **v1.2.0**). Steps:

1. **Branch/PR**: land these changes (currently on `feature/smb-samba-support`); open a PR
   to `main`, ensure CI/`dotnet test` is green (integration tests self-skip without
   `DUETTO_S3_TEST`).
2. **Optional pre-merge smoke**: with Docker running and ports 445/2222/9000 free,
   `bash scripts/smoke.sh` to exercise all three remote backends end-to-end.
3. **Version bump**: set `<Version>1.2.0</Version>` in `Directory.Build.props`; move the
   `## Unreleased` CHANGELOG section to `## 1.2.0 — <date>`.
4. **Merge to `main`**, then tag `v1.2.0` and push the tag
   (`git tag -a v1.2.0 -m "Duetto v1.2.0 — S3 backend" && git push origin v1.2.0`).
5. **GitHub release**: `gh release create v1.2.0` with notes from the 1.2.0 CHANGELOG
   section (mirrors how v1.1.0 shipped).
6. **Runtime note**: `AWSSDK.S3` (+ `AWSSDK.Core`) are new runtime dependencies pulled into
   `Duetto.Core`; no packaging/config changes are required beyond the normal `dotnet
   publish` (Version is injected from the tag in CI, `1.0.0`/local default otherwise).

# SMB / Samba Share Support

Add SMB/CIFS (Samba) network shares as a first-class remote backend in Duetto,
parallel to the existing SFTP backend: an in-app managed SMB2/3 client
(SMBLibrary) exposed through `IFileSystemProvider`, with its own connect dialog,
its own on-disk connection store, and full read/write parity with SFTP.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

Work on a branch: `feature/smb-samba-support` (AGENTS.md requires `feature/` prefix;
create it before touching code). Commit per phase with `feat:`/`test:`/`docs:` messages.

---

## Design Decisions & Constraints (locked with the user 2026-07-31)

These are the confirmed answers that shape every phase — do not re-litigate them.

1. **Approach: in-app managed SMB client.** Use **SMBLibrary** (TalAloni,
   `/talaloni/smblibrary`, NuGet `SMBLibrary`) — pure-managed SMB2/3 client, NTLM.
   No OS mount, no shelling out. A new `SmbFileSystemProvider : IFileSystemProvider`
   mirrors `SftpFileSystemProvider`. Cross-platform (net10.0).
2. **Scope: full parity with SFTP.** Implement the entire `IFileSystemProvider`
   surface (list, stat, exists, OpenRead, OpenWrite, CreateDirectory, CreateFile,
   Rename, Move, ReplaceFile, Delete recursive, SetLastWriteTimeUtc,
   EnumerateRecursive). Transfers work both directions.
3. **Auth: user/password/domain AND guest/anonymous.** No SSH keys, no host-key
   pinning (those are SFTP-only concepts and must NOT appear in the SMB dialog).
   `Domain`/`Workgroup` is an optional non-secret field. Guest = empty credentials.
4. **Share model: enumerate shares after connect.** Root `smb://id/` lists the
   server's shares (as directories). `smb://id/<share>/...` operates inside that
   share's tree. A connection points at a *host*, not a single share.
5. **UI/storage: SEPARATE SMB dialog + SEPARATE storage.** A dedicated
   `SmbConnectDialog` and a dedicated `smb-connections.json` /
   `SmbConnectionStore`, parallel to the SFTP ones — do NOT add a protocol
   discriminator to the existing SFTP `StoredConnection`. The drive popover's
   "Shares" list MERGES both sources (each row tagged with its scheme).
6. **Integration testing: fake-adapter contract tests (always on) PLUS a
   Dockerized Samba.** Ship a `docker-compose.smb.yml` + `scripts/smb-it.sh`, and
   `SmbIntegrationTests` gated on `DUETTO_SMB_TEST` (mirrors `SftpIntegrationTests`).

### Architecture facts this plugs into (verified in code)

- `IFileSystemProvider` (`src/Duetto.Core/FileSystem/IFileSystemProvider.cs`) is the
  backend abstraction. Paths handed to a provider are already provider-local
  (scheme/id stripped by `FileSystemRegistry`).
- `FileSystemRegistry` keys providers by `"{scheme}://{id}"` and is protocol-agnostic
  — **no change needed**; the SMB manager registers under scheme `"smb"`.
- `PathUtil.ParseRemote` already parses any `scheme://id/localpath` — **no change
  needed** for parsing. (`smb://` is handled generically.)
- The existing SFTP stack to mirror, file-by-file:
  - `Remote/SftpConnection.cs` → `ISftpClientAdapter` + `RealSftpClientAdapter` +
    `ISftpClientFactory` + `SftpConnection` (Connect/Disconnect/WithReconnect).
  - `Remote/SftpEntry.cs` → thin DTO returned by the adapter.
  - `Remote/SftpFileSystemProvider.cs` → the `IFileSystemProvider` impl + capabilities.
  - `Remote/ConnectionManager.cs` → lifecycle, registry (un)register, lock/eviction.
  - `Remote/ConnectionInfo.cs`, `Remote/ConnectionStore.cs` → info DTO + persistence.
  - `Remote/SecretCodec.cs`, `Remote/ConnectSecret.cs` → **reused as-is** for SMB.
  - `Remote/AppPaths.cs` → add `SmbConnectionsJsonPath`.
  - UI: `ViewModels/ConnectDialogViewModel.cs`, `Views/ConnectWindow.axaml(.cs)`,
    `ViewModels/DrivePopoverViewModel.cs` (`ShareRowViewModel`), and
    `ViewModels/MainViewModel.cs` (`ConnectToShare`, path building `sftp://…`).
- The `"sftp"` scheme literal appears only in `ConnectionManager.cs` (5×) and
  `MainViewModel.cs` (`sftp://{id}…`, lines ~210/219). SMB adds a parallel `"smb"`.

### SMBLibrary client API (confirmed via Context7 `/talaloni/smblibrary`)

```
var client = new SMB2Client();
client.Connect(ipAddress, SMBTransportType.DirectTCPTransport);   // resolve host→IP via Dns; port 445
NTStatus s = client.Login(domain, user, password);                // guest: Login("", "Guest", "")  (empty pass)
List<string> shares = client.ListShares(out s);
ISMBFileStore store = client.TreeConnect(shareName, out s);        // one tree per share; cache per share
store.CreateFile(out handle, out fileStatus, path, AccessMask, FileAttributes, ShareAccess, CreateDisposition, CreateOptions, null);
store.QueryDirectory(out fileList, dirHandle, "*", FileInformationClass.FileDirectoryInformation);
store.ReadFile(out data, handle, offset, (int)client.MaxReadSize);   // loop until STATUS_END_OF_FILE
store.WriteFile(out written, handle, offset, data);                  // chunk by client.MaxWriteSize
store.SetFileInformation(handle, new FileDispositionInformation { DeletePending = true });  // delete
store.SetFileInformation(handle, new FileRenameInformation { … ReplaceIfExists });          // rename/move
store.CloseFile(handle); store.Disconnect(); client.Logoff(); client.Disconnect();
```

**Gotchas / design notes to honor:**
- SMB2 paths use `\` separators and NO leading separator; share root = `""`. The
  provider presents `/` to the app and translates to SMB2 `\` at the adapter edge.
- `OpenRead`/`OpenWrite` must return a real `Stream` wrapping chunked
  `ReadFile`/`WriteFile` (offset tracking, chunk = `MaxReadSize`/`MaxWriteSize`),
  holding the file handle open and closing it (+ its tree if we choose per-stream
  trees) on `Dispose`. Call this `SmbFileStream`.
- `QueryDirectory` returns `.` and `..` — filter them (as the SFTP provider does).
- No POSIX permissions: `Capabilities.HasPermissions = false`. Map DOS `ReadOnly`
  attribute → `FileEntry.AccessSummary` ("R"/"RW"); leave `UnixPermissions` empty.
- Atomic replace: `FileRenameInformation.ReplaceIfExists = true` → `AtomicRename = true`,
  with a delete-then-rename fallback if the server rejects it.
- Connection drop → reconnect-once semantics, mirroring `SftpConnection.WithReconnect`
  (map SMBLibrary connection loss / a non-success `NTStatus` that means "disconnected"
  to the retry path).

### Proposed `SmbCapabilities`

```
CanRename=true, CanCreateEmptyDir=true, CanCreateFile=true, CanDelete=true,
HasTrash=false, HasPermissions=false, PreservesMTime=true, AtomicRename=true,
CanWatch=false, ReportsCapacity=false, SupportsSearch=true, CaseSensitive=false,
Separator='/'
```

### Build / test commands (this repo)

- Build: `dotnet build`
- Test (all): `dotnet test`
- Run app: `dotnet run --project src/Duetto`  (NOTE: AGENTS.md's `src/Phoenix/Host`
  line is a stale template — the real entry project is `src/Duetto`.)
- Targeted test filter: `dotnet test --filter "FullyQualifiedName~Smb"`

---

## Phase 0: Dependency spike & SMBLibrary validation
Status: Complete

Prove the SMBLibrary client works end-to-end against a throwaway Samba before
building the abstraction, and pin the exact API shapes / gotchas.

- [x] Create branch `feature/smb-samba-support`.
- [x] Add `SMBLibrary` `PackageReference` to `src/Duetto.Core/Duetto.Core.csproj`
      (pinned **1.5.7.1**, netstandard2.0 — compatible with net10.0).
- [x] Author `docker-compose.smb.yml` at repo root: `dperson/samba` (linux/amd64),
      publishes **445:445** (SMBLibrary dials 445 directly — no custom port), auth
      share `duetto` (smbuser/smbpass, WORKGROUP) + guest share `public`.
- [x] Ran a throwaway spike console (in scratchpad, not committed) against the
      container: Connect → Login → ListShares → TreeConnect → CreateFile+WriteFile →
      ReadFile roundtrip → QueryDirectory → delete via `FileDispositionInformation`.
      All `STATUS_SUCCESS`; read-back returned the written bytes.
- [x] Confirmed & recorded gotchas (see updated "Gotchas" section above + summary below).
- [x] Throwaway spike lives only in the session scratchpad — nothing to remove from the repo.

### Verification Plan
- `dotnet build` succeeds with the new package reference.
- `docker compose -f docker-compose.smb.yml up -d` starts; the spike lists both
  `duetto` and `public` shares. Expected: both shares visible, read/write roundtrip
  returns the written bytes.

### Phase Summary
**Done.** SMBLibrary **1.5.7.1** added to `Duetto.Core`. `docker-compose.smb.yml`
uses **`dperson/samba`** (`platform: linux/amd64`), host port **445:445**, share
`duetto` (user **smbuser** / pass **smbpass** / workgroup **WORKGROUP**) and guest
share `public`. Spike proved the full client flow green.

**Confirmed API facts (pin these for Phases 1–2):**
- `SMB2Client.Connect(IPAddress, SMBTransportType.DirectTCPTransport)` — **no
  custom-port overload**; port 445 fixed → host must publish 445. Resolve host→IP
  via `Dns.GetHostAddresses` in the factory.
- `SMB2Client.IsConnected` (bool) exists → use it in `WithReconnect` pre-check.
- `MaxReadSize == MaxWriteSize == MaxTransactSize == 1048576` (1 MB) → stream chunk size.
- **Reconnect trigger:** after a drop, store ops throw
  `System.InvalidOperationException` with message **"The client is no longer
  connected"** (NOT an `NTStatus`). `WithReconnect` catches `InvalidOperationException`
  (+ pre-checks `IsConnected`) → reconnect once → retry.
- `QueryDirectory` returns `.` and `..` (filter them) and its terminal status is
  **`STATUS_NO_MORE_FILES`**, not `STATUS_SUCCESS` — treat both as success and, for
  large dirs, **loop** `QueryDirectory` until `STATUS_NO_MORE_FILES`.
- `FileDirectoryInformation` fields used: `FileName`, `FileAttributes` (has
  `Directory`/`ReadOnly` flags), `EndOfFile` (size), `LastWriteTime`.
- Guest: `Login("", "Guest", "")` → `STATUS_SUCCESS`; guest can `ListShares` +
  read/write the `public` share.
- **`FileAttributes` name collision:** `SMBLibrary.FileAttributes` vs
  `System.IO.FileAttributes` (ImplicitUsings on) → add
  `using FileAttributes = SMBLibrary.FileAttributes;` in every SMB file that uses it.
- Delete: open with `DELETE` access → `SetFileInformation(FileDispositionInformation{DeletePending=true})` → close.
- Rename/Move: `FileRenameInformationType2 { ReplaceIfExists, FileName }` (SMB2 dialect;
  Type1 also exists — verify Type2 is what `SMB2FileStore` expects in Phase 2).
- Set mtime: `SetFileInformation(FileBasicInformation{ LastWriteTime = utc })` (leave
  other time fields default/0 = "unchanged").
- Set length/truncate: `FileEndOfFileInformation`.

Container left **running** for Phases 2 & 5 (tear down with
`docker compose -f docker-compose.smb.yml down` when finished).

## Phase 1: SMB connection layer (Core)
Status: Complete

Build the low-level, testable adapter + connection wrapper — the SMB analogue of
`SftpConnection.cs`. No `IFileSystemProvider` yet.

- [x] `Remote/SmbEntry.cs`: thin record (`Name`, `FullName`, `IsDirectory`,
      `IsReadOnly`, `Length`, `LastWriteTimeUtc`).
- [x] `Remote/SmbConnection.cs` → `ISmbClientAdapter` interface (+ `ISmbClientFactory`,
      `SmbConnection`, `SmbConnectionException`, `SmbAuthenticationException`).
- [x] `RealSmbClientAdapter` (SMB2Client): per-share `ISMBFileStore` cache, DNS
      resolution, share/path split + `/`→`\` translation, `NTStatus`→exception mapping.
      Returns `.`/`..` raw (provider filters, mirroring SFTP).
- [x] `DefaultSmbClientFactory`: builds `RealSmbClientAdapter`; connect = DirectTCP 445
      + `Login` (domain/user/pass, or guest `Login("","Guest","")`). Creates un-connected.
- [x] `SmbConnection`: `Connect`/`Disconnect`/`WithReconnect<T>`/`WithReconnect` +
      `Adapter`, mirroring `SftpConnection` reconnect-once. Catches `SmbConnectionException`
      only. No `HostKeyStore`.
- [x] `Remote/SmbFileStream.cs`: forward-only `Stream` over chunked `ReadFile`/`WriteFile`
      (buffered writes flush at chunk size; reads pull chunk-sized server reads); closes
      handle on `Dispose`.
- [x] Pulled forward (needed here): `Remote/SmbConnectionInfo.cs` (Phase 3 item) and
      `tests/.../FakeSmbClientAdapter.cs` + `FakeSmbFactory` (Phase 2 item).
- [x] Added `<InternalsVisibleTo Include="Duetto.Tests" />` to `Duetto.Core.csproj`
      (SmbFileStream is internal).

### Verification Plan
- `dotnet build` succeeds.
- `dotnet test --filter "FullyQualifiedName~SmbConnection|FullyQualifiedName~SmbFileStream"`
  passes. Expected: green.

### Phase Summary
**Done. 9/9 tests green.** `SmbConnection` + `RealSmbClientAdapter` + `SmbFileStream`
implemented. Key decisions:
- **Reconnect** keys on the typed `SmbConnectionException`. `RealSmbClientAdapter.Run`
  wraps every op: pre-checks `client.IsConnected` and converts SMBLibrary's
  `InvalidOperationException`/`SocketException` (dropped socket) into `SmbConnectionException`
  so `WithReconnect` retries exactly once.
- **`SmbFileStream`** is delegate-backed (testable without a socket); a real bug was
  caught + fixed — the write buffer is reused across flushes, so `FlushWrite` now always
  hands out a right-sized **copy** (a Stream must not mutate data a caller may hold).
- **`.`/`..` filtering** lives in the provider (Phase 2), not the adapter — the adapter
  returns raw listings, matching `SftpFileSystemProvider`.
- **Port limitation:** SMBLibrary has no custom-port `Connect`, so `SmbConnectionInfo.Port`
  is stored for parity but the transport always uses 445 (documented in the record).
- `FakeSmbClientAdapter` models an in-memory tree keyed by provider paths; top-level dirs
  are the shares (`ListShares`), and it emits `.`/`..` like a real server.

## Phase 2: SmbFileSystemProvider + contract tests
Status: Complete

Implement the full `IFileSystemProvider` and prove parity via the shared contract.

- [x] `Remote/SmbFileSystemProvider.cs : IFileSystemProvider, IDisposable`:
      `SmbCapabilities`; `Exec` lock+`WithReconnect` wrapper; `MapEntry`; root `/` →
      share list as directories; deeper paths routed into the share tree; `Move` guards
      existing target; `ReplaceFile` atomic rename with delete+rename fallback (guarded
      so it never swallows connection/auth drops); recursive `Delete`;
      `EnumerateRecursive` (incl. root=share-list) skipping bad dirs but propagating
      `SmbConnectionException`/`SmbAuthenticationException`; `VolumeFor` → null.
- [x] `FakeSmbClientAdapter` (created in Phase 1) reused; added a `MarkReadOnly` test hook.
- [x] `tests/.../Core/Remote/SmbFileSystemProviderContractTests.cs`: subclasses
      `FileSystemProviderContract` with `Root = "/Shared"`; SMB-specific tests: root
      lists shares, `.`/`..` filtered, readonly→`AccessSummary`, mtime roundtrip,
      reconnect-once, EnumerateRecursive skip-bad-dir + propagate-auth. **19/19 green.**
- [x] Pulled forward from Phase 5: `SmbIntegrationTests` (gated on `DUETTO_SMB_TEST`) to
      validate the REAL adapter against the live container now — de-risks SMBLibrary flag
      choices before building UI.

### Verification Plan
- `dotnet test --filter "FullyQualifiedName~SmbFileSystemProviderContract"` — 19/19 green.
- `dotnet test` (full suite) — **530 passed, 0 failed** (no regressions).
- Live: `DUETTO_SMB_TEST=1 … dotnet test --filter FullyQualifiedName~SmbIntegrationTests`
  against `docker-compose.smb.yml` — **3/3 green**; skips (5 ms) without the env var.

### Phase Summary
**Done.** `SmbFileSystemProvider` passes the shared provider contract that Local /
InMemory / SFTP satisfy, plus SMB-specific tests. **Validated against real Samba**: the
`SmbIntegrationTests` roundtrip (create/write/read/mtime/rename/atomic-replace/enumerate/
recursive-delete) and guest write to `public` all pass through the real SMBLibrary path —
confirming the earlier flag choices (`FileRenameInformationType2`, `SetFileTime` mtime,
`FILE_OVERWRITE_IF`, `ReplaceIfExists`). `.`/`..` filtering lives in the provider.
Capabilities: `HasPermissions=false`, `AtomicRename=true`, `HasTrash=false`,
`CaseSensitive=false`, `Separator='/'`.

## Phase 3: SMB connection manager + storage (Core)
Status: Complete

Lifecycle + persistence, parallel to `ConnectionManager` / `ConnectionStore`, with
SEPARATE storage.

- [x] `Remote/SmbConnectionInfo.cs` (created in Phase 1).
- [x] `Remote/SmbConnectionStore.cs` + `StoredSmbConnection` DTO (`id, name, host, port,
      username, domain, guest, initialPath, savePassword, obfuscatedSecret`). Reuses
      `SecretCodec` + `ConnectSecret.FromPassword`. Guest → never persists a secret but
      `ResolveSecret` returns an empty password so connect needs no prompt.
- [x] `Remote/AppPaths.cs`: added `SmbConnectionsJsonPath` (`smb-connections.json`).
- [x] `Remote/SmbConnectionManager.cs`: registers `SmbFileSystemProvider` under scheme
      `"smb"`; mirrors `ConnectionManager` lock / evict-outside-lock / dispose. No
      `HostKeyStore`.
- [x] Tests: `SmbConnectionManagerTests` (register/resolve, replace-on-reconnect,
      connect-failure cleanup, dispose, case-insensitive ids, lock-scope responsiveness)
      + `SmbConnectionStoreTests` (roundtrip, obfuscate/decrypt, no-save, guest). Added
      concurrency gate hooks to `FakeSmbClientAdapter`. **17/17 green.**

### Verification Plan
- `dotnet test --filter "FullyQualifiedName~SmbConnectionManager|FullyQualifiedName~SmbConnectionStore"`
  — 17/17 green.
- `dotnet test` (full suite) — **550 passed, 0 failed**.

### Phase Summary
**Done.** SMB lifecycle + persistence complete and isolated from SFTP: separate
`smb-connections.json`, separate `SmbConnectionStore`/`SmbConnectionManager`, scheme
`"smb"` in the shared `FileSystemRegistry`. Registry keys are `smb://id`, so SMB and SFTP
ids never collide. Guest connections resolve without a prompt.

## Phase 4: UI — SMB connect dialog + merged shares popover
Status: Complete

Separate SMB dialog; drive popover merges SFTP + SMB shares.

- [ ] `ViewModels/SmbConnectDialogViewModel.cs`: fields `Name, Host, PortText="445",
      Username, Password, Domain, Guest (bool), InitialPath, SavePassword` + error/
      connecting state; validation (host required; user required unless Guest; port
      1–65535); `BuildInfo`/`BuildSecret`; `ConnectAsync` catching SMB/socket/IO errors.
      NO host-key and NO key-auth rows.
- [ ] `Views/SmbConnectWindow.axaml` (+ `.cs`): mirror `ConnectWindow` with SMB fields
      (Domain + Guest checkbox; no key/host-key sections).
- [ ] `ViewModels/DrivePopoverViewModel.cs`: `ShareRowViewModel` gains `Scheme`
      (`"sftp"`/`"smb"`); `ListConnections`/`IsConnected` seams merge both stores;
      `RebuildShareRows` combines SFTP + SMB rows (tagged with scheme); edit/remove
      route to the correct store; disconnect label resolves across both.
- [ ] `ViewModels/MainViewModel.cs`: construct + hold `SmbConnectionManager` +
      `SmbConnectionStore`; `ConnectToShare` routes by row scheme to the right manager
      and builds `smb://{id}{initialPath}` vs `sftp://…`; edit/remove dispatch to the
      right store; add a "Connect SMB" entry point (two buttons: "Connect SFTP" /
      "Connect SMB", or a small protocol chooser); session-restore fallback treats
      `smb://` like `sftp://` (fall back to home when not connected).
- [ ] Wire `OpenConnectDialog` seam for SMB (owner-window plumbing like SFTP).
- [ ] UI tests: `SmbConnectDialogTests`, `SmbConnectToShareTests`, and a popover-merge
      test (both scheme rows appear, activate routes to the right scheme) mirroring
      `ConnectDialogTests` / `ConnectToShareTests` / `SharesPopoverTests`.

### Verification Plan
- `dotnet test --filter "FullyQualifiedName~SmbConnectDialog|FullyQualifiedName~SmbConnectToShare|FullyQualifiedName~SmbSharesPopoverMerge"`
  — 15/15 green.
- `dotnet build src/Duetto` — Avalonia XAML compiles, 0 errors.
- `dotnet test` (full suite) — **565 passed, 0 failed** (SFTP UI tests unaffected).

### Phase Summary
**Done.** SMB is reachable from the UI with **zero changes to the SFTP event surface**
(existing tests untouched):
- `SmbConnectDialogViewModel` + `SmbConnectWindow.axaml(.cs)`: host/port/user/password/
  domain/guest/initial-path; guest hides credentials; SMB-specific exception handling.
  No SSH-key / host-key rows.
- Popover merge: `ShareRowViewModel` gained a `Scheme` discriminator + second ctor
  (`StoredSmbConnection`) + `SchemeLabel` badge; `DrivePopoverViewModel` got parallel
  seams (`ListSmbConnections`/`IsSmbConnected`), parallel events
  (`ConnectSmbRequested`/`EditSmbShareRequested`/`RemoveSmbShareRequested`), a
  `ConnectSmbCommand`, and `RebuildShareRows` appends SMB rows. `ShareActivated` stays
  row-based; the view routes by `row.Scheme`.
- `MainViewModel`: holds `SmbConnectionManager` + `SmbConnectionStore`; `ConnectToSmbShare`
  (mirrors `ConnectToShare`, builds `smb://id/path`); `OpenSmbConnectDialog` seam;
  disposes the SMB manager.
- `PaneView.axaml(.cs)`: "Connect SMB…" button; `ActivateShare`/`DisconnectCurrentPane`/
  remove routed by scheme; `OpenSmbConnectDialogCore` opens `SmbConnectWindow`.
- **Known v1 limitations (accepted):** the separate `RemotePlaces` sidebar stays SFTP-only
  (popover is the SMB entry point); SMB free-space/capacity is not shown
  (`ReportsCapacity=false`, matching SFTP). Guest is modeled as a saved connection with a
  `Guest` flag.

## Phase 5: Integration tests, Docker Samba & docs
Status: Not started

Reproducible live-server coverage + user-facing docs.

- [ ] Finalize `docker-compose.smb.yml` (from Phase 0) as the committed integration
      fixture: authenticated `duetto` share + guest `public` share, fixed creds.
- [ ] `scripts/smb-it.sh`: `docker compose -f docker-compose.smb.yml up -d` → wait for
      445 healthy → export `DUETTO_SMB_TEST=1` + host/port/user/pass/domain →
      `dotnet test --filter "Category=Integration&FullyQualifiedName~Smb"` → `down` on exit.
- [ ] `tests/.../Core/Remote/SmbIntegrationTests.cs` (`[Trait("Category","Integration")]`,
      gated on `DUETTO_SMB_TEST`, early-return when unset — mirror `SftpIntegrationTests`):
      connect → list shares → tree → write/read roundtrip → rename → recursive delete →
      guest-share read.
- [ ] Docs: add `docs/smb.md` (mirror the existing SFTP doc), a README feature line,
      and a `CHANGELOG.md` entry. Optional: a popover screenshot showing an SMB share.
- [ ] Optional: CI wiring to run `scripts/smb-it.sh` (note as optional; SFTP integration
      is not in CI either).

### Verification Plan
- `bash scripts/smb-it.sh` exits 0 with all SMB integration tests passing against the
  container. Expected: green; container torn down afterward.
- `dotnet test` (default, no env var) stays green and does NOT touch the network
  (integration tests skip). Expected: green.

### Phase Summary
_(write when phase completes)_

## Final Recap
_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan
_(write when all phases complete: step-by-step release/integration instructions —
merge branch, version bump, changelog, packaging)_

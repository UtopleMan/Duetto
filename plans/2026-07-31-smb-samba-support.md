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
Status: Not started

Build the low-level, testable adapter + connection wrapper — the SMB analogue of
`SftpConnection.cs`. No `IFileSystemProvider` yet.

- [ ] `Remote/SmbEntry.cs`: thin record (`Name`, `FullName`, `IsDirectory`,
      `IsReadOnly`, `Length`, `LastWriteTimeUtc`).
- [ ] `Remote/SmbConnection.cs` → `ISmbClientAdapter` interface: `IsConnected`,
      `Connect`, `Disconnect`, `ListShares()`, `ListDirectory(path)`, `Get(path)`,
      `IsDirectory`, `IsFile`, `CreateDirectory`, `CreateFile`, `RenameFile(old,new,replace)`,
      `DeleteFile`, `DeleteDirectory`, `Exists`, `OpenRead→Stream`, `OpenWrite→Stream`,
      `SetLastWriteTimeUtc`. Paths are provider-local (`/share/dir/...`); the adapter
      owns the share→`TreeConnect` split and `/`→`\` translation.
- [ ] `RealSmbClientAdapter` (SMB2Client): per-share `ISMBFileStore` cache, DNS
      resolution, path translation, `NTStatus`→exception mapping.
- [ ] `ISmbClientFactory` + `DefaultSmbClientFactory`: build+configure `SMB2Client`
      (DirectTCP, port), perform `Login` with domain/user/pass or guest. Creates but
      does NOT connect (mirrors `ISftpClientFactory`).
- [ ] `SmbConnection` class: `Connect`/`Disconnect`/`WithReconnect<T>`/`WithReconnect`
      + `Adapter` property, mirroring `SftpConnection` reconnect-once semantics. No
      `HostKeyStore`.
- [ ] `Remote/SmbFileStream.cs`: `Stream` over chunked `ReadFile`/`WriteFile` with
      offset tracking; closes handle (and tree if per-stream) on `Dispose`.

### Verification Plan
- `dotnet build` succeeds.
- `dotnet test --filter "FullyQualifiedName~SmbConnection"` passes (unit tests added
  here for `WithReconnect` reconnect-once + `SmbFileStream` chunk math using a fake
  adapter — no socket). Expected: green.

### Phase Summary
_(write when phase completes)_

## Phase 2: SmbFileSystemProvider + contract tests
Status: Not started

Implement the full `IFileSystemProvider` and prove parity via the shared contract.

- [ ] `Remote/SmbFileSystemProvider.cs : IFileSystemProvider, IDisposable`:
      `SmbCapabilities` (values above); `Exec` lock+`WithReconnect` wrapper;
      `MapEntry(SmbEntry)→FileEntry`; root `/` → share list mapped as directories;
      deeper paths routed into the share tree; `Move` guards existing target
      (throws `IOException`); `ReplaceFile` atomic rename with delete+rename fallback;
      recursive `Delete`; `EnumerateRecursive` skipping bad dirs but propagating
      auth/connection failures (mirror SFTP semantics); `VolumeFor` → null.
- [ ] `tests/.../Core/Remote/FakeSmbClientAdapter.cs`: in-memory tree with top-level
      shares + a controllable "throw once" hook (mirror `FakeSftpClientAdapter`).
- [ ] `tests/.../Core/Remote/SmbFileSystemProviderContractTests.cs`: subclass
      `FileSystemProviderContract` with `Root = "/Shared"` (a dir inside a fake share);
      add SMB-specific tests: root lists shares, `.`/`..` filtered, readonly attr →
      `AccessSummary`, mtime roundtrip, reconnect-once on a simulated drop.

### Verification Plan
- `dotnet test --filter "FullyQualifiedName~SmbFileSystemProviderContract"` passes —
  the same contract that Local/InMemory/SFTP satisfy. Expected: all contract tests green.
- `dotnet test` (full suite) stays green (no regressions).

### Phase Summary
_(write when phase completes)_

## Phase 3: SMB connection manager + storage (Core)
Status: Not started

Lifecycle + persistence, parallel to `ConnectionManager` / `ConnectionStore`, with
SEPARATE storage.

- [ ] `Remote/SmbConnectionInfo.cs`: `Id, Name, Host, Port=445, Username, Domain="",
      Guest=false, InitialPath="/"`.
- [ ] `Remote/SmbConnectionStore.cs` + `StoredSmbConnection` DTO (`id, name, host,
      port, username, domain, guest, initialPath, savePassword, obfuscatedSecret`).
      Reuse `SecretCodec` + `ConnectSecret.FromPassword`; guest → no secret.
      `Load`/`Save`/`Pack`/`Resolve*` mirror `ConnectionStore`.
- [ ] `Remote/AppPaths.cs`: add `SmbConnectionsJsonPath` (e.g. `smb-connections.json`
      alongside `connections.json`).
- [ ] `Remote/SmbConnectionManager.cs`: registers `SmbFileSystemProvider` under scheme
      `"smb"`; mirror `ConnectionManager` lock/eviction/dispose. No `HostKeyStore`.
- [ ] Tests: `SmbConnectionManagerTests`, `SmbConnectionStoreTests`,
      `SmbConnectionInfoTests` mirroring their SFTP counterparts (fake factory/adapter).

### Verification Plan
- `dotnet test --filter "FullyQualifiedName~SmbConnection|FullyQualifiedName~SmbConnectionManager|FullyQualifiedName~SmbConnectionStore"`
  passes. Expected: green — register/unregister/evict/reconnect + JSON roundtrip + guest
  (no-secret) case covered.

### Phase Summary
_(write when phase completes)_

## Phase 4: UI — SMB connect dialog + merged shares popover
Status: Not started

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
- `dotnet test --filter "FullyQualifiedName~SmbConnectDialog|FullyQualifiedName~SmbConnectToShare|FullyQualifiedName~SharesPopover"`
  passes. Expected: green.
- `dotnet build` of `src/Duetto` (Avalonia XAML compiles). Expected: no XAML/compile errors.
- Manual smoke (documented, not required for autonomous pass): `dotnet run --project src/Duetto`,
  Connect SMB → guest `public` share lists; authenticated `duetto` share read/write works.

### Phase Summary
_(write when phase completes)_

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

# Remote backends — SFTP (v1)

Add real remote connections to Duetto, starting with SFTP, behind a provider
abstraction with a capability descriptor so later odd backends (S3, SMB, WebDAV)
can opt out of features and the app degrades gracefully. This first slice delivers
full browse + manage + copy/move + search over SFTP, replacing the Connect stub.

## Decisions (locked with user)

- **Backend:** SFTP only this slice. S3 / SMB are later sub-projects on the same seam.
- **Ops depth:** full — list/navigate, new folder/file, rename, delete, F5/F6
  copy/move to/from remote (local↔remote and remote↔remote), and recursive search.
- **Auth:** password **or** private-key file (+ optional passphrase).
- **Host key:** trust-on-first-use — pin fingerprint on first connect, warn/block
  on change.
- **Persistence:** connections saved to a local JSON config in the OS app-data dir.
  Per-connection "save password" toggle; unsaved secrets prompted at connect; key
  path always saved; passphrase optional. Secrets obfuscated with a machine-derived
  key (reversible, **not** strong crypto — labelled as such in the UI).
- **Remote delete:** permanent, recursive, **no prompt** (SFTP has no Trash).
- **Deferred to later sub-projects:** live watch on remote (manual refresh only —
  no `FileSystemWatcher`); Enter-to-open a remote file (download-and-open); S3/SMB.
- **Library:** SSH.NET (`Renci.SshNet`). Confirm net10 support + pin a version in
  Phase 2 (Context7 / nuget); it targets netstandard2.0 so it should load on net10.
- **Config path:** macOS `~/Library/Application Support/Duetto/`, Linux XDG
  `~/.config/duetto/`, Windows `%APPDATA%\Duetto\`. Files: `connections.json`,
  `hostkeys.json`.
- **Testing:** an in-memory fake `IFileSystemProvider` drives all unit/headless
  tests; a real SFTP server integration test is gated behind an env var and skipped
  by default.

## Architecture

- **`IFileSystemProvider`** (Duetto.Core) is the seam: `List`, `DirectoryExists`,
  `FileExists`, `Stat`, `CreateDirectory`, `CreateFile`, `Rename`, `Delete(path,
  toTrash)`, `OpenRead`, `OpenWrite`, `SetLastWriteTimeUtc`, `EnumerateRecursive`,
  `VolumeFor`. Each provider exposes a **`FileSystemCapabilities`** record
  (`CanRename`, `CanCreateEmptyDir`, `CanCreateFile`, `CanDelete`, `HasTrash`,
  `HasPermissions`, `PreservesMTime`, `AtomicRename`, `CanWatch`, `ReportsCapacity`,
  `SupportsSearch`, `CaseSensitive`, `Separator`). Optional methods throw
  `NotSupportedException` as a backstop, but callers gate on capabilities first.
- **Addressing:** local paths stay as today. Remote locations are the string
  `sftp://<connectionId>/<remote/path>`. A **`FileSystemRegistry`** resolves a path
  to `(provider, providerLocalPath)`; a **`PathUtil`** performs parent/combine/leaf
  using the resolved provider's `Separator` (replacing raw `Path.*` in navigation).
- **Consumers routed through the registry:** `DirectoryLister`→`LocalFileSystemProvider`,
  `FileOps`, `TransferEngine`, `TrashService`, `SearchService`, and
  `PaneViewModel` navigation.
- **Connections:** an `SftpConnection` (SSH.NET client + reconnect) built from a
  `ConnectionInfo` record; a `ConnectionManager` owns live connections keyed by id
  and hands `SftpFileSystemProvider` instances to the registry.

---

## Phase 1: Provider abstraction + local refactor (behavior-preserving)
Status: Complete

- [x] Add `Duetto.Core/FileSystem/IFileSystemProvider.cs` and
  `FileSystemCapabilities.cs` (record as specified in Architecture).
- [x] Add `Duetto.Core/FileSystem/LocalFileSystemProvider.cs` wrapping the existing
  local behavior (`DirectoryLister.List`, `FileOps`, `TrashService`, `DriveInfo`
  streams); `Capabilities` all-true, `Separator = Path.DirectorySeparatorChar`,
  `HasTrash = true`.
- [x] Add `Duetto.Core/FileSystem/FileSystemRegistry.cs` (`Resolve(path) →
  (IFileSystemProvider, string localPath)`; default resolves everything to the
  local provider) and `Duetto.Core/FileSystem/PathUtil.cs` (`Parent`, `Combine`,
  `Leaf`, `IsRemote`, scheme parsing) that delegates to the resolved separator.
- [x] Route `FileOps` (`SuggestEntryName`, `CreateFolder`, `CreateFile`, `Rename`)
  and `TrashService` through a provider parameter/registry instead of calling
  `Directory`/`File` directly. Keep existing public signatures working via a local
  default so callers compile.
- [x] Route `PaneViewModel.Lister` default and navigation helpers (`DirName`,
  `Up`, breadcrumb math, `CanGoUp`) through `PathUtil`/registry rather than raw
  `Path.*`, so a remote address parses correctly. Local behavior unchanged.
- [x] Generalize `TransferEngine` to take source+dest `IFileSystemProvider`
  (stream copy via `OpenRead`/`OpenWrite`; `.part`+rename only when
  `dest.Capabilities.AtomicRename`; mtime copy only when `PreservesMTime`; move =
  native `Rename` when same provider and `CanRename`, else copy+delete). Local↔local
  path must be byte-for-byte equivalent to today.
- [x] Generalize `SearchService` recursive walk to `provider.EnumerateRecursive` +
  `OpenRead` for content search, gated on `SupportsSearch`.

### Verification Plan
- `dotnet build Duetto.slnx` → 0 errors, 0 warnings.
- `dotnet test Duetto.slnx` → all existing tests green (baseline 154), proving the
  local refactor changed no behavior.
- New Core tests: `LocalFileSystemProvider` satisfies a shared provider-contract
  test suite (list/create/rename/delete/read/write round-trips in a `TempDir`);
  `PathUtil` parses both a local path and `sftp://id/a/b` (parent/leaf/combine).

### Phase Summary
Done 2026-07-28, commits 2da9e91..c37779f. Every consumer now routes through the
seam: `FileOps` (+ provider-aware `NewFolder`) and trash via
`provider.Delete(path, toTrash)`; `PaneViewModel` navigation through
`PathUtil`/injectable `FileSystemRegistry` with the `FileSystemWatcher` gated
off for remote addresses (`HasActiveWatcher` test seam); `TransferEngine` takes
source+dest providers with capability-gated `.part`/mtime/move strategies —
review caught and fixed an atomicity regression by adding
`IFileSystemProvider.ReplaceFile` (local = `File.Move(overwrite: true)`, single
`rename(2)`); `SearchService` walks `provider.EnumerateRecursive` + `OpenRead`
gated on `SupportsSearch` (symlink-skip and TCC per-MoveNext guards ported into
`LocalFileSystemProvider.EnumerateRecursive` verbatim). Suite 154 → 208 green,
no pre-existing test modified; build has 0 errors and only the 5 pre-existing
branch warnings (3× CS4014, MVVMTK0034, xUnit2031 — untouched, out of scope).
Deferred for later phases: Back/Forward bypass the `DirectoryExists` guard
(pre-existing, matters when remote mounts vanish — Phase 5);
`SearchViewModel.ScopeDirName` still raw `Path.GetFileName` (Phase 4);
`.part` sibling-name collision (pre-existing).

## Phase 2: SFTP provider over SSH.NET
Status: Complete

- [x] Add `Renci.SshNet` PackageReference to `Duetto.Core`; confirm net10 restore +
  pin version (Context7 / `dotnet add package`).
- [x] Add `Duetto.Core/Remote/ConnectionInfo.cs` (record: `Id`, `Name`, `Host`,
  `Port=22`, `Username`, `AuthMode` {Password|Key}, `KeyPath?`, initial `RemotePath`).
- [x] Add `Duetto.Core/Remote/SftpConnection.cs` — opens an SSH.NET `SftpClient`
  from `ConnectionInfo` + a resolved secret; exposes connect/disconnect/is-connected;
  single reconnect attempt on a dropped op.
- [x] Add `Duetto.Core/Remote/SftpFileSystemProvider.cs` implementing
  `IFileSystemProvider` over the connection: `List`/`Stat` (map SFTP attrs →
  `FileEntry`, Unix perms, mtime), `CreateDirectory`/`CreateFile`, `Rename` (SFTP
  rename), `Delete` (recursive, `HasTrash=false`), `OpenRead`/`OpenWrite`,
  `SetLastWriteTimeUtc` (setstat), `EnumerateRecursive`. `Capabilities`:
  `CanRename/CanCreateEmptyDir/CanCreateFile/CanDelete/HasPermissions/PreservesMTime/
  AtomicRename/SupportsSearch = true`, `HasTrash/CanWatch/ReportsCapacity = false`,
  `Separator='/'`, `CaseSensitive=true`.
- [x] Add `Duetto.Core/Remote/HostKeyStore.cs` — TOFU: on first connect record the
  server fingerprint; on later connects compare and raise a
  `HostKeyChangedException` (carrying old/new fingerprint) when it differs. Wire via
  SSH.NET `HostKeyReceived`.
- [x] Add `Duetto.Core/Remote/ConnectionManager.cs` — owns live connections by id,
  builds `SftpFileSystemProvider`, registers them with `FileSystemRegistry` for the
  `sftp://<id>/...` scheme; connect/disconnect/dispose-all.

### Verification Plan
- `dotnet build Duetto.slnx` → clean.
- `SftpFileSystemProvider` passes the **same** provider-contract test suite as the
  local provider, run against an in-memory fake SFTP backend (no network).
- `HostKeyStore` unit tests: first-use pins; unchanged key passes; changed key
  raises `HostKeyChangedException`.
- Real-server integration test in `tests/.../Remote/SftpIntegrationTests.cs` marked
  `[Trait("Category","Integration")]` and skipped unless `DUETTO_SFTP_TEST` env var
  is set — documents the manual smoke against a live server.

### Phase Summary
Done 2026-07-29, commits 9867739..5d6fbcb. SSH.NET 2025.1.0 pinned (net10 restore
clean). Connection layer: ConnectionInfo/ConnectSecret records, TOFU HostKeyStore
(Verify core + IHostKeyPersistence seam for Phase 3, CanTrust forced false before
verify since SSH.NET defaults it true), SftpConnection with injectable
ISftpClientFactory + WithReconnect (reconnect once on SshConnectionException,
then propagate; dispose-on-failed-connect fixed in review). Provider:
SftpFileSystemProvider implements the full seam incl. ReplaceFile (posix rename)
and Move; narrow SftpEntry adapter record avoids faking SSH.NET's 90-member
interfaces; passes the shared FileSystemProviderContract unmodified on an
in-memory fake; EnumerateRecursive swallows per-directory non-connection
SshExceptions; OpenRead/OpenWrite documented as connection-bound (mitigation in
Phase 5's transfer retry). ConnectionManager registers sftp://<id> providers in
the registry with the SSH handshake outside the manager lock, case-insensitive
ids unregistering by stored casing, and airtight failed-connect cleanup.
Suite 228 → 306 green; 2 integration tests gated on DUETTO_SFTP_TEST (no-op
otherwise). Known deferred: connection-bound stream lifetime (Phase 5 transfer
retry); fake IsFile true for symlinks; 0-assertion integration skips; three
zombie-agent incidents during execution reconciled — all committed work
controller-verified.

## Phase 3: Connection config store + secrets
Status: Complete

- [x] Add `Duetto.Core/Remote/AppPaths.cs` — per-OS config dir (mac Application
  Support / linux XDG / win APPDATA), `connections.json`, `hostkeys.json`.
- [x] Add `Duetto.Core/Remote/SecretCodec.cs` — reversible obfuscation with a
  machine-derived key (e.g. AES from a machine-id + app-salt); explicitly documented
  as obfuscation, not secure storage.
- [x] Add `Duetto.Core/Remote/ConnectionStore.cs` — load/save `ConnectionInfo[]`
  (JSON); per-connection `SavePassword` flag; when false the secret field is empty
  and resolved at connect time from a prompt; key path always saved; passphrase
  optional. Injectable file IO + codec for tests.
- [x] Persist host-key fingerprints via `HostKeyStore` into `hostkeys.json`.

### Verification Plan
- `dotnet test` Core: config round-trips (save→load equality) incl. save-password
  on/off; `SecretCodec` encode→decode is identity and ciphertext ≠ plaintext;
  corrupt/missing file loads to an empty list without throwing; host-key store
  persists and reloads pinned fingerprints.

### Phase Summary
Done 2026-07-29, commits f7d6bb3 + 64bb8e6 (single-task phase; task review served
as the phase gate). AppPaths (per-OS config dir, XDG-aware), SecretCodec
(AES-256-CBC, SHA-256 machine-derived key, random IV per encrypt, documented as
obfuscation-not-security, whole-block length guard, decode failures surface as
prompt-at-connect), ConnectionStore (StoredConnection DTO separate from
ConnectionInfo, SavePassword flag, injectable IO + codec, atomic sibling-tmp
writes, corrupt/missing loads empty, case-insensitive property names,
Resolve/Pack helpers for Phase 4), JsonHostKeyPersistence storing MakeStoreKey
format verbatim with an Attach helper. Suite 316 → 369 green. Deferred note:
JsonHostKeyPersistence read-modify-write runs under the HostKeyStore lock —
acceptable at pin scale, watch on slow mounts in Phase 4.

## Phase 4: Connect dialog + popover shares + lifecycle UX
Status: Complete

- [x] Replace `ConnectStubWindow` with a real **Connect** dialog (Duetto styling):
  fields — Name, Protocol (SFTP, only option enabled), Host, Port (22), Username,
  Auth radio (Password | Key file + browse + passphrase), initial Remote path,
  "Save password" checkbox. Validate + Test/Connect + Cancel.
- [x] `ViewModels/ConnectDialogViewModel.cs` — builds a `ConnectionInfo`, invokes
  `ConnectionManager.Connect`, surfaces connect errors (auth failure, timeout,
  `HostKeyChangedException` → explicit accept-new-key confirmation), saves via
  `ConnectionStore` on success.
- [x] `DrivePopoverViewModel` — populate the **CONNECTED SHARES** section from saved
  connections (name + host); clicking connects (prompting for an unsaved secret) and
  `pane.NavigateTo("sftp://<id>/<remotePath>")`; a **Disconnect** row for the current
  remote (parallels Eject); an edit/remove affordance for saved connections.
- [x] Remote pane presentation: volume chip shows the connection name; `PathTailText`
  uses `PathUtil` for the remote tail; capacity bar hidden when
  `!ReportsCapacity`. GNOME Places rail **Remote** section lists connections.
- [x] Wire `ConnectionManager` + `FileSystemRegistry` + `ConnectionStore` into app
  composition (`App`/`Program`/`MainViewModel`).

### Verification Plan
- Headless UI (fake provider registered under a test scheme): Connect dialog opens
  from the popover; submitting a valid connection adds it to CONNECTED SHARES;
  clicking a share navigates the pane to its remote root and lists fake entries;
  Disconnect returns the pane home and drops the share; capacity bar hidden for a
  `ReportsCapacity=false` provider.
- `dotnet test` full suite green.

### Phase Summary
Done 2026-07-29, commits 8d5deb8..64665b0 (Tasks I+J with review fix rounds).
ConnectStubWindow replaced by a real ConnectWindow + ConnectDialogViewModel:
validation, background-thread connect, specific error surfacing, the
HostKeyChangedException accept-new-key flow (Forget(StoreKey) + single retry),
saves via ConnectionStore honoring SavePassword. App composition unified: ONE
FileSystemRegistry/ConnectionManager/ConnectionStore/HostKeyStore owned by
MainViewModel, shared by both panes and search; JsonHostKeyPersistence attached
in production; manager disposed with the app. Popover gained the CONNECTED
SHARES section (status dots, click = navigate / background-connect / prompt via
dialog, edit + remove affordances, Disconnect row paralleling Eject); remove
disconnects live connections and navigates affected panes home; failed
background connects reopen the dialog prefilled instead of failing silently.
Remote chip shows the connection name, PathTailText shows the provider-local
path, GNOME Places rail lists saved connections. Suite 369 → 408 green; final
phase review fix wave (0b35ab9) broadened the connect-error catch space
(SshException/IOException/InvalidOperationException fallbacks at all four
sites, timeout pinned by test), wired MainWindow.OnClosed → MainViewModel
dispose, and pinned the (incidental, now tested) no-capacity behavior for
remote paths — explicit ReportsCapacity gating moves to Phase 5's
capability-gating item. Suite at phase close: 410.
Deferred (recorded): DisconnectRowVisible staleness between popover opens,
StatusTextColor hardcode, ConnectionNameFor per-evaluation store read,
visual-tree IsVisible asserts, PaneView Connected-subscription pattern
(drive nav off ShowDialog return), File.Exists on UI thread in dialog Validate.

## Phase 5: End-to-end remote ops (copy/move, delete, rename, mkdir, search)
Status: Complete

- [x] Copy/move (F5/F6) across providers through the reworked `TransferEngine`,
  shown in the existing two-tone progress strip: local→remote (upload),
  remote→local (download), remote→remote. Move = native rename within one remote,
  else copy+delete. Conflict/skip-newer + pause/cancel preserved.
- [x] New folder / file (Phase-1 placeholder flow) works on a remote pane via the
  provider; disabled when `!CanCreateEmptyDir` / `!CanCreateFile`.
- [x] Rename (F2) on a remote via `provider.Rename`; disabled when `!CanRename`.
- [x] Delete (F8/Del) on a remote → permanent recursive delete (no prompt); status
  reflects "deleted" vs local "moved to Trash" based on `HasTrash`.
- [x] Ctrl+F search over a remote pane via `EnumerateRecursive` + capped content
  read; search disabled when `!SupportsSearch`.
- [x] Capability-gate the command bar / key handlers off the active pane's provider
  capabilities (rename, new, delete, eject/disconnect, capacity, search).
- [x] (From Phase 2 review) Probe the server's advertised extensions at connect
  and gate `AtomicRename` per connection (fallback: delete+rename inside
  `ReplaceFile`) — today the capability hard-commits every upload to
  `posix-rename@openssh.com`, which some servers lack.
- [x] (From Phase 2 review) Materialize each directory's children (`.ToList()`)
  before deleting inside `SftpFileSystemProvider.DeleteRecursive` — lazy paged
  `READDIR` while deleting is unspecified server behavior; consider per-node
  `Exec` granularity so a reconnect mid-delete does not retry from the top.
- [x] (From Phase 2 review) Re-prefix provider-local search-hit paths with
  `sftp://<id>` before they reach reveal/delete-from-search (`MainViewModel`
  resolves `entry.FullPath` through the registry — a bare `/docs/x` would
  resolve to the LOCAL provider).
- [x] (From Phase 4 review) Extract the duplicated share-connect flow
  (PaneView.ActivateShare / MainWindow.OnRemotePlaceClicked) into a testable
  `MainViewModel` seam — FIRST Phase 5 task, before capability gating touches
  those paths.
- [x] (From Phase 4 review) Surface provider/registry failures from in-flight
  listings on disconnect/drop (extend PaneViewModel load catch with
  SshException/InvalidOperationException; reset IsLoading).
- [x] (From Phase 4 review, re-deferred from Phase 1) `SearchViewModel.ScopeDirName`
  still raw `Path.GetFileName` — remote root scope displays the connection id.
- [x] (From Phase 2 review) Surface `HostKeyChangedException` thrown from a
  mid-operation reconnect (deep in a transfer/search, not just Connect) —
  Phase 4's dialog-only handling will not see it.

### Verification Plan
- Headless/VM tests over the fake provider: upload (local→fake) and download
  (fake→local) copy bytes + land the row; move within the fake uses rename; move
  fake→local is copy+delete; delete on a `HasTrash=false` provider removes
  permanently; rename/mkdir round-trip; recursive search returns fake matches; a
  provider with a capability off disables the matching command.
- `dotnet test Duetto.slnx` full suite green; `dotnet build` clean.

### Phase Summary
Done 2026-07-29, commits 48867b7..c4186ca (Tasks K/L/M with review fix rounds;
Tasks E/F/G/L/M each survived a subagent death or session-limit interruption —
all recovered by takeover/reconcile and every commit controller-verified). K
extracted the duplicated share-connect flow onto a testable MainViewModel seam
(one 8-type catch set, seam captured before scheduling to avoid a two-pane
race), extended the pane load catch (SshException/InvalidOperationException,
IsLoading always resets), and routed ScopeDirName through PathUtil.Leaf. L wired
end-to-end remote ops: transfers resolve both sides through the registry to
TransferEngine's provider overload (upload/download/remote-remote, native move
when same provider, local-local byte-for-byte unchanged); new/rename/delete on
remote panes via provider-aware FileOps + provider.Delete; delete status keyed
on HasTrash; the posix-rename capability gated per connection by an empirical
first-failure probe (SSH.NET exposes no extension discovery); DeleteRecursive
materializes children before deleting. L's own first commit shipped a
data-loss-class bug (remote delete resolved provider-local paths against the
LOCAL provider, masked by a TrashFn-overriding test) — caught by the implementer
via revert-and-watch-fail and fixed structurally; the review then audited all
eight Resolve sites clean. M wired remote search (EnumerateRecursive gated on
SupportsSearch, disabled search box + watermark), capability-gated every op
entry point at the method level (key handlers no-op, not just disabled buttons),
re-prefixed search-hit paths to full sftp://id addresses at reveal AND
delete-from-search (same data-loss class as L, guard tests fail if reverted),
surfaced mid-operation HostKeyChangedException in the transfer and search
workers (fail with status, disconnected, no retry), and showed the full address
in the transfer strip. Suite 424 → 451 green; clean --no-incremental build
confirmed (an incremental build had masked a dangling XAML binding the takeover
resolved). Deferred to Phase 6/backlog: command-bar buttons not visually
disabled on no-capability providers (method-level no-op is safe; affordance
polish only); FileOps.Exists PathUtil.Combine on provider-local paths
(pre-existing, backstopped by server-side collision throw); "Deleted 0 items"
wording when all items are capability-skipped.

## Phase 6: Cross-platform build, docs, backlog
Status: Not started

- [ ] Publish for win-x64 / linux-x64 / osx (existing `scripts`) — confirm SSH.NET
  ships in the output and the app launches on each target (or note the manual check,
  consistent with the existing cross-compile caveat).
- [ ] Update `plans/duetto-file-manager.md` architecture notes + mark the backlog
  "Real Connect backend" item, noting SFTP done and S3/SMB as follow-ups on the
  provider seam.
- [ ] Short `docs`/README note: how to add an SFTP connection, where config lives,
  and the explicit "secrets are obfuscated, not encrypted" caveat.

### Verification Plan
- `bash scripts/<publish script>` for each RID → exit 0, binary present.
- `grep` backlog + design doc show the updated status lines.

### Phase Summary
_(write when phase completes)_

## Final Recap
_(write when all phases complete)_

## Deployment Plan
_(write when all phases complete)_

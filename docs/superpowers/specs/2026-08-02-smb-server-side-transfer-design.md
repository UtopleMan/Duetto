# Server-side transfer for same-host remote panes

Date: 2026-08-02
Status: Approved — Phase 1 (move rename) + Phase 2 (copy offload) both in scope.
Wiring: keep NuGet `SMBLibrary 1.5.7.1`; copychunk implemented in duetto's adapter via the
library's public `DeviceIOControl`. `vendor/SMBLibrary` @ `v1.5.7` is reference only, not built.

## Problem

When both panes address the same remote host (e.g. `node108` over SMB), copy and move
operations still stream every byte down to the client (the Mac) and back up to the server.
The bytes make two network trips even though source and destination live on the same server.

Root cause, in `TransferEngine.Run` (`src/Duetto.Core/Operations/TransferEngine.cs:314`):

```csharp
if (mode == TransferMode.Move && ReferenceEquals(srcProvider, destProvider)
    && srcProvider.Capabilities.CanRename
    && !srcProvider.FileExists(dest) && !srcProvider.DirectoryExists(dest))
```

The move shortcut requires the **same provider instance**. Each connection produces its own
`SmbFileSystemProvider` (`SmbConnectionManager.cs:107`), so two panes connected to the same
host are two distinct instances. `ReferenceEquals` is false, the shortcut is skipped, and the
move degrades to copy + delete — streaming the data through the client. Copy never had a
server-side path at all.

## Goals

- A **move** between two panes on the same SMB host **and** share executes as a single
  server-side rename, with zero file bytes crossing the client.
- No regression to existing same-instance behavior, or to cross-host / cross-backend transfers.
- Safe by construction: never rename to the wrong file; fall back to the current streaming
  path whenever a server-side rename is not provably valid or fails at runtime.

## Non-goals

- Cross-instance server-side move/copy for **SFTP** — deliberately excluded (see Safety).
- Connection de-duplication / provider sharing — rejected in favor of the backend-identity
  approach below.
- Any change to `vendor/SMBLibrary` — the public `DeviceIOControl` + `IoControlCode` enum are
  sufficient; the submodule is reference only.
- Cross-share (same server, different share) copychunk — the first cut keys on same host **and**
  share, matching move. A later extension can widen the copy domain.

## Design — Phase 1

### Backend identity

A new optional interface lets a provider name the server-side "rename domain" a path lives in.

```csharp
namespace Duetto.Core.FileSystem;

// Optional capability: identify the server-side rename domain that contains `path`.
// Two providers that return equal, NON-NULL keys for their respective paths address the same
// backend location, where a native rename between those two paths is valid and stays entirely
// server-side. The transfer engine can then issue a Move via the source provider instead of
// streaming copy + delete through the client.
public interface IBackendIdentity
{
    // Null when `path` has no server-side rename domain (e.g. the SMB share-list root) or when
    // the provider opts out of cross-instance server-side moves.
    string? BackendKey(string path);
}
```

Keys are scheme-prefixed and lowercased so unrelated backends can never collide.

| Provider | `BackendKey(path)` | Rationale |
|----------|--------------------|-----------|
| SMB | `smb://{host}/{share}` where `share` = first path segment; `null` for root `/` | SMB rename (`FileRenameInformation`) is valid only within a single tree connect, i.e. same host **and** same share. `share` derives from the provider-local path `/share/dir/file`. |
| SFTP | *not implemented* (effectively `null`) | Two SFTP sessions can expose divergent namespaces (chroot, per-user home, symlinks); the same path string may resolve to different files across sessions. Only same-session moves stay safe, and those are already covered by `ReferenceEquals`. |
| Local / in-memory / S3 | *not implemented* | Local same-instance moves are already covered by `ReferenceEquals`; no cross-instance case exists in the UI. |

Host comparison is by string equality after lowercasing. Aliases that differ textually
(`node108` vs `node108.local` vs an IP) yield different keys and simply fall back to streaming —
safe, just unoptimized.

### Engine change

In `TransferEngine.Run`, generalize the move gate:

```csharp
static bool SameRenameDomain(
    IFileSystemProvider src, string source, IFileSystemProvider dest, string destPath)
    => ReferenceEquals(src, dest)
       || (src is IBackendIdentity a && dest is IBackendIdentity b
           && a.BackendKey(source) is { } key && key == b.BackendKey(destPath));
```

```csharp
if (mode == TransferMode.Move
    && srcProvider.Capabilities.CanRename
    && SameRenameDomain(srcProvider, source, destProvider, dest)
    && !destProvider.FileExists(dest) && !destProvider.DirectoryExists(dest))
{
    if (TryServerSideMove(srcProvider, source, dest, session, size))
        continue;
    // else fall through to stream copy + delete
}
```

- `TryServerSideMove` calls `srcProvider.Move(source, dest)` and reports progress/done. It
  returns `true` on success.
- On a non-fatal `IOException` (permission denied, replace refused — but **not**
  `SmbConnectionException` / `SmbAuthenticationException`, which stay fatal so `WithReconnect`
  and the session fault path keep working), it returns `false` and the code falls through to
  the existing `CopyFile` + delete-source path. The move still completes, just by streaming.
- The `!FileExists && !DirectoryExists` guard now runs against `destProvider` (previously
  `srcProvider`, valid only because they were the same instance).

The existing same-instance branch keeps its exact success behavior; only the failure path gains
the streaming fallback, which is strictly more robust.

### Supporting changes

- `SmbConnection`: expose the host — `public string Host => info.Host;`.
- `SmbFileSystemProvider`: implement `IBackendIdentity`. Extract the first path segment as the
  share; return `null` for root; build `smb://{host}/{share}` lowercased.

## Safety analysis

- **Correctness of target.** The move is issued on the *source* provider's session only. For
  SMB, `/share/...` is a server-global namespace: the same path string denotes the same file for
  any session on that host, so renaming source→dest via the source session hits the intended
  files. SFTP lacks this guarantee (chroot/relative namespaces), which is why it opts out.
- **Permissions.** If the source session cannot write the destination (e.g. the dest pane
  authenticated as a different, more-privileged user), the rename throws a non-fatal
  `IOException` and we fall back to streaming copy + delete, which writes with the destination
  provider's own credentials. Result is correct either way.
- **Overwrite.** Guarded by the pre-existing `!FileExists && !DirectoryExists` destination
  check plus the newer-file skip earlier in the loop; SMB rename is issued with
  `replaceExisting: false`.
- **Cross-share / cross-host.** Different share or host → different (or null) keys → no
  shortcut → existing streaming path. No behavior change.

## Testing

- Unit: `SmbFileSystemProvider.BackendKey` — `/share/a/b` → `smb://host/share`; `/share` →
  `smb://host/share`; `/` → `null`; case-insensitive host/share normalization.
- Engine (fake providers implementing `IBackendIdentity`): move between two distinct instances
  with equal keys issues a single `Move` and no `OpenRead`/`OpenWrite`; different keys stream;
  a `Move` that throws non-fatal `IOException` falls back to copy + delete; a
  `SmbConnectionException` stays fatal.
- Regression: same-instance move (`ReferenceEquals`) unchanged; cross-host move still streams;
  SFTP cross-instance move still streams (no `IBackendIdentity`).

Phase 2:

- Unit (pure bytes, the error-prone core): `SmbCopyChunk.BuildCopyChunkRequest` produces the exact
  MS-SMB2 layout — SourceKey at 0..24, `ChunkCount` LE at 24, first chunk's SourceOffset/
  TargetOffset/Length at the right offsets; multi-chunk arrays pack at 24-byte stride.
  `ParseResumeKey` returns the leading 24 bytes; `ParseCopyChunkResponse` decodes the 12-byte
  triple. Round-trip a known vector.
- Engine (fake `IServerSideCopy` provider): same-backend copy calls `TryServerSideCopy` and never
  calls `OpenRead`/`OpenWrite`; `TryServerSideCopy` returning `false` falls back to the streaming
  loop and still completes; different-backend copy streams (offload never attempted); progress
  deltas reported from the offload sum to the file size; cancel between copychunk calls aborts.
- Integration (opt-in, real Samba fixture from `docker-compose`): a same-host copy of a multi-MiB
  file completes and matches the source bytes; skipped when the fixture/server lacks copychunk.

## Design — Phase 2: server-side copy offload (SMB copychunk)

Goal: a **copy** between two paths on the same SMB host + share stays server-side via SMB2
`FSCTL_SRV_REQUEST_RESUME_KEY` + `FSCTL_SRV_COPYCHUNK`, instead of streaming through the client.

### Feasibility (confirmed)

Against `vendor/SMBLibrary` @ `v1.5.7` (matches NuGet `1.5.7.1`), all public:

- `INTFileStore.DeviceIOControl(object handle, uint ctlCode, byte[] input, out byte[] output,
  int maxOutputLength)` (`SMBLibrary/NTFileStore/INTFileStore.cs:62`), implemented by
  `SMB2FileStore` (`SMBLibrary/Client/SMB2FileStore.cs:315`).
- `IoControlCode`: `FSCTL_SRV_REQUEST_RESUME_KEY = 0x00140078`, `FSCTL_SRV_COPYCHUNK = 0x001440F2`
  (`SMBLibrary/NTFileStore/Enums/IoControlCode.cs:45,51`).

`RealSmbClientAdapter` already holds an `ISMBFileStore store` (per share, cached in `trees`) and
the `object handle` returned by `store.CreateFile(...)`, and already calls `store.ReadFile`,
`store.SetFileInformation`, etc. `DeviceIOControl` is reachable the same way — no SMBLibrary edit.

### Wire protocol (hand-marshalled)

A dedicated, unit-testable static helper owns the byte layout (MS-SMB2 2.2.31/2.2.32, MS-FSCC):

```
SmbCopyChunk.ParseResumeKey(byte[] fsctlOutput) -> byte[24]        // first 24 bytes of response

// SRV_COPYCHUNK_COPY request:
//   SourceKey (24) | ChunkCount (u32 LE) | Reserved (u32) | Chunks[]
// each SRV_COPYCHUNK (24): SourceOffset (u64 LE) | TargetOffset (u64 LE) | Length (u32 LE) | Reserved (u32)
SmbCopyChunk.BuildCopyChunkRequest(byte[24] sourceKey, IReadOnlyList<Chunk> chunks) -> byte[]

// SRV_COPYCHUNK_RESPONSE (12): ChunksWritten (u32) | ChunkBytesWritten (u32) | TotalBytesWritten (u32)
SmbCopyChunk.ParseCopyChunkResponse(byte[] fsctlOutput) -> (uint ChunksWritten, uint ChunkBytesWritten, uint TotalBytesWritten)
```

### Server limits and the loop

Copychunk is bounded per call (MS-SMB2 defaults: ≤ 16 chunks, ≤ 1 MiB/chunk, ≤ 16 MiB total).
A request over a limit is rejected with `STATUS_INVALID_PARAMETER` and a `SRV_COPYCHUNK_RESPONSE`
carrying the server's actual maxima (`ChunksWritten`=MaxChunks, `ChunkBytesWritten`=MaxChunkSize,
`TotalBytesWritten`=MaxDataSize). The adapter:

1. Starts with conservative defaults (1 chunk of ≤ 1 MiB per call is the simple, safe cut; may
   batch up to the negotiated maxima later).
2. On `STATUS_INVALID_PARAMETER` with a limits response, re-reads the maxima and retries.
3. Walks the source length in `MaxChunkSize` slices, batching up to `MaxChunks` per
   `DeviceIOControl`, until EOF; reports incremental bytes after each call.

Zero-length file: skip copychunk, just create/truncate the destination.

### Adapter surface

Add to `ISmbClientAdapter`:

```csharp
// Copies source -> dest entirely on the server (SMB copychunk), reporting bytes copied.
// Both provider-local paths MUST resolve within one tree connect (same share); the caller
// guarantees this. Returns false if the server rejects copychunk as unsupported
// (STATUS_NOT_SUPPORTED / STATUS_INVALID_DEVICE_REQUEST) so the caller falls back to streaming.
bool ServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token);
```

`RealSmbClientAdapter.ServerSideCopy`: resolve both paths' share (must match), `Tree(share)`
once; open source handle (`GENERIC_READ`), `DeviceIOControl(FSCTL_SRV_REQUEST_RESUME_KEY)`, open
dest handle (`GENERIC_READ | GENERIC_WRITE`, `FILE_OVERWRITE_IF`), loop copychunk, close both.
Fatal drops surface as `SmbConnectionException` (via the existing `Run` wrapper) so reconnect
still works; a `false` return means "unsupported, stream instead".

### Provider + engine integration

- `SmbFileSystemProvider` implements a new optional interface and forwards to the adapter:

  ```csharp
  public interface IServerSideCopy
  {
      // dest is provider-local and guaranteed by the caller to share this provider's copy domain
      // (same host+share, via IBackendIdentity). Returns false when the server lacks copychunk.
      bool TryServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token);
  }
  ```

- `TransferEngine.CopyFile` tries the offload first when the source provider supports it and the
  destination is in the same backend domain (reusing the Phase 1 `SameRenameDomain` check on the
  `writePath`), then finishes with the existing `ReplaceFile` (`.part`) and `SetLastWriteTimeUtc`:

  ```csharp
  if (srcProvider is IServerSideCopy ssc
      && SameRenameDomain(srcProvider, source, destProvider, writePath)
      && TryOffload(ssc, source, writePath, session, token))
  {
      // offloaded: bytes never touched the client
  }
  else
  {
      // existing OpenRead/OpenWrite streaming loop
  }
  ```

  `TryOffload` maps the adapter's cumulative byte callback onto `session.FileProgress` (deltas),
  honors pause/cancel between copychunk calls, and returns `false` on `ServerSideCopy` returning
  `false` — falling through to streaming with no partial file left behind (the `.part` target is
  overwritten/cleaned by the existing `finally`).

Because the copy targets the engine-chosen `writePath` (the `.part` file on `AtomicRename`
backends), the atomic-finish and mtime steps are unchanged; the offload only replaces the
byte-moving inner loop.

### Data path after Phase 2

Same host + share **copy**: `REQUEST_RESUME_KEY` + `COPYCHUNK` FSCTLs cross the client (a few
hundred bytes); the file payload is copied by the server, node108 → node108, zero payload bytes
through the Mac.

## Risks

- SMB servers vary in rename semantics across shares; mitigated by keying on share and the
  runtime fallback.
- Host-string aliasing misses some same-host cases (unoptimized, not incorrect).
- Adding `IBackendIdentity` checks in the hot loop is O(1) per file; negligible.
</content>

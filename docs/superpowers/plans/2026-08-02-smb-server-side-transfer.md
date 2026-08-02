# SMB Server-Side Transfer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make same-host SMB move and copy between two panes run server-side (zero file bytes through the client), falling back to streaming whenever a server-side path is not provably valid.

**Architecture:** Add a backend-identity token (`IBackendIdentity`) so `TransferEngine` recognizes two distinct provider instances that address the same SMB host + share. Move uses a native rename via the source provider (Phase 1). Copy uses SMB2 copychunk (`FSCTL_SRV_REQUEST_RESUME_KEY` + `FSCTL_SRV_COPYCHUNK`) issued through the existing `ISMBFileStore.DeviceIOControl`, with byte marshalling in a standalone, unit-tested helper (Phase 2). Every server-side path falls back to the existing streaming loop on any non-fatal failure.

**Tech Stack:** C# / .NET 10, xunit, `SMBLibrary 1.5.7.1` (NuGet, unchanged), SSH.NET.

Design reference: `docs/superpowers/specs/2026-08-02-smb-server-side-transfer-design.md`.

## Global Constraints

- Target framework `net10.0`; solution `Duetto.slnx`; tests in `tests/Duetto.Tests` (xunit).
- Do **not** modify `vendor/SMBLibrary`; keep `PackageReference SMBLibrary 1.5.7.1`. The submodule is reference only.
- Commit messages: Conventional Commits. **No** `Co-Authored-By` trailer; **no** mention of Claude/Anthropic/AI anywhere.
- SFTP and other providers do **not** implement `IBackendIdentity`/`IServerSideCopy` — they keep the existing `ReferenceEquals` same-instance behavior only.
- Cross-instance server-side paths are restricted to **same host + same share** (one SMB tree connect).
- Build: `dotnet build Duetto.slnx`. Test: `dotnet test Duetto.slnx`. Single test: `dotnet test Duetto.slnx --filter "FullyQualifiedName~<name>"`.

---

## File Structure

- Create `src/Duetto.Core/FileSystem/IBackendIdentity.cs` — backend-identity token interface.
- Create `src/Duetto.Core/FileSystem/IServerSideCopy.cs` — optional server-side copy interface.
- Create `src/Duetto.Core/Remote/SmbCopyChunk.cs` — copychunk byte marshalling (no SMB client types).
- Modify `src/Duetto.Core/Remote/SmbConnection.cs` — expose `Host`.
- Modify `src/Duetto.Core/Remote/SmbFileSystemProvider.cs` — implement `IBackendIdentity` + `IServerSideCopy`.
- Modify `src/Duetto.Core/Remote/SmbConnection.cs` interface `ISmbClientAdapter` + `src/Duetto.Core/Remote/RealSmbClientAdapter.cs` — add `ServerSideCopy`.
- Modify `src/Duetto.Core/Operations/TransferEngine.cs` — `SameRenameDomain` helper; generalized move shortcut; copy offload hook; both with streaming fallback.
- Test files: `tests/Duetto.Tests/Core/Remote/SmbBackendIdentityTests.cs`, `tests/Duetto.Tests/Core/Remote/SmbCopyChunkTests.cs`, `tests/Duetto.Tests/Core/Remote/SmbServerSideCopyProviderTests.cs`, `tests/Duetto.Tests/Core/ServerSideTransferEngineTests.cs`. Modify `tests/Duetto.Tests/Core/Remote/FakeSmbClientAdapter.cs`.

---

## Task 1: Backend identity token (Phase 1 detection)

**Files:**
- Create: `src/Duetto.Core/FileSystem/IBackendIdentity.cs`
- Modify: `src/Duetto.Core/Remote/SmbConnection.cs` (add `Host`)
- Modify: `src/Duetto.Core/Remote/SmbFileSystemProvider.cs` (implement interface)
- Test: `tests/Duetto.Tests/Core/Remote/SmbBackendIdentityTests.cs`

**Interfaces:**
- Produces: `IBackendIdentity.BackendKey(string path) -> string?`; `SmbConnection.Host -> string`; `SmbFileSystemProvider : IBackendIdentity`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Duetto.Tests/Core/Remote/SmbBackendIdentityTests.cs
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;
using Duetto.Tests.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public class SmbBackendIdentityTests
{
    private static SmbFileSystemProvider Provider(string host)
    {
        var info = new SmbConnectionInfo(Id: host, Name: host, Host: host);
        var conn = new SmbConnection(info, new ConnectSecret(""), new FakeSmbFactory(new FakeSmbClientAdapter()));
        conn.Connect();
        return new SmbFileSystemProvider(conn);
    }

    [Fact]
    public void BackendKey_is_host_and_share_lowercased()
    {
        var p = (IBackendIdentity)Provider("Node108");
        Assert.Equal("smb://node108/data", p.BackendKey("/Data/dir/file.txt"));
        Assert.Equal("smb://node108/data", p.BackendKey("/Data"));
    }

    [Fact]
    public void BackendKey_is_null_for_share_root()
    {
        var p = (IBackendIdentity)Provider("node108");
        Assert.Null(p.BackendKey("/"));
        Assert.Null(p.BackendKey(""));
    }

    [Fact]
    public void Same_host_and_share_two_instances_have_equal_keys()
    {
        var a = (IBackendIdentity)Provider("node108");
        var b = (IBackendIdentity)Provider("node108");
        Assert.Equal(a.BackendKey("/data/x"), b.BackendKey("/data/y"));
        Assert.NotNull(a.BackendKey("/data/x"));
    }

    [Fact]
    public void Different_share_or_host_have_different_keys()
    {
        var a = (IBackendIdentity)Provider("node108");
        var b = (IBackendIdentity)Provider("node109");
        Assert.NotEqual(a.BackendKey("/data/x"), a.BackendKey("/other/x"));
        Assert.NotEqual(a.BackendKey("/data/x"), b.BackendKey("/data/x"));
    }
}
```

Note: confirm the `ConnectSecret` constructor shape (password-only) against `src/Duetto.Core/Remote/ConnectSecret.cs`; adjust the `new ConnectSecret("")` call if the ctor differs.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~SmbBackendIdentityTests"`
Expected: FAIL to compile — `IBackendIdentity` and `SmbFileSystemProvider` cast do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Duetto.Core/FileSystem/IBackendIdentity.cs
namespace Duetto.Core.FileSystem;

// Optional capability: identify the server-side rename/copy domain that contains `path`.
// Two providers that return equal, NON-NULL keys for their respective paths address the same
// backend location, where a native rename (and, for IServerSideCopy providers, a server-side
// copy) between those two paths is valid and stays entirely server-side. Null means the path
// has no such domain (e.g. an SMB share-list root) or the provider opts out.
public interface IBackendIdentity
{
    string? BackendKey(string path);
}
```

```csharp
// src/Duetto.Core/Remote/SmbConnection.cs — add inside SmbConnection, near IsConnected:
public string Host => info.Host;
```

```csharp
// src/Duetto.Core/Remote/SmbFileSystemProvider.cs
// 1) add IBackendIdentity to the class declaration:
//    public sealed class SmbFileSystemProvider : IFileSystemProvider, IBackendIdentity, IDisposable
// 2) add the method:
public string? BackendKey(string path)
{
    if (IsRoot(path))
        return null;
    var trimmed = path.TrimStart('/');
    var slash = trimmed.IndexOf('/');
    var share = slash < 0 ? trimmed : trimmed[..slash];
    if (share.Length == 0)
        return null;
    return $"smb://{conn.Host.ToLowerInvariant()}/{share.ToLowerInvariant()}";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~SmbBackendIdentityTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Duetto.Core/FileSystem/IBackendIdentity.cs src/Duetto.Core/Remote/SmbConnection.cs src/Duetto.Core/Remote/SmbFileSystemProvider.cs tests/Duetto.Tests/Core/Remote/SmbBackendIdentityTests.cs
git commit -m "feat(smb): backend-identity key for same host+share detection"
```

---

## Task 2: Generalized move shortcut with fallback (Phase 1 complete)

**Files:**
- Modify: `src/Duetto.Core/Operations/TransferEngine.cs` (`Run` loop ~lines 296-329)
- Test: `tests/Duetto.Tests/Core/ServerSideTransferEngineTests.cs`

**Interfaces:**
- Consumes: `IBackendIdentity.BackendKey` (Task 1).
- Produces: `TransferEngine` static helper `SameRenameDomain(IFileSystemProvider, string, IFileSystemProvider, string) -> bool` (used again in Task 6).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Duetto.Tests/Core/ServerSideTransferEngineTests.cs
using Duetto.Core.FileSystem;
using Duetto.Core.Operations;
using Duetto.Tests.Support;

namespace Duetto.Tests.Core;

public class ServerSideTransferEngineTests
{
    // A provider over a shared in-memory store, tagged with a backend key, that records whether
    // the engine used native Move vs stream, and whether server-side copy was attempted.
    private sealed class BackendProvider(InMemoryFileSystemProvider store, string backendKey)
        : IFileSystemProvider, IBackendIdentity
    {
        public bool MoveCalled;
        public bool OpenReadCalled;
        public bool OpenWriteCalled;
        public bool FailNextMove;

        public string? BackendKey(string path) => path is "" or "/" ? null : backendKey;
        public FileSystemCapabilities Capabilities => store.Capabilities;
        public IReadOnlyList<FileEntry> List(string p) => store.List(p);
        public bool DirectoryExists(string p) => store.DirectoryExists(p);
        public bool FileExists(string p) => store.FileExists(p);
        public FileEntry? Stat(string p) => store.Stat(p);
        public string CreateDirectory(string parent, string name) => store.CreateDirectory(parent, name);
        public string CreateFile(string parent, string name) => store.CreateFile(parent, name);
        public string Rename(string p, string n) => store.Rename(p, n);
        public void ReplaceFile(string f, string t) => store.ReplaceFile(f, t);
        public void Delete(string p, bool trash) => store.Delete(p, trash);
        public void SetLastWriteTimeUtc(string p, DateTime u) => store.SetLastWriteTimeUtc(p, u);
        public IEnumerable<FileEntry> EnumerateRecursive(string p) => store.EnumerateRecursive(p);
        public VolumeInfo? VolumeFor(string p) => null;

        public void Move(string from, string to)
        {
            if (FailNextMove) { FailNextMove = false; throw new IOException("rename refused"); }
            MoveCalled = true; store.Move(from, to);
        }
        public Stream OpenRead(string p) { OpenReadCalled = true; return store.OpenRead(p); }
        public Stream OpenWrite(string p) { OpenWriteCalled = true; return store.OpenWrite(p); }
    }

    // Two providers share one InMemoryFileSystemProvider instance = same backend store.
    private static (BackendProvider, BackendProvider, InMemoryFileSystemProvider) SameBackendPair(string key = "smb://h/s")
    {
        var store = new InMemoryFileSystemProvider();
        store.CreateDirectory("/", "src");
        store.CreateDirectory("/", "dst");
        return (new BackendProvider(store, key), new BackendProvider(store, key), store);
    }

    private static void Seed(IFileSystemProvider fs, string dir, string name, string text)
    {
        var full = fs.CreateFile(dir, name);
        using var w = fs.OpenWrite(full);
        w.Write(System.Text.Encoding.UTF8.GetBytes(text));
    }

    private static string ReadText(IFileSystemProvider fs, string path)
    {
        using var s = fs.OpenRead(path); using var ms = new MemoryStream();
        s.CopyTo(ms); return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    [Fact]
    public async Task Move_across_same_backend_instances_uses_native_Move_not_streaming()
    {
        var (src, dst, store) = SameBackendPair();
        Seed(src, "/src", "a.txt", "server move");

        var session = TransferEngine.Start(["/src/a.txt"], src, "/dst", dst, TransferMode.Move);
        await session.Completion;

        Assert.True(session.Snapshot().IsComplete);
        Assert.True(src.MoveCalled, "must use native Move across same-backend instances");
        Assert.False(src.OpenReadCalled, "must not stream through the client");
        Assert.False(src.FileExists("/src/a.txt"));
        Assert.Equal("server move", ReadText(store, "/dst/a.txt"));
    }

    [Fact]
    public async Task Move_falls_back_to_stream_when_native_Move_throws_nonfatal()
    {
        var (src, dst, store) = SameBackendPair();
        Seed(src, "/src", "a.txt", "fallback move");
        src.FailNextMove = true;

        var session = TransferEngine.Start(["/src/a.txt"], src, "/dst", dst, TransferMode.Move);
        await session.Completion;

        // Assert the OUTCOME, not the fallback mechanism: native Move threw yet the file still
        // moved. (After Task 6 the fallback prefers server-side copy over streaming; asserting
        // OpenReadCalled here would then be wrong.)
        Assert.True(session.Snapshot().IsComplete);
        Assert.False(src.MoveCalled, "native Move threw, never marked success");
        Assert.False(src.FileExists("/src/a.txt"), "source removed after fallback move");
        Assert.Equal("fallback move", ReadText(store, "/dst/a.txt"));
    }

    [Fact]
    public async Task Move_across_different_backends_streams()
    {
        var storeA = new InMemoryFileSystemProvider(); storeA.CreateDirectory("/", "src");
        var storeB = new InMemoryFileSystemProvider(); storeB.CreateDirectory("/", "dst");
        var src = new BackendProvider(storeA, "smb://h/s");
        var dst = new BackendProvider(storeB, "smb://other/s");
        Seed(src, "/src", "a.txt", "x");

        var session = TransferEngine.Start(["/src/a.txt"], src, "/dst", dst, TransferMode.Move);
        await session.Completion;

        Assert.False(src.MoveCalled);
        Assert.True(src.OpenReadCalled);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~ServerSideTransferEngineTests"`
Expected: FAIL — `Move_across_same_backend_instances...` streams (MoveCalled false) because the engine still gates on `ReferenceEquals`.

- [ ] **Step 3: Write minimal implementation**

In `TransferEngine.cs`, add the helper (near the bottom of the class):

```csharp
internal static bool SameRenameDomain(
    IFileSystemProvider src, string source, IFileSystemProvider dest, string destPath)
    => ReferenceEquals(src, dest)
       || (src is IBackendIdentity a && dest is IBackendIdentity b
           && a.BackendKey(source) is { } key && key == b.BackendKey(destPath));
```

Replace the move shortcut (currently `TransferEngine.cs:314-322`) with:

```csharp
if (mode == TransferMode.Move
    && srcProvider.Capabilities.CanRename
    && SameRenameDomain(srcProvider, source, destProvider, dest)
    && !destProvider.FileExists(dest) && !destProvider.DirectoryExists(dest))
{
    var moved = false;
    try
    {
        srcProvider.Move(source, dest);
        moved = true;
    }
    catch (IOException ex) when (ex is not HostKeyChangedException
                                and not SmbConnectionException and not SmbAuthenticationException)
    {
        // Native rename refused (permissions, replace) — fall through to streaming copy + delete.
    }

    if (moved)
    {
        session.FileProgress(source, size, size);
        session.FileDone(source);
        continue;
    }
}
```

Note: `SmbConnectionException`/`SmbAuthenticationException` are in `Duetto.Core.Remote` (already `using`-ed at `TransferEngine.cs:4`); `HostKeyChangedException` likewise. The destination-existence guard now runs against `destProvider` (previously `srcProvider`).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~ServerSideTransferEngineTests"`
Expected: PASS (3 tests). Also run the existing suite to catch regressions:
Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~CrossProviderTransferTests"`
Expected: PASS (unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/Duetto.Core/Operations/TransferEngine.cs tests/Duetto.Tests/Core/ServerSideTransferEngineTests.cs
git commit -m "feat(transfer): server-side move across same host+share panes"
```

---

## Task 3: Copychunk byte marshalling

**Files:**
- Create: `src/Duetto.Core/Remote/SmbCopyChunk.cs`
- Test: `tests/Duetto.Tests/Core/Remote/SmbCopyChunkTests.cs`

**Interfaces:**
- Produces: `SmbCopyChunk.FsctlRequestResumeKey`, `SmbCopyChunk.FsctlSrvCopyChunk` (uint consts); `SmbCopyChunk.ResumeKeyLength` (=24); `readonly record struct Chunk(long SourceOffset, long TargetOffset, int Length)`; `readonly record struct CopyChunkResult(uint ChunksWritten, uint ChunkBytesWritten, uint TotalBytesWritten)`; `ParseResumeKey(byte[]) -> byte[]`; `BuildCopyChunkRequest(byte[], IReadOnlyList<Chunk>) -> byte[]`; `ParseCopyChunkResponse(byte[]) -> CopyChunkResult`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Duetto.Tests/Core/Remote/SmbCopyChunkTests.cs
using System.Buffers.Binary;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public class SmbCopyChunkTests
{
    [Fact]
    public void Fsctl_codes_match_ms_smb2()
    {
        Assert.Equal(0x00140078u, SmbCopyChunk.FsctlRequestResumeKey);
        Assert.Equal(0x001440F2u, SmbCopyChunk.FsctlSrvCopyChunk);
    }

    [Fact]
    public void ParseResumeKey_takes_leading_24_bytes()
    {
        var response = new byte[32];
        for (var i = 0; i < 24; i++) response[i] = (byte)(i + 1);
        var key = SmbCopyChunk.ParseResumeKey(response);
        Assert.Equal(24, key.Length);
        Assert.Equal(1, key[0]);
        Assert.Equal(24, key[23]);
    }

    [Fact]
    public void BuildCopyChunkRequest_lays_out_header_and_chunks()
    {
        var key = new byte[24];
        for (var i = 0; i < 24; i++) key[i] = 0xAB;
        var req = SmbCopyChunk.BuildCopyChunkRequest(key,
        [
            new SmbCopyChunk.Chunk(SourceOffset: 0, TargetOffset: 0, Length: 1048576),
            new SmbCopyChunk.Chunk(SourceOffset: 1048576, TargetOffset: 1048576, Length: 256),
        ]);

        Assert.Equal(32 + 24 * 2, req.Length);              // header 32 + 24/chunk
        Assert.Equal(0xAB, req[0]);                          // SourceKey
        var span = req.AsSpan();
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(span[24..]));   // ChunkCount
        Assert.Equal(0L, BinaryPrimitives.ReadInt64LittleEndian(span[32..]));    // chunk0 SourceOffset
        Assert.Equal(0L, BinaryPrimitives.ReadInt64LittleEndian(span[40..]));    // chunk0 TargetOffset
        Assert.Equal(1048576, BinaryPrimitives.ReadInt32LittleEndian(span[48..]));// chunk0 Length
        Assert.Equal(1048576L, BinaryPrimitives.ReadInt64LittleEndian(span[56..]));// chunk1 SourceOffset
        Assert.Equal(256, BinaryPrimitives.ReadInt32LittleEndian(span[72..]));   // chunk1 Length
    }

    [Fact]
    public void ParseCopyChunkResponse_decodes_triple()
    {
        var buf = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), 1048576);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8), 1048832);
        var r = SmbCopyChunk.ParseCopyChunkResponse(buf);
        Assert.Equal(1u, r.ChunksWritten);
        Assert.Equal(1048576u, r.ChunkBytesWritten);
        Assert.Equal(1048832u, r.TotalBytesWritten);
    }

    [Fact]
    public void ParseResumeKey_throws_when_too_short() =>
        Assert.Throws<IOException>(() => SmbCopyChunk.ParseResumeKey(new byte[10]));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~SmbCopyChunkTests"`
Expected: FAIL to compile — `SmbCopyChunk` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Duetto.Core/Remote/SmbCopyChunk.cs
using System.Buffers.Binary;

namespace Duetto.Core.Remote;

// Byte marshalling for SMB2 server-side copy (copychunk), MS-SMB2 2.2.31 / 2.2.32. Deliberately
// free of any SMBLibrary type so the layout can be unit-tested in isolation.
internal static class SmbCopyChunk
{
    public const uint FsctlRequestResumeKey = 0x00140078;
    public const uint FsctlSrvCopyChunk = 0x001440F2;
    public const int ResumeKeyLength = 24;

    public readonly record struct Chunk(long SourceOffset, long TargetOffset, int Length);
    public readonly record struct CopyChunkResult(uint ChunksWritten, uint ChunkBytesWritten, uint TotalBytesWritten);

    // SRV_REQUEST_RESUME_KEY Response: ResumeKey(24) | ContextLength(4) | Context(var).
    public static byte[] ParseResumeKey(byte[] fsctlOutput)
    {
        if (fsctlOutput is null || fsctlOutput.Length < ResumeKeyLength)
            throw new IOException("SRV_REQUEST_RESUME_KEY response too short.");
        return fsctlOutput[..ResumeKeyLength];
    }

    // SRV_COPYCHUNK_COPY: SourceKey(24) | ChunkCount(u32) | Reserved(u32) | Chunk[]
    //   where SRV_COPYCHUNK = SourceOffset(u64) | TargetOffset(u64) | Length(u32) | Reserved(u32).
    public static byte[] BuildCopyChunkRequest(byte[] sourceKey, IReadOnlyList<Chunk> chunks)
    {
        if (sourceKey is null || sourceKey.Length != ResumeKeyLength)
            throw new ArgumentException($"Source key must be {ResumeKeyLength} bytes.", nameof(sourceKey));

        var buffer = new byte[32 + 24 * chunks.Count];
        var span = buffer.AsSpan();
        sourceKey.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)chunks.Count);
        // Reserved [28..32) stays zero.
        var offset = 32;
        foreach (var c in chunks)
        {
            BinaryPrimitives.WriteInt64LittleEndian(span[offset..], c.SourceOffset);
            BinaryPrimitives.WriteInt64LittleEndian(span[(offset + 8)..], c.TargetOffset);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 16)..], c.Length);
            // Reserved [offset+20..offset+24) stays zero.
            offset += 24;
        }

        return buffer;
    }

    // SRV_COPYCHUNK_RESPONSE: ChunksWritten(u32) | ChunkBytesWritten(u32) | TotalBytesWritten(u32).
    public static CopyChunkResult ParseCopyChunkResponse(byte[] fsctlOutput)
    {
        if (fsctlOutput is null || fsctlOutput.Length < 12)
            throw new IOException("SRV_COPYCHUNK response too short.");
        var span = fsctlOutput.AsSpan();
        return new CopyChunkResult(
            BinaryPrimitives.ReadUInt32LittleEndian(span),
            BinaryPrimitives.ReadUInt32LittleEndian(span[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(span[8..]));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~SmbCopyChunkTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Duetto.Core/Remote/SmbCopyChunk.cs tests/Duetto.Tests/Core/Remote/SmbCopyChunkTests.cs
git commit -m "feat(smb): copychunk request/response byte marshalling"
```

---

## Task 4: Adapter `ServerSideCopy` (real copychunk + fake in-memory)

**Files:**
- Modify: `src/Duetto.Core/Remote/SmbConnection.cs` (add method to `ISmbClientAdapter`)
- Modify: `src/Duetto.Core/Remote/RealSmbClientAdapter.cs` (implement via copychunk)
- Modify: `tests/Duetto.Tests/Core/Remote/FakeSmbClientAdapter.cs` (implement in-memory)

**Interfaces:**
- Consumes: `SmbCopyChunk.*` (Task 3).
- Produces: `ISmbClientAdapter.ServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token) -> bool`.

**Coverage note:** `RealSmbClientAdapter.ServerSideCopy` calls the live SMB stack (`ISMBFileStore.DeviceIOControl`) and cannot be unit-tested without a server — its byte layout is covered by Task 3 and its end-to-end behavior by the opt-in integration test (Task 7). The `FakeSmbClientAdapter` implementation added here is what the provider/engine tests (Tasks 5-6) exercise.

- [ ] **Step 1: Write the failing test** (drives the interface + fake; asserted through the provider in Task 5, so here we only add a compile-driving fake test)

```csharp
// Append to tests/Duetto.Tests/Core/Remote/SmbCopyChunkTests.cs
[Fact]
public void FakeAdapter_server_side_copy_copies_bytes_and_reports_progress()
{
    var a = new FakeSmbClientAdapter();
    a.Connect();
    a.CreateDirectory("/share");
    using (var w = a.OpenWrite("/share/src.bin")) w.Write(new byte[3000], 0, 3000);

    long reported = 0;
    var ok = a.ServerSideCopy("/share/src.bin", "/share/dst.bin", n => reported += n, CancellationToken.None);

    Assert.True(ok);
    Assert.Equal(3000, reported);
    Assert.True(a.Exists("/share/dst.bin"));
    Assert.Equal(3000, a.Get("/share/dst.bin")!.Length);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~SmbCopyChunkTests"`
Expected: FAIL to compile — `ISmbClientAdapter` has no `ServerSideCopy`.

- [ ] **Step 3: Write minimal implementation**

Add to the `ISmbClientAdapter` interface in `SmbConnection.cs` (after `SetLastWriteTimeUtc`):

```csharp
// Copies source -> dest entirely on the server (SMB2 copychunk). Both provider-local paths MUST
// be within the same share; the caller guarantees this. Reports per-step bytes via onBytesCopied.
// Returns false when the server does not support copychunk so the caller can stream instead.
bool ServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token);
```

Add to `FakeSmbClientAdapter` (models a server-side copy in-memory):

```csharp
// One-shot / persistent toggles for engine + provider fallback tests.
public bool ServerSideCopySupported { get; set; } = true;

public bool ServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token)
{
    if (!ServerSideCopySupported)
        return false;

    var from = Norm(source);
    var to = Norm(dest);
    if (!nodes.TryGetValue(from, out var srcNode))
        throw new FileNotFoundException($"Source not found: {from}");

    var copy = (byte[])srcNode.Bytes.Clone();
    nodes[to] = new Node { IsDirectory = false, Bytes = copy, LastWriteTimeUtc = DateTime.UtcNow };
    onBytesCopied(copy.Length);
    return true;
}
```

Add to `RealSmbClientAdapter`:

```csharp
public bool ServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token) => Run(() =>
{
    var (srcShare, srcRel) = Split(source);
    var (dstShare, dstRel) = Split(dest);
    if (!string.Equals(srcShare, dstShare, StringComparison.OrdinalIgnoreCase))
        throw new IOException("SMB server-side copy across shares is not supported.");

    var store = Tree(srcShare);
    var length = Get(source)?.Length ?? throw new FileNotFoundException($"SMB copy source not found: {source}");
    if (length < 0) length = 0;

    var openSrc = store.CreateFile(out var srcHandle, out _, srcRel,
        AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE, FileAttributes.Normal,
        ShareAccess.Read, CreateDisposition.FILE_OPEN,
        CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
    if (openSrc != NTStatus.STATUS_SUCCESS)
        throw Translate(openSrc, $"open '{source}' for server-side copy");

    object? dstHandle = null;
    try
    {
        // Any non-success (unsupported FSCTL, or this build returning null output) -> stream.
        // A dropped socket throws InvalidOperationException, surfaced as SmbConnectionException
        // by the Run wrapper, so reconnect still works.
        var rk = store.DeviceIOControl(srcHandle, SmbCopyChunk.FsctlRequestResumeKey, [], out var rkOut, 64);
        if (rk != NTStatus.STATUS_SUCCESS || rkOut is not { Length: >= SmbCopyChunk.ResumeKeyLength })
            return false;
        var resumeKey = SmbCopyChunk.ParseResumeKey(rkOut);

        var openDst = store.CreateFile(out dstHandle, out _, dstRel,
            AccessMask.GENERIC_READ | AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE, FileAttributes.Normal,
            ShareAccess.None, CreateDisposition.FILE_OVERWRITE_IF,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT, null);
        if (openDst != NTStatus.STATUS_SUCCESS)
            throw Translate(openDst, $"open '{dest}' for server-side copy");

        // One 1 MiB chunk per call fits the MS-SMB2 default server limits (MaxChunkSize 1 MiB,
        // MaxChunks 16, MaxDataSize 16 MiB), so a well-formed request is not rejected for sizing.
        // This SMBLibrary build discards the FSCTL output on any non-success status, so we cannot
        // read a server's advertised maxima: any non-success copychunk falls back to streaming
        // (return false). A dropped socket still surfaces via the Run wrapper.
        const int chunk = 1024 * 1024;
        long offset = 0;
        while (offset < length)
        {
            token.ThrowIfCancellationRequested();
            var thisLen = (int)Math.Min(chunk, length - offset);
            var request = SmbCopyChunk.BuildCopyChunkRequest(resumeKey,
                [new SmbCopyChunk.Chunk(offset, offset, thisLen)]);

            var cc = store.DeviceIOControl(dstHandle, SmbCopyChunk.FsctlSrvCopyChunk, request, out var ccOut, 12);
            if (cc != NTStatus.STATUS_SUCCESS || ccOut is not { Length: >= 12 })
                return false;   // unsupported / rejected -> caller streams

            var result = SmbCopyChunk.ParseCopyChunkResponse(ccOut);
            var written = result.TotalBytesWritten > 0 ? (long)result.TotalBytesWritten : thisLen;
            offset += written;
            onBytesCopied(written);
        }

        return true;
    }
    finally
    {
        CloseQuietly(store, srcHandle);
        if (dstHandle is not null)
            CloseQuietly(store, dstHandle);
    }
});
```

Implementation checks (verify against `vendor/SMBLibrary` while coding — do not modify it):
- `INTFileStore.DeviceIOControl(object, uint, byte[], out byte[], int)` exact signature (`SMBLibrary/NTFileStore/INTFileStore.cs`).
- `NTStatus` member names `STATUS_NOT_SUPPORTED`, `STATUS_INVALID_DEVICE_REQUEST`, `STATUS_INVALID_PARAMETER` (`SMBLibrary/NTFileStore/Enums/NTStatus.cs`). Adjust names if they differ.
- Passing `[]` (empty) as FSCTL input is accepted; if not, pass `Array.Empty<byte>()` or a zero-length non-null buffer.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~SmbCopyChunkTests"`
Expected: PASS (6 tests). Also build the whole solution to prove the interface change compiles everywhere:
Run: `dotnet build Duetto.slnx`
Expected: BUILD succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/Duetto.Core/Remote/SmbConnection.cs src/Duetto.Core/Remote/RealSmbClientAdapter.cs tests/Duetto.Tests/Core/Remote/FakeSmbClientAdapter.cs tests/Duetto.Tests/Core/Remote/SmbCopyChunkTests.cs
git commit -m "feat(smb): ServerSideCopy adapter op via copychunk FSCTL"
```

---

## Task 5: Provider `TryServerSideCopy` forwarding

**Files:**
- Create: `src/Duetto.Core/FileSystem/IServerSideCopy.cs`
- Modify: `src/Duetto.Core/Remote/SmbFileSystemProvider.cs`
- Test: `tests/Duetto.Tests/Core/Remote/SmbServerSideCopyProviderTests.cs`

**Interfaces:**
- Consumes: `ISmbClientAdapter.ServerSideCopy` (Task 4).
- Produces: `IServerSideCopy.TryServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token) -> bool`; `SmbFileSystemProvider : IServerSideCopy`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Duetto.Tests/Core/Remote/SmbServerSideCopyProviderTests.cs
using Duetto.Core.FileSystem;
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public class SmbServerSideCopyProviderTests
{
    private static (SmbFileSystemProvider, FakeSmbClientAdapter) Connected()
    {
        var adapter = new FakeSmbClientAdapter();
        var conn = new SmbConnection(new SmbConnectionInfo("id", "n", "node108"),
            new ConnectSecret(""), new FakeSmbFactory(adapter));
        conn.Connect();
        return (new SmbFileSystemProvider(conn), adapter);
    }

    [Fact]
    public void TryServerSideCopy_forwards_to_adapter_and_copies()
    {
        var (provider, adapter) = Connected();
        adapter.CreateDirectory("/share");
        using (var w = adapter.OpenWrite("/share/a.bin")) w.Write(new byte[1234], 0, 1234);

        long reported = 0;
        var ok = ((IServerSideCopy)provider).TryServerSideCopy(
            "/share/a.bin", "/share/b.bin", n => reported += n, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(1234, reported);
        Assert.True(adapter.Exists("/share/b.bin"));
    }

    [Fact]
    public void TryServerSideCopy_returns_false_when_unsupported()
    {
        var (provider, adapter) = Connected();
        adapter.CreateDirectory("/share");
        using (var w = adapter.OpenWrite("/share/a.bin")) w.Write(new byte[10], 0, 10);
        adapter.ServerSideCopySupported = false;

        var ok = ((IServerSideCopy)provider).TryServerSideCopy(
            "/share/a.bin", "/share/b.bin", _ => { }, CancellationToken.None);

        Assert.False(ok);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~SmbServerSideCopyProviderTests"`
Expected: FAIL to compile — `IServerSideCopy` and provider method missing.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/Duetto.Core/FileSystem/IServerSideCopy.cs
namespace Duetto.Core.FileSystem;

// Optional capability: copy `source` to `dest` entirely on the backend (no bytes through the
// client). The caller guarantees, via IBackendIdentity, that both paths share this provider's
// copy domain (same host + share). Reports per-step bytes copied. Returns false when the server
// does not support server-side copy, so the caller falls back to streaming.
public interface IServerSideCopy
{
    bool TryServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token);
}
```

```csharp
// src/Duetto.Core/Remote/SmbFileSystemProvider.cs
// 1) add IServerSideCopy to the declaration:
//    public sealed class SmbFileSystemProvider : IFileSystemProvider, IBackendIdentity, IServerSideCopy, IDisposable
// 2) add the method:
public bool TryServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token)
    => Exec(a => a.ServerSideCopy(source, dest, onBytesCopied, token));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~SmbServerSideCopyProviderTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Duetto.Core/FileSystem/IServerSideCopy.cs src/Duetto.Core/Remote/SmbFileSystemProvider.cs tests/Duetto.Tests/Core/Remote/SmbServerSideCopyProviderTests.cs
git commit -m "feat(smb): provider TryServerSideCopy forwarding to adapter"
```

---

## Task 6: Engine copy offload hook with fallback

**Files:**
- Modify: `src/Duetto.Core/Operations/TransferEngine.cs` (`CopyFile`)
- Test: `tests/Duetto.Tests/Core/ServerSideTransferEngineTests.cs` (extend)

**Interfaces:**
- Consumes: `IServerSideCopy.TryServerSideCopy` (Task 5); `SameRenameDomain` (Task 2).

- [ ] **Step 1: Write the failing test** (extend `ServerSideTransferEngineTests`; make `BackendProvider` also implement `IServerSideCopy`)

Add `IServerSideCopy` to the test `BackendProvider` from Task 2 and these fields/method:

```csharp
// add to BackendProvider:  , IServerSideCopy
public bool ServerSideCopyCalled;
public bool ServerSideCopySupported = true;
public bool TryServerSideCopy(string source, string dest, Action<long> onBytesCopied, CancellationToken token)
{
    ServerSideCopyCalled = true;
    if (!ServerSideCopySupported) return false;
    using var src = store.OpenRead(source);
    using var ms = new MemoryStream(); src.CopyTo(ms);
    var bytes = ms.ToArray();
    using (var w = store.OpenWrite(dest)) w.Write(bytes);
    onBytesCopied(bytes.Length);
    return true;
}
```

```csharp
[Fact]
public async Task Copy_across_same_backend_uses_server_side_copy_not_streaming()
{
    var (src, dst, store) = SameBackendPair();
    Seed(src, "/src", "a.txt", "offloaded copy");

    var session = TransferEngine.Start(["/src/a.txt"], src, "/dst", dst, TransferMode.Copy);
    await session.Completion;

    Assert.True(session.Snapshot().IsComplete);
    Assert.True(src.ServerSideCopyCalled, "must attempt server-side copy");
    Assert.False(src.OpenReadCalled, "must not stream through the client");
    Assert.Equal("offloaded copy", ReadText(store, "/dst/a.txt"));
}

[Fact]
public async Task Copy_falls_back_to_stream_when_offload_unsupported()
{
    var (src, dst, store) = SameBackendPair();
    Seed(src, "/src", "a.txt", "streamed copy");
    src.ServerSideCopySupported = false;

    var session = TransferEngine.Start(["/src/a.txt"], src, "/dst", dst, TransferMode.Copy);
    await session.Completion;

    Assert.True(src.ServerSideCopyCalled, "offload attempted");
    Assert.True(src.OpenReadCalled, "then streamed");
    Assert.Equal("streamed copy", ReadText(store, "/dst/a.txt"));
}

[Fact]
public async Task Copy_across_different_backends_does_not_attempt_offload()
{
    var storeA = new InMemoryFileSystemProvider(); storeA.CreateDirectory("/", "src");
    var storeB = new InMemoryFileSystemProvider(); storeB.CreateDirectory("/", "dst");
    var src = new BackendProvider(storeA, "smb://h/s");
    var dst = new BackendProvider(storeB, "smb://other/s");
    Seed(src, "/src", "a.txt", "x");

    var session = TransferEngine.Start(["/src/a.txt"], src, "/dst", dst, TransferMode.Copy);
    await session.Completion;

    Assert.False(src.ServerSideCopyCalled);
    Assert.True(src.OpenReadCalled);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~ServerSideTransferEngineTests"`
Expected: FAIL — offload tests fail because `CopyFile` always streams.

- [ ] **Step 3: Write minimal implementation**

Refactor `CopyFile` (`TransferEngine.cs:355-397`) so the offload replaces the inner byte loop and the atomic-finish/mtime tail runs for both paths:

```csharp
private static void CopyFile(
    TransferSession session, string source, string dest, DateTime sourceMtimeUtc,
    IFileSystemProvider srcProvider, IFileSystemProvider destProvider)
{
    var useAtomicRename = destProvider.Capabilities.AtomicRename;
    var writePath = useAtomicRename ? dest + ".part" : dest;
    var succeeded = false;
    try
    {
        if (!TryServerSideCopyInto(session, source, writePath, srcProvider, destProvider))
        {
            using var input = srcProvider.OpenRead(source);
            using var output = destProvider.OpenWrite(writePath);
            var buffer = new byte[ChunkSize];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                session.WaitIfPaused();
                session.Token.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                total += read;
                session.FileProgress(source, total, read);
            }
        }

        if (useAtomicRename)
            destProvider.ReplaceFile(writePath, dest);
        if (destProvider.Capabilities.PreservesMTime)
            destProvider.SetLastWriteTimeUtc(dest, sourceMtimeUtc);

        succeeded = true;
    }
    finally
    {
        if (!succeeded && useAtomicRename && destProvider.FileExists(writePath))
            destProvider.Delete(writePath, toTrash: false);
    }
}

// Returns true when the file was fully copied server-side into writePath. Returns false to make
// the caller stream (offload unsupported or a non-fatal server error). Cancellation/auth/
// connection errors propagate so the session faults/cancels as usual.
private static bool TryServerSideCopyInto(
    TransferSession session, string source, string writePath,
    IFileSystemProvider srcProvider, IFileSystemProvider destProvider)
{
    if (srcProvider is not IServerSideCopy ssc
        || !SameRenameDomain(srcProvider, source, destProvider, writePath))
        return false;

    long copied = 0;
    try
    {
        return ssc.TryServerSideCopy(source, writePath, delta =>
        {
            session.WaitIfPaused();
            session.Token.ThrowIfCancellationRequested();
            copied += delta;
            session.FileProgress(source, copied, delta);
        }, session.Token);
    }
    catch (IOException ex) when (ex is not SmbConnectionException and not SmbAuthenticationException)
    {
        // Non-fatal server error (e.g. copychunk rejected) — fall back to streaming.
        return false;
    }
}
```

Note: if the offload copies part of the file then returns `false` (rare — most reject on the first call), the streaming path re-opens `writePath` with `OpenWrite` (truncate) and re-copies; progress may briefly over-count. Acceptable; documented in the spec's Risks.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Duetto.slnx --filter "FullyQualifiedName~ServerSideTransferEngineTests"`
Expected: PASS (6 tests total in the class). Then the full suite:
Run: `dotnet test Duetto.slnx`
Expected: PASS (no regressions).

- [ ] **Step 5: Commit**

```bash
git add src/Duetto.Core/Operations/TransferEngine.cs tests/Duetto.Tests/Core/ServerSideTransferEngineTests.cs
git commit -m "feat(transfer): server-side copy offload for same host+share panes"
```

---

## Task 7: Opt-in Samba integration test (optional)

**Files:**
- Test: `tests/Duetto.Tests/Core/Remote/SmbServerSideCopyIntegrationTests.cs`

**Purpose:** Exercise the real copychunk path (`RealSmbClientAdapter.ServerSideCopy`) end-to-end against the `docker-compose` Samba fixture. Skips when the fixture is not running or the server lacks copychunk, so CI without Docker stays green.

- [ ] **Step 1: Write the test (skippable)**

```csharp
// tests/Duetto.Tests/Core/Remote/SmbServerSideCopyIntegrationTests.cs
using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public class SmbServerSideCopyIntegrationTests
{
    // Set DUETTO_SMB_IT=1 (and have the docker-compose Samba fixture up) to run.
    private static bool Enabled => Environment.GetEnvironmentVariable("DUETTO_SMB_IT") == "1";

    [SkippableFact]  // if a Skippable adapter is not present, guard with: if (!Enabled) return;
    public void Copychunk_copies_multi_mib_file_bytes_exactly()
    {
        if (!Enabled) return;

        // Connect to the fixture (host/share/creds from the compose file), write a >2 MiB source,
        // call ServerSideCopy, then read both back and assert byte-equality; assert the
        // destination length equals the source. Fill in the fixture's host/share/credentials.
    }
}
```

- [ ] **Step 2: Run (opt-in)**

Run: `DUETTO_SMB_IT=1 dotnet test Duetto.slnx --filter "FullyQualifiedName~SmbServerSideCopyIntegrationTests"`
Expected: PASS against a live fixture; otherwise skipped/no-op.

- [ ] **Step 3: Commit**

```bash
git add tests/Duetto.Tests/Core/Remote/SmbServerSideCopyIntegrationTests.cs
git commit -m "test(smb): opt-in copychunk integration test against Samba fixture"
```

---

## Final verification

- [ ] `dotnet build Duetto.slnx` — succeeds.
- [ ] `dotnet test Duetto.slnx` — all pass (integration test skipped without `DUETTO_SMB_IT`).
- [ ] Manual smoke (optional): two panes on the same SMB host+share; a copy shows no local up/down bandwidth; a move completes instantly (rename). Cross-host copy/move still streams.

## Notes carried from the spec

- SFTP intentionally excluded from cross-instance server-side paths (chroot/per-user namespace risk).
- No changes to `vendor/SMBLibrary`; NuGet `SMBLibrary 1.5.7.1` retained.
- Move/copy domain is same host + same share; cross-share same-server offload is a possible later extension.
</content>

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
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(span[24..]));    // ChunkCount
        Assert.Equal(0L, BinaryPrimitives.ReadInt64LittleEndian(span[32..]));     // chunk0 SourceOffset
        Assert.Equal(0L, BinaryPrimitives.ReadInt64LittleEndian(span[40..]));     // chunk0 TargetOffset
        Assert.Equal(1048576, BinaryPrimitives.ReadInt32LittleEndian(span[48..]));// chunk0 Length
        Assert.Equal(1048576L, BinaryPrimitives.ReadInt64LittleEndian(span[56..]));// chunk1 SourceOffset
        Assert.Equal(256, BinaryPrimitives.ReadInt32LittleEndian(span[72..]));    // chunk1 Length
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
}

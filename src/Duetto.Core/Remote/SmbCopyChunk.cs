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

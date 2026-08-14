using Duetto.Core.Remote;

namespace Duetto.Tests.Core.Remote;

public sealed class SmbFileStreamTests
{
    [Fact]
    public void Write_chunks_by_max_size_and_preserves_content()
    {
        var writes = new List<(long Offset, byte[] Data)>();
        var closed = false;

        var payload = new byte[10];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)i;

        using (var stream = SmbFileStream.ForWrite(
                   (offset, data) => writes.Add((offset, data)),
                   () => closed = true,
                   chunk: 4))
        {
            stream.Write(payload, 0, payload.Length);
        }

        Assert.True(closed);
        Assert.Equal(new long[] { 0, 4, 8 }, writes.Select(w => w.Offset).ToArray());
        Assert.All(writes, w => Assert.True(w.Data.Length <= 4));
        Assert.Equal(payload, writes.SelectMany(w => w.Data).ToArray());
    }

    [Fact]
    public void Write_accumulates_across_multiple_small_writes()
    {
        var writes = new List<(long Offset, byte[] Data)>();
        using (var stream = SmbFileStream.ForWrite((offset, data) => writes.Add((offset, data)), null, chunk: 4))
        {
            stream.Write([1, 1, 1], 0, 3);
            stream.Write([2, 2, 2], 0, 3);
            stream.Write([3, 3, 3], 0, 3);
        }

        var reassembled = writes.SelectMany(w => w.Data).ToArray();
        Assert.Equal(new byte[] { 1, 1, 1, 2, 2, 2, 3, 3, 3 }, reassembled);
        Assert.All(writes, w => Assert.True(w.Data.Length <= 4));
    }

    [Fact]
    public void Read_pulls_server_chunks_and_returns_full_content()
    {
        var source = new byte[10];
        for (var i = 0; i < source.Length; i++)
            source[i] = (byte)(i + 100);

        var maxRequested = 0;
        var closed = false;

        byte[] ReadAt(long offset, int count)
        {
            maxRequested = Math.Max(maxRequested, count);
            if (offset >= source.Length)
                return [];
            var len = (int)Math.Min(Math.Min(count, 4), source.Length - offset);
            return source.AsSpan((int)offset, len).ToArray();
        }

        var output = new MemoryStream();
        using (var stream = SmbFileStream.ForRead(ReadAt, () => closed = true, chunk: 4))
        {
            stream.CopyTo(output, bufferSize: 3);
        }

        Assert.True(closed);
        Assert.Equal(source, output.ToArray());
        Assert.True(maxRequested <= 4, $"readAt asked for {maxRequested}, exceeding chunk 4");
    }

    [Fact]
    public void Read_returns_zero_at_eof()
    {
        using var stream = SmbFileStream.ForRead((_, _) => [], null, chunk: 4);
        var buffer = new byte[8];
        Assert.Equal(0, stream.Read(buffer, 0, buffer.Length));
    }
}

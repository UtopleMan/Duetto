namespace Duetto.Core.Remote;

// Forward-only stream over an SMB file handle. Reads pull server chunks of up to the
// negotiated MaxReadSize; writes buffer up to MaxWriteSize before flushing. Not seekable —
// TransferEngine only does sequential Read/Write then Dispose (verified in TransferEngine).
// Backed by delegates so it can be unit-tested without a socket.
internal sealed class SmbFileStream : Stream
{
    private readonly Func<long, int, byte[]>? readAt;
    private readonly Action<long, byte[]>? writeAt;
    private readonly Action? onClose;
    private readonly int chunk;

    private long position;

    private byte[] readBuffer = [];
    private int readBufferPos;

    private readonly byte[]? writeBuffer;
    private int writeBufferLen;

    private bool disposed;

    private SmbFileStream(Func<long, int, byte[]>? readAt, Action<long, byte[]>? writeAt, Action? onClose, int chunk)
    {
        this.readAt = readAt;
        this.writeAt = writeAt;
        this.onClose = onClose;
        this.chunk = chunk < 1 ? 65536 : chunk;

        if (writeAt is not null)
            writeBuffer = new byte[this.chunk];
    }

    public static SmbFileStream ForRead(Func<long, int, byte[]> readAt, Action? onClose, int chunk) =>
        new(readAt, null, onClose, chunk);

    public static SmbFileStream ForWrite(Action<long, byte[]> writeAt, Action? onClose, int chunk) =>
        new(null, writeAt, onClose, chunk);

    public override bool CanRead => readAt is not null;
    public override bool CanWrite => writeAt is not null;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (readAt is null)
            throw new NotSupportedException("Stream is write-only.");
        if (count == 0)
            return 0;

        if (readBufferPos >= readBuffer.Length)
        {
            readBuffer = readAt(position, chunk);
            readBufferPos = 0;
            if (readBuffer.Length == 0)
                return 0;
        }

        var available = readBuffer.Length - readBufferPos;
        var n = Math.Min(count, available);
        Array.Copy(readBuffer, readBufferPos, buffer, offset, n);
        readBufferPos += n;
        position += n;
        return n;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (writeAt is null)
            throw new NotSupportedException("Stream is read-only.");

        var src = offset;
        var remaining = count;
        while (remaining > 0)
        {
            var space = chunk - writeBufferLen;
            var n = Math.Min(space, remaining);
            Array.Copy(buffer, src, writeBuffer!, writeBufferLen, n);
            writeBufferLen += n;
            src += n;
            remaining -= n;
            if (writeBufferLen == chunk)
                FlushWrite();
        }
    }

    public override void Flush() => FlushWrite();

    private void FlushWrite()
    {
        if (writeAt is null || writeBufferLen == 0)
            return;

        // Always hand out a right-sized copy: the internal buffer is reused across flushes, so
        // passing it by reference would let a later Write mutate data a caller may still hold.
        var data = writeBuffer![..writeBufferLen];
        writeAt(position, data);
        position += writeBufferLen;
        writeBufferLen = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposed)
            return;
        disposed = true;

        if (disposing)
        {
            try
            {
                FlushWrite();
            }
            finally
            {
                onClose?.Invoke();
            }
        }

        base.Dispose(disposing);
    }
}

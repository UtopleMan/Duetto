namespace Duetto.Core.Remote;

// Write stream for a block blob. A single upload wants a seekable source, so writes spool to a temp
// file; on close the rewound temp stream is handed to `upload`. The blob only becomes visible once
// the upload completes, so a failed transfer never exposes a partial blob — this is why the Azure
// provider needs no ".part" staging. Backed by a delegate so it can be unit-tested without a network.
internal sealed class AzureFileStream : Stream
{
    private readonly FileStream temp;
    private readonly string tempPath;
    private readonly Action<Stream> upload;
    private bool disposed;

    private AzureFileStream(string tempPath, Action<Stream> upload)
    {
        this.tempPath = tempPath;
        this.upload = upload;
        temp = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    }

    public static AzureFileStream ForWrite(Action<Stream> upload)
    {
        var path = Path.Combine(Path.GetTempPath(), $"duetto-azure-{Guid.NewGuid():N}.part");
        return new AzureFileStream(path, upload);
    }

    public override bool CanRead => false;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => temp.Length;

    public override long Position
    {
        get => temp.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Stream is write-only.");

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        temp.Write(buffer, offset, count);
    }

    public override void Flush() => temp.Flush();

    protected override void Dispose(bool disposing)
    {
        if (disposed)
            return;
        disposed = true;

        if (disposing)
        {
            try
            {
                temp.Flush();
                temp.Position = 0;
                upload(temp);
            }
            finally
            {
                temp.Dispose();
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Best-effort temp cleanup; a leaked temp file must not fail the transfer.
                }
            }
        }

        base.Dispose(disposing);
    }
}

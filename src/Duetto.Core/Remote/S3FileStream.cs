namespace Duetto.Core.Remote;

internal sealed class S3FileStream : Stream
{
    private readonly FileStream temp;
    private readonly string tempPath;
    private readonly Action<Stream> upload;
    private bool disposed;

    private S3FileStream(string tempPath, Action<Stream> upload)
    {
        this.tempPath = tempPath;
        this.upload = upload;
        temp = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    }

    public static S3FileStream ForWrite(Action<Stream> upload)
    {
        var path = Path.Combine(Path.GetTempPath(), $"duetto-s3-{Guid.NewGuid():N}.part");
        return new S3FileStream(path, upload);
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
                }
            }
        }

        base.Dispose(disposing);
    }
}

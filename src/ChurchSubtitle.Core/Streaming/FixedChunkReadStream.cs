namespace ChurchSubtitle.Core.Streaming;

public sealed class FixedChunkReadStream : Stream
{
    private readonly Stream _inner;
    private readonly bool _leaveOpen;
    private bool _reachedEnd;
    private bool _disposed;

    public FixedChunkReadStream(Stream inner, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => !_disposed && _inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_reachedEnd)
        {
            return 0;
        }

        var totalBytesRead = 0;
        while (totalBytesRead < buffer.Length)
        {
            var bytesRead = _inner.Read(buffer[totalBytesRead..]);
            if (bytesRead == 0)
            {
                _reachedEnd = true;
                break;
            }

            totalBytesRead += bytesRead;
        }

        return totalBytesRead;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_reachedEnd)
        {
            return 0;
        }

        var totalBytesRead = 0;
        while (totalBytesRead < buffer.Length)
        {
            int bytesRead;
            try
            {
                bytesRead = await _inner.ReadAsync(
                    buffer[totalBytesRead..],
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (totalBytesRead > 0)
            {
                return totalBytesRead;
            }

            if (bytesRead == 0)
            {
                _reachedEnd = true;
                break;
            }

            totalBytesRead += bytesRead;
        }

        return totalBytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException());

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing && !_leaveOpen)
            {
                _inner.Dispose();
            }

            _disposed = true;
        }

        base.Dispose(disposing);
    }
}

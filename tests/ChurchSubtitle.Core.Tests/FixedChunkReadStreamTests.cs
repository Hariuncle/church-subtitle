using ChurchSubtitle.Core.Streaming;

namespace ChurchSubtitle.Core.Tests;

public sealed class FixedChunkReadStreamTests
{
    [Fact]
    public async Task ReadAsync_combines_multiple_short_reads_until_destination_is_full()
    {
        await using var inner = new ShortReadStream([1, 2, 3, 4, 5, 6], maxReadSize: 2);
        await using var stream = new FixedChunkReadStream(inner, leaveOpen: true);
        var destination = new byte[5];

        var bytesRead = await stream.ReadAsync(destination);

        Assert.Equal(5, bytesRead);
        Assert.Equal([1, 2, 3, 4, 5], destination);
        Assert.Equal(3, inner.ReadCount);
    }

    [Fact]
    public async Task ReadAsync_returns_remaining_bytes_at_eof_then_zero()
    {
        await using var inner = new ShortReadStream([1, 2, 3], maxReadSize: 2);
        await using var stream = new FixedChunkReadStream(inner, leaveOpen: true);
        var destination = new byte[5];

        var remainingBytesRead = await stream.ReadAsync(destination);
        var bytesReadAfterEof = await stream.ReadAsync(destination);

        Assert.Equal(3, remainingBytesRead);
        Assert.Equal([1, 2, 3], destination[..remainingBytesRead]);
        Assert.Equal(0, bytesReadAfterEof);
    }

    [Fact]
    public async Task ReadAsync_returns_accumulated_bytes_when_next_short_read_is_cancelled()
    {
        using var cancellationSource = new CancellationTokenSource();
        await using var inner = new CancelOnSecondAsyncReadStream(cancellationSource);
        await using var stream = new FixedChunkReadStream(inner, leaveOpen: true);
        var destination = new byte[5];

        var pendingRead = stream.ReadAsync(destination, cancellationSource.Token);

        Assert.False(pendingRead.IsCompleted);
        var bytesRead = await pendingRead;
        Assert.Equal(2, bytesRead);
        Assert.Equal([1, 2], destination[..bytesRead]);
        Assert.True(cancellationSource.IsCancellationRequested);
    }

    [Fact]
    public void Read_combines_short_reads_and_returns_remaining_bytes_at_eof()
    {
        using var inner = new ShortReadStream([1, 2, 3], maxReadSize: 1);
        using var stream = new FixedChunkReadStream(inner, leaveOpen: true);
        var destination = new byte[5];

        var remainingBytesRead = stream.Read(destination);
        var bytesReadAfterEof = stream.Read(destination);

        Assert.Equal(3, remainingBytesRead);
        Assert.Equal([1, 2, 3], destination[..remainingBytesRead]);
        Assert.Equal(0, bytesReadAfterEof);
    }

    [Fact]
    public void Dispose_disposes_inner_stream_when_leaveOpen_is_false()
    {
        var inner = new ShortReadStream([], maxReadSize: 1);
        var stream = new FixedChunkReadStream(inner, leaveOpen: false);

        stream.Dispose();

        Assert.True(inner.IsDisposed);
    }

    [Fact]
    public void Dispose_leaves_inner_stream_open_when_leaveOpen_is_true()
    {
        var inner = new ShortReadStream([], maxReadSize: 1);
        var stream = new FixedChunkReadStream(inner, leaveOpen: true);

        stream.Dispose();

        Assert.False(inner.IsDisposed);
        inner.Dispose();
    }

    [Fact]
    public void Reports_read_only_non_seekable_capabilities_and_rejects_unsupported_operations()
    {
        using var inner = new ShortReadStream([], maxReadSize: 1);
        using var stream = new FixedChunkReadStream(inner, leaveOpen: true);

        Assert.True(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        Assert.Throws<NotSupportedException>(() => stream.Write([], 0, 0));
    }

    private sealed class ShortReadStream(byte[] data, int maxReadSize) : Stream
    {
        private int _position;

        public int ReadCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);

            var bytesRead = Math.Min(Math.Min(count, maxReadSize), data.Length - _position);
            data.AsSpan(_position, bytesRead).CopyTo(buffer.AsSpan(offset, bytesRead));
            _position += bytesRead;
            ReadCount++;
            return bytesRead;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = Read(buffer.Span);
            return ValueTask.FromResult(bytesRead);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class CancelOnSecondAsyncReadStream(CancellationTokenSource cancellationSource)
        : Stream
    {
        private int _readCount;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            if (_readCount++ == 0)
            {
                buffer.Span[0] = 1;
                buffer.Span[1] = 2;
                return 2;
            }

            cancellationSource.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("A cancellation token was expected.");
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

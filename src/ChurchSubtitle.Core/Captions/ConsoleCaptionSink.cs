namespace ChurchSubtitle.Core.Captions;

public sealed class ConsoleCaptionSink : ICaptionSink
{
    private readonly TextWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _previousLength;

    public ConsoleCaptionSink(TextWriter writer)
    {
        _writer = writer;
    }

    public async ValueTask PublishAsync(
        CaptionUpdate update,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (update.State is CaptionState.Partial)
            {
                var padding = new string(' ', Math.Max(0, _previousLength - update.Text.Length));
                await _writer.WriteAsync($"\r{update.Text}{padding}".AsMemory(), cancellationToken);
                _previousLength = update.Text.Length;
            }
            else if (update.State is CaptionState.Final)
            {
                await _writer.WriteLineAsync($"\r{update.Text}".AsMemory(), cancellationToken);
                _previousLength = 0;
            }

            await _writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

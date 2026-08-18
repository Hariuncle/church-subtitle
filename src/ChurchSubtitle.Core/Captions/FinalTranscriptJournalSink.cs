namespace ChurchSubtitle.Core.Captions;

public sealed class FinalTranscriptJournalSink : ICaptionSink
{
    private readonly TextWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FinalTranscriptJournalSink(TextWriter writer)
    {
        _writer = writer;
    }

    public async ValueTask PublishAsync(
        CaptionUpdate update,
        CancellationToken cancellationToken)
    {
        if (update.State is not CaptionState.Final)
        {
            return;
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteLineAsync(update.Text.AsMemory(), cancellationToken);
            await _writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

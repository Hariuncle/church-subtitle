namespace ChurchSubtitle.Core.Captions;

public sealed class FinalTranscriptSink : ICompletableCaptionSink
{
    private readonly TextWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly List<CaptionUpdate> _finals = [];

    public FinalTranscriptSink(TextWriter writer)
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
            _finals.Add(update);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var update in _finals
                .OrderBy(update => update.SegmentOrdinal.HasValue ? 0 : 1)
                .ThenBy(update => update.SegmentOrdinal ?? long.MaxValue)
                .ThenBy(update => update.AudioStartMs ?? long.MaxValue)
                .ThenBy(update => update.Sequence))
            {
                await _writer.WriteLineAsync(update.Text.AsMemory(), cancellationToken);
            }

            await _writer.FlushAsync(cancellationToken);
            _finals.Clear();
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

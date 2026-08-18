namespace ChurchSubtitle.Core.Captions;

public sealed class CompositeCaptionSink(params ICaptionSink[] sinks) : ICompletableCaptionSink
{
    private readonly IReadOnlyList<ICaptionSink> _sinks = sinks;

    public async ValueTask PublishAsync(
        CaptionUpdate update,
        CancellationToken cancellationToken)
    {
        foreach (var sink in _sinks)
        {
            await sink.PublishAsync(update, cancellationToken);
        }
    }


    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        foreach (var sink in _sinks.OfType<ICompletableCaptionSink>())
        {
            await sink.CompleteAsync(cancellationToken);
        }
    }
}

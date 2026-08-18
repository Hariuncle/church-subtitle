namespace ChurchSubtitle.Core.Captions;

public interface ICaptionSink
{
    ValueTask PublishAsync(CaptionUpdate update, CancellationToken cancellationToken);
}

public interface ICompletableCaptionSink : ICaptionSink
{
    ValueTask CompleteAsync(CancellationToken cancellationToken);
}

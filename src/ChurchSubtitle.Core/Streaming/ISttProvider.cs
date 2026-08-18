using ChurchSubtitle.Core.Captions;

namespace ChurchSubtitle.Core.Streaming;

public interface ISttProvider<in TRequest>
{
    Task<TranscriptionRunResult> TranscribeAsync(
        Stream pcmAudio,
        TRequest request,
        ICaptionSink sink,
        CancellationToken cancellationToken);
}

public sealed record TranscriptionRunResult(
    bool Completed,
    int ReconnectCount,
    OpenAi.TranscriptionRuntimeMetrics? Metrics = null);

public sealed class TranscriptionRunFailedException : Exception
{
    public TranscriptionRunFailedException(
        string message,
        TranscriptionRunResult partialResult,
        Exception innerException)
        : base(message, innerException)
    {
        PartialResult = partialResult;
    }

    public TranscriptionRunResult PartialResult { get; }
}

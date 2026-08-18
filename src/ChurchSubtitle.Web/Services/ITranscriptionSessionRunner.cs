using ChurchSubtitle.Core.Captions;
using ChurchSubtitle.Core.Streaming;

namespace ChurchSubtitle.Web.Services;

public interface ITranscriptionSessionRunner
{
    Task<TranscriptionRunResult> RunAsync(
        Stream pcmAudio,
        string sessionId,
        string delay,
        ICaptionSink sink,
        CancellationToken cancellationToken);
}

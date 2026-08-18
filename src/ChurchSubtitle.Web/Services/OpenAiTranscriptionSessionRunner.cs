using ChurchSubtitle.Core.Captions;
using ChurchSubtitle.Core.OpenAi;
using ChurchSubtitle.Core.Streaming;

namespace ChurchSubtitle.Web.Services;

public sealed class OpenAiTranscriptionSessionRunner : ITranscriptionSessionRunner
{
    private const string ChurchPrompt =
        "교회 예배 설교의 한국어 실시간 자막입니다. 성경 인명, 지명, 성경 권명과 교회 용어를 정확히 전사하세요.";

    private readonly string _apiKey;
    private readonly IReadOnlyList<string> _keywords;
    private readonly OpenAiRealtimeTranscriptionProvider _provider;

    public OpenAiTranscriptionSessionRunner(
        string apiKey,
        IReadOnlyList<string> keywords)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(keywords);

        _apiKey = apiKey;
        _keywords = keywords.ToArray();
        _provider = new OpenAiRealtimeTranscriptionProvider(
            new ClientWebSocketConnectionFactory(),
            new SystemAsyncDelay(),
            new ReconnectPolicy(maxAttempts: 3),
            drainPeriod: TimeSpan.FromSeconds(10));
    }

    public Task<TranscriptionRunResult> RunAsync(
        Stream pcmAudio,
        string sessionId,
        string delay,
        ICaptionSink sink,
        CancellationToken cancellationToken)
    {
        var request = new OpenAiTranscriptionRequest(
            _apiKey,
            sessionId,
            new OpenAiTranscriptionOptions(delay, ChurchPrompt, _keywords));
        return _provider.TranscribeAsync(
            pcmAudio,
            request,
            sink,
            cancellationToken);
    }
}

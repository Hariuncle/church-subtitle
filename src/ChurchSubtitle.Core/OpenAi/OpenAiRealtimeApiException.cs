namespace ChurchSubtitle.Core.OpenAi;

public sealed class OpenAiRealtimeApiException : Exception
{
    public OpenAiRealtimeApiException(string code, string message)
        : base($"OpenAI Realtime API error ({code}): {message}")
    {
        Code = code;
    }

    public string Code { get; }
}

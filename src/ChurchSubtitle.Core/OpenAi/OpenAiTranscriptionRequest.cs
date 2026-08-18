namespace ChurchSubtitle.Core.OpenAi;

public sealed record OpenAiTranscriptionRequest(
    string ApiKey,
    string SessionId,
    OpenAiTranscriptionOptions Options);

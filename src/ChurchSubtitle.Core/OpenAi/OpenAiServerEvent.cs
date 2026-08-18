using ChurchSubtitle.Core.Captions;

namespace ChurchSubtitle.Core.OpenAi;

public abstract record OpenAiServerEvent(DateTimeOffset ReceivedAtUtc);

public sealed record TranscriptServerEvent(
    CaptionUpdate Update,
    DateTimeOffset ReceivedAtUtc,
    string? ProviderItemId = null) : OpenAiServerEvent(ReceivedAtUtc);

public sealed record AudioBufferCommittedServerEvent(
    string ItemId,
    DateTimeOffset ReceivedAtUtc) : OpenAiServerEvent(ReceivedAtUtc);

public enum SpeechBoundaryKind
{
    Started,
    Stopped
}

public sealed record SpeechBoundaryServerEvent(
    string ItemId,
    SpeechBoundaryKind Kind,
    long AudioOffsetMs,
    DateTimeOffset ReceivedAtUtc) : OpenAiServerEvent(ReceivedAtUtc);

public sealed record ProviderErrorServerEvent(
    string Code,
    string Message,
    DateTimeOffset ReceivedAtUtc) : OpenAiServerEvent(ReceivedAtUtc);

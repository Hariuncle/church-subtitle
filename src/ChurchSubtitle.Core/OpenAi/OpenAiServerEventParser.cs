using System.Text.Json;
using ChurchSubtitle.Core.Captions;

namespace ChurchSubtitle.Core.OpenAi;

public sealed class OpenAiServerEventParser
{
    private readonly TranscriptProjector _projector;
    private readonly long _audioBaseOffsetMs;
    private readonly string _itemIdNamespace;

    public OpenAiServerEventParser(
        TranscriptProjector projector,
        long audioBaseOffsetMs = 0,
        string itemIdNamespace = "")
    {
        _projector = projector;
        _audioBaseOffsetMs = audioBaseOffsetMs;
        _itemIdNamespace = itemIdNamespace;
    }

    public OpenAiServerEvent? Parse(string json, DateTimeOffset receivedAtUtc)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString();

        return type switch
        {
            "conversation.item.input_audio_transcription.delta" =>
                CreateTranscriptEvent(
                    root,
                    _projector.ApplyDelta(
                        Qualify(root.GetProperty("item_id").GetString()!),
                        root.GetProperty("delta").GetString() ?? string.Empty,
                        receivedAtUtc),
                    receivedAtUtc),
            "conversation.item.input_audio_transcription.completed" =>
                CreateTranscriptEvent(
                    root,
                    _projector.ApplyCompleted(
                        Qualify(root.GetProperty("item_id").GetString()!),
                        root.GetProperty("transcript").GetString() ?? string.Empty,
                        receivedAtUtc),
                    receivedAtUtc),
            "input_audio_buffer.committed" =>
                new AudioBufferCommittedServerEvent(
                    root.GetProperty("item_id").GetString()!,
                    receivedAtUtc),
            "input_audio_buffer.speech_started" => CreateStartedBoundary(
                root,
                receivedAtUtc),
            "input_audio_buffer.speech_stopped" => CreateBoundary(
                root,
                SpeechBoundaryKind.Stopped,
                "audio_end_ms",
                receivedAtUtc),
            "error" => CreateError(root, receivedAtUtc),
            _ => null
        };
    }

    private SpeechBoundaryServerEvent CreateStartedBoundary(
        JsonElement root,
        DateTimeOffset receivedAtUtc)
    {
        var itemId = Qualify(root.GetProperty("item_id").GetString()!);
        var audioStartMs = root.GetProperty("audio_start_ms").GetInt64();
        _projector.RegisterSpeechStart(itemId, _audioBaseOffsetMs + audioStartMs);
        return new SpeechBoundaryServerEvent(
            itemId,
            SpeechBoundaryKind.Started,
            audioStartMs,
            receivedAtUtc);
    }

    private SpeechBoundaryServerEvent CreateBoundary(
        JsonElement root,
        SpeechBoundaryKind kind,
        string offsetProperty,
        DateTimeOffset receivedAtUtc) =>
        new(
            Qualify(root.GetProperty("item_id").GetString()!),
            kind,
            root.GetProperty(offsetProperty).GetInt64(),
            receivedAtUtc);

    private TranscriptServerEvent CreateTranscriptEvent(
        JsonElement root,
        CaptionUpdate update,
        DateTimeOffset receivedAtUtc) =>
        new(
            update,
            receivedAtUtc,
            root.GetProperty("item_id").GetString()!);

    private string Qualify(string itemId) => string.IsNullOrEmpty(_itemIdNamespace)
        ? itemId
        : $"{_itemIdNamespace}:{itemId}";

    private static ProviderErrorServerEvent CreateError(
        JsonElement root,
        DateTimeOffset receivedAtUtc)
    {
        var error = root.GetProperty("error");
        return new ProviderErrorServerEvent(
            error.TryGetProperty("code", out var code)
                ? code.GetString() ?? "unknown_error"
                : "unknown_error",
            error.TryGetProperty("message", out var message)
                ? message.GetString() ?? "OpenAI Realtime API error"
                : "OpenAI Realtime API error",
            receivedAtUtc);
    }
}

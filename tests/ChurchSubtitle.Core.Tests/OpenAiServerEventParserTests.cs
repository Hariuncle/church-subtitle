using ChurchSubtitle.Core.Captions;
using ChurchSubtitle.Core.OpenAi;

namespace ChurchSubtitle.Core.Tests;

public sealed class OpenAiServerEventParserTests
{
    [Fact]
    public void Committed_event_exposes_the_provider_item_id()
    {
        var parser = new OpenAiServerEventParser(
            new TranscriptProjector("session", "openai"));

        var parsed = parser.Parse(
            """{"type":"input_audio_buffer.committed","item_id":"item-17","previous_item_id":null}""",
            DateTimeOffset.Parse("2026-08-19T00:00:00Z"));

        var committed = Assert.IsType<AudioBufferCommittedServerEvent>(parsed);
        Assert.Equal("item-17", committed.ItemId);
    }

    [Fact]
    public void Connection_namespace_prevents_provider_item_id_collisions()
    {
        var projector = new TranscriptProjector("session", "openai");
        var first = new OpenAiServerEventParser(projector, itemIdNamespace: "connection-1");
        var second = new OpenAiServerEventParser(projector, itemIdNamespace: "connection-2");

        var firstFinal = Assert.IsType<TranscriptServerEvent>(first.Parse(
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-1","transcript":"첫째"}""",
            DateTimeOffset.Parse("2026-08-19T00:00:00Z")));
        var secondFinal = Assert.IsType<TranscriptServerEvent>(second.Parse(
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-1","transcript":"둘째"}""",
            DateTimeOffset.Parse("2026-08-19T00:00:01Z")));

        Assert.Equal("connection-1:item-1", firstFinal.Update.LineId);
        Assert.Equal("connection-2:item-1", secondFinal.Update.LineId);
        Assert.Equal("item-1", firstFinal.ProviderItemId);
        Assert.Equal("item-1", secondFinal.ProviderItemId);
    }
    [Fact]
    public void Delta_event_becomes_accumulated_partial_caption()
    {
        var parser = CreateParser();
        var timestamp = DateTimeOffset.Parse("2026-08-18T01:00:00Z");

        var first = Assert.IsType<TranscriptServerEvent>(parser.Parse(
            """{"type":"conversation.item.input_audio_transcription.delta","item_id":"item-1","delta":"하나"}""",
            timestamp));
        var second = Assert.IsType<TranscriptServerEvent>(parser.Parse(
            """{"type":"conversation.item.input_audio_transcription.delta","item_id":"item-1","delta":"님"}""",
            timestamp.AddMilliseconds(100)));

        Assert.Equal("하나", first.Update.Text);
        Assert.Equal("하나님", second.Update.Text);
        Assert.Equal(CaptionState.Partial, second.Update.State);
    }

    [Fact]
    public void Completed_event_becomes_final_caption()
    {
        var parser = CreateParser();

        var parsed = Assert.IsType<TranscriptServerEvent>(parser.Parse(
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-2","transcript":"창세기 1장"}""",
            DateTimeOffset.UtcNow));

        Assert.Equal("창세기 1장", parsed.Update.Text);
        Assert.Equal(CaptionState.Final, parsed.Update.State);
    }

    [Fact]
    public void Speech_start_offset_is_attached_to_later_caption_updates()
    {
        var parser = CreateParser();
        parser.Parse(
            """{"type":"input_audio_buffer.speech_started","item_id":"item-4","audio_start_ms":1250}""",
            DateTimeOffset.UtcNow);

        var parsed = Assert.IsType<TranscriptServerEvent>(parser.Parse(
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-4","transcript":"말씀"}""",
            DateTimeOffset.UtcNow));

        Assert.Equal(1250, parsed.Update.AudioStartMs);
    }

    [Fact]
    public void Caption_audio_start_uses_the_run_global_session_offset()
    {
        var parser = new OpenAiServerEventParser(
            new TranscriptProjector("session", "openai"),
            audioBaseOffsetMs: 300_000);
        parser.Parse(
            """{"type":"input_audio_buffer.speech_started","item_id":"item-5","audio_start_ms":1250}""",
            DateTimeOffset.UtcNow);

        var parsed = Assert.IsType<TranscriptServerEvent>(parser.Parse(
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-5","transcript":"말씀"}""",
            DateTimeOffset.UtcNow));

        Assert.Equal(301_250, parsed.Update.AudioStartMs);
    }

    [Theory]
    [InlineData("input_audio_buffer.speech_started", "audio_start_ms", 1200, SpeechBoundaryKind.Started)]
    [InlineData("input_audio_buffer.speech_stopped", "audio_end_ms", 3400, SpeechBoundaryKind.Stopped)]
    public void Vad_events_capture_audio_offsets(
        string type,
        string offsetProperty,
        long offset,
        SpeechBoundaryKind expectedKind)
    {
        var parser = CreateParser();
        var json = $$"""{"type":"{{type}}","item_id":"item-3","{{offsetProperty}}":{{offset}}}""";

        var parsed = Assert.IsType<SpeechBoundaryServerEvent>(
            parser.Parse(json, DateTimeOffset.UtcNow));

        Assert.Equal("item-3", parsed.ItemId);
        Assert.Equal(offset, parsed.AudioOffsetMs);
        Assert.Equal(expectedKind, parsed.Kind);
    }

    [Fact]
    public void Api_error_becomes_provider_error_without_exposing_request_headers()
    {
        var parser = CreateParser();

        var parsed = Assert.IsType<ProviderErrorServerEvent>(parser.Parse(
            """{"type":"error","error":{"code":"invalid_request_error","message":"Bad session"}}""",
            DateTimeOffset.UtcNow));

        Assert.Equal("invalid_request_error", parsed.Code);
        Assert.Equal("Bad session", parsed.Message);
    }

    private static OpenAiServerEventParser CreateParser() =>
        new(new TranscriptProjector("session", "openai"));
}

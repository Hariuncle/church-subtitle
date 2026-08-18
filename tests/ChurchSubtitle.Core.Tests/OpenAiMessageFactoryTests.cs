using System.Text.Json;
using ChurchSubtitle.Core.OpenAi;

namespace ChurchSubtitle.Core.Tests;

public sealed class OpenAiMessageFactoryTests
{
    [Fact]
    public void Session_update_uses_live_transcribe_korean_pcm24k_with_manual_turn_detection()
    {
        var options = new OpenAiTranscriptionOptions(
            Delay: "low",
            Prompt: "한국어 교회 예배 설교",
            Keywords: ["창세기", "예수님"]);

        using var document = JsonDocument.Parse(
            OpenAiMessageFactory.CreateSessionUpdate(options));
        var session = document.RootElement.GetProperty("session");
        var input = session.GetProperty("audio").GetProperty("input");
        var transcription = input.GetProperty("transcription");

        Assert.Equal("session.update", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("transcription", session.GetProperty("type").GetString());
        Assert.Equal("audio/pcm", input.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal(24000, input.GetProperty("format").GetProperty("rate").GetInt32());
        Assert.Equal("gpt-live-transcribe", transcription.GetProperty("model").GetString());
        Assert.Equal("ko", transcription.GetProperty("languages")[0].GetString());
        Assert.Equal("low", transcription.GetProperty("delay").GetString());
        Assert.Equal(JsonValueKind.Null, input.GetProperty("turn_detection").ValueKind);
    }

    [Fact]
    public void Audio_append_encodes_pcm_bytes_as_base64()
    {
        using var document = JsonDocument.Parse(
            OpenAiMessageFactory.CreateAudioAppend([0x01, 0x02, 0x03]));

        Assert.Equal("input_audio_buffer.append", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("AQID", document.RootElement.GetProperty("audio").GetString());
    }

    [Fact]
    public void Audio_commit_uses_the_exact_realtime_client_event_shape()
    {
        Assert.Equal(
            "{\"type\":\"input_audio_buffer.commit\"}",
            OpenAiMessageFactory.CreateAudioCommit());
    }

    [Theory]
    [InlineData("minimal")]
    [InlineData("high")]
    [InlineData("")]
    public void Options_reject_delay_values_outside_poc_scope(string delay)
    {
        Assert.Throws<ArgumentException>(() =>
            new OpenAiTranscriptionOptions(delay, "prompt", []));
    }

    [Theory]
    [InlineData("<예수님")]
    [InlineData("예수님>")]
    [InlineData("예수님\n하나님")]
    public void Options_reject_keywords_disallowed_by_realtime_api(string keyword)
    {
        Assert.Throws<ArgumentException>(() =>
            new OpenAiTranscriptionOptions("low", "prompt", [keyword]));
    }
}

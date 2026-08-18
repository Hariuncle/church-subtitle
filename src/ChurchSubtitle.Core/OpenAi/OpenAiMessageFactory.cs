using System.Text.Json;

namespace ChurchSubtitle.Core.OpenAi;

public static class OpenAiMessageFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static string CreateSessionUpdate(OpenAiTranscriptionOptions options)
    {
        var message = new
        {
            type = "session.update",
            session = new
            {
                type = "transcription",
                audio = new
                {
                    input = new
                    {
                        format = new
                        {
                            type = "audio/pcm",
                            rate = 24000
                        },
                        transcription = new
                        {
                            model = "gpt-live-transcribe",
                            prompt = options.Prompt,
                            keywords = options.Keywords,
                            languages = new[] { "ko" },
                            delay = options.Delay
                        },
                        turn_detection = (object?)null
                    }
                }
            }
        };

        return JsonSerializer.Serialize(message, SerializerOptions);
    }

    public static string CreateAudioAppend(ReadOnlySpan<byte> pcm) =>
        JsonSerializer.Serialize(new
        {
            type = "input_audio_buffer.append",
            audio = Convert.ToBase64String(pcm)
        });

    public static string CreateAudioCommit() =>
        "{\"type\":\"input_audio_buffer.commit\"}";
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChurchSubtitle.Core.Captions;

public sealed class JsonlCaptionSink : ICaptionSink
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly TextWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonlCaptionSink(TextWriter writer)
    {
        _writer = writer;
    }

    public async ValueTask PublishAsync(
        CaptionUpdate update,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(update, SerializerOptions);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteLineAsync(json.AsMemory(), cancellationToken);
            await _writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

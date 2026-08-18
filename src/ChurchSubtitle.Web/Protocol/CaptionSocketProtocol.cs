using System.Text.Json;

namespace ChurchSubtitle.Web.Protocol;

public static class CaptionSocketProtocol
{
    private const int MaximumCommandLength = 4096;

    public static CaptionSocketCommand Parse(string? json)
    {
        if (json is null || json.Length > MaximumCommandLength)
        {
            throw InvalidCommand();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw InvalidCommand();
            }

            if (!root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                throw InvalidCommand();
            }

            return typeElement.GetString() switch
            {
                "start" => ParseStart(root),
                "end" => ParseEnd(root),
                _ => throw InvalidCommand()
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Invalid caption socket command.", exception);
        }
    }

    private static StartCaptionSocketCommand ParseStart(JsonElement root)
    {
        if (root.EnumerateObject().Count() != 2 ||
            !root.TryGetProperty("delay", out var delayElement) ||
            delayElement.ValueKind != JsonValueKind.String)
        {
            throw InvalidCommand();
        }

        var delay = delayElement.GetString();
        return delay is "low" or "medium"
            ? new StartCaptionSocketCommand(delay)
            : throw InvalidCommand();
    }

    private static EndCaptionSocketCommand ParseEnd(JsonElement root) =>
        root.EnumerateObject().Count() == 1
            ? new EndCaptionSocketCommand()
            : throw InvalidCommand();

    private static InvalidDataException InvalidCommand() =>
        new("Invalid caption socket command.");
}

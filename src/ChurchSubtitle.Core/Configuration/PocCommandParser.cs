namespace ChurchSubtitle.Core.Configuration;

public abstract record PocCommandOptions;

public sealed record TranscribeCommandOptions(
    string PcmPath,
    string Delay,
    string OutputDirectory,
    string? KeywordsPath,
    string Prompt) : PocCommandOptions;

public sealed record EvaluateCommandOptions(
    string ReferencePath,
    string HypothesisPath,
    string KeywordsPath,
    string RuntimeMetricsPath,
    string OutputPath) : PocCommandOptions;

public static class PocCommandParser
{
    public const string DefaultPrompt =
        "한국어 교회 예배와 설교. 성경 구절, 성경 인명과 지명, 기도문이 포함됩니다.";

    public static PocCommandOptions Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            throw new ArgumentException("A command is required: transcribe or evaluate.");
        }

        var options = ParseOptions(arguments.Skip(1).ToArray());
        return arguments[0] switch
        {
            "transcribe" => new TranscribeCommandOptions(
                Required(options, "--pcm"),
                Required(options, "--delay"),
                Required(options, "--output"),
                Optional(options, "--keywords"),
                Optional(options, "--prompt") ?? DefaultPrompt),
            "evaluate" => new EvaluateCommandOptions(
                Required(options, "--reference"),
                Required(options, "--hypothesis"),
                Required(options, "--keywords"),
                Required(options, "--runtime-metrics"),
                Required(options, "--output")),
            _ => throw new ArgumentException($"Unknown command: {arguments[0]}")
        };
    }

    private static Dictionary<string, string> ParseOptions(IReadOnlyList<string> arguments)
    {
        if (arguments.Count % 2 != 0)
        {
            throw new ArgumentException($"Missing value for option: {arguments[^1]}");
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index += 2)
        {
            var name = arguments[index];
            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Invalid option name: {name}");
            }

            options[name] = arguments[index + 1];
        }

        return options;
    }

    private static string Required(
        IReadOnlyDictionary<string, string> options,
        string name) =>
        Optional(options, name) ??
        throw new ArgumentException($"Required option is missing: {name}");

    private static string? Optional(
        IReadOnlyDictionary<string, string> options,
        string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}

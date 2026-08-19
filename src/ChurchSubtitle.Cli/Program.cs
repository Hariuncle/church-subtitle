using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChurchSubtitle.Core.Captions;
using ChurchSubtitle.Core.Configuration;
using ChurchSubtitle.Core.Evaluation;
using ChurchSubtitle.Core.OpenAi;
using ChurchSubtitle.Core.Streaming;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
{
    PrintUsage();
    return 0;
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

try
{
    var command = PocCommandParser.Parse(args);
    switch (command)
    {
        case TranscribeCommandOptions transcribe:
            await RunTranscriptionAsync(transcribe, shutdown.Token);
            break;
        case EvaluateCommandOptions evaluate:
            await RunEvaluationAsync(evaluate, shutdown.Token);
            break;
    }

    return 0;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    Console.Error.WriteLine("사용자 요청으로 중단했습니다.");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"오류: {exception.Message}");
    Console.Error.WriteLine("AI 자막을 중지했습니다. 기존 수동 자막으로 전환하세요.");
    return 1;
}

static async Task RunTranscriptionAsync(
    TranscribeCommandOptions command,
    CancellationToken cancellationToken)
{
    if (!File.Exists(command.PcmPath))
    {
        throw new FileNotFoundException("PCM 입력 파일을 찾을 수 없습니다.", command.PcmPath);
    }

    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        throw new InvalidOperationException(
            "OPENAI_API_KEY 환경변수가 필요합니다. 키는 명령줄 인자로 전달하지 마세요.");
    }

    var keywords = command.KeywordsPath is null
        ? []
        : await ReadKeywordFileAsync(command.KeywordsPath, cancellationToken);
    var options = new OpenAiTranscriptionOptions(command.Delay, command.Prompt, keywords);
    Directory.CreateDirectory(command.OutputDirectory);
    WarnIfDiskSpaceIsLow(command.OutputDirectory);

    var eventsPath = Path.Combine(command.OutputDirectory, "events.jsonl");
    var transcriptPath = Path.Combine(command.OutputDirectory, "final.txt");
    var transcriptJournalPath = Path.Combine(
        command.OutputDirectory,
        "final-arrival-journal.txt");
    var metricsPath = Path.Combine(command.OutputDirectory, "runtime-metrics.json");

    await using var pcm = new FileStream(
        command.PcmPath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 64 * 1024,
        useAsync: true);
    await using var eventsWriter = CreateNewUtf8Writer(eventsPath);
    await using var transcriptWriter = CreateNewUtf8Writer(transcriptPath);
    await using var transcriptJournalWriter = CreateNewUtf8Writer(transcriptJournalPath);

    var sink = new CompositeCaptionSink(
        new ConsoleCaptionSink(Console.Out),
        new JsonlCaptionSink(eventsWriter),
        new FinalTranscriptJournalSink(transcriptJournalWriter),
        new FinalTranscriptSink(transcriptWriter));
    var provider = new OpenAiRealtimeTranscriptionProvider(
        new ClientWebSocketConnectionFactory(),
        new SystemAsyncDelay(),
        new ReconnectPolicy(maxAttempts: 3),
        drainPeriod: TimeSpan.FromSeconds(10));
    var request = new OpenAiTranscriptionRequest(
        apiKey,
        $"church-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{command.Delay}",
        options);

    Console.WriteLine(
        $"OpenAI gpt-live-transcribe 시작: delay={command.Delay}, " +
        $"입력={Path.GetFullPath(command.PcmPath)}");
    TranscriptionRunResult result;
    try
    {
        try
        {
            result = await provider.TranscribeAsync(
                pcm,
                request,
                sink,
                cancellationToken);
        }
        catch (TranscriptionRunFailedException exception)
        {
            await WriteJsonAsync(
                metricsPath,
                exception.PartialResult,
                CancellationToken.None);
            throw;
        }
    }
    finally
    {
        await sink.CompleteAsync(CancellationToken.None);
    }

    await WriteJsonAsync(metricsPath, result, cancellationToken);
    if (!result.Completed)
    {
        throw new InvalidOperationException(
            "연결이 복구되었지만 오디오 연속성을 보장할 수 없어 실행을 불완전 처리했습니다.");
    }

    Console.WriteLine($"\n완료: {Path.GetFullPath(command.OutputDirectory)}");
    Console.WriteLine($"재접속 횟수: {result.ReconnectCount}");
}

static async Task RunEvaluationAsync(
    EvaluateCommandOptions command,
    CancellationToken cancellationToken)
{
    var reference = await File.ReadAllTextAsync(command.ReferencePath, cancellationToken);
    var hypothesis = await File.ReadAllTextAsync(command.HypothesisPath, cancellationToken);
    var keywords = await ReadKeywordFileAsync(command.KeywordsPath, cancellationToken);
    var runtimeJson = await File.ReadAllTextAsync(
        command.RuntimeMetricsPath,
        cancellationToken);
    var runtime = JsonSerializer.Deserialize<TranscriptionRunResult>(
        runtimeJson,
        JsonOptions()) ?? throw new InvalidDataException("런타임 메트릭 JSON이 비어 있습니다.");
    var metrics = runtime.Metrics ??
        throw new InvalidDataException("런타임 메트릭에 지연 샘플 정보가 없습니다.");
    var report = PocEvaluationReport.Create(
        reference,
        hypothesis,
        keywords,
        metrics,
        runtime.ReconnectCount);

    await WriteJsonAsync(command.OutputPath, report, cancellationToken);
    Console.WriteLine($"CER: {report.CharacterErrorRate:P2}");
    Console.WriteLine($"핵심 용어 정확도: {report.KeywordAccuracy:P2}");
    Console.WriteLine($"핵심 용어 정답 표본 수: {report.KeywordExpectedOccurrenceCount}");
    Console.WriteLine($"입력 길이: {report.AudioDurationMs / 1000d:F1}초 (15분 기준 통과: {report.DurationPassed})");
    Console.WriteLine($"자동 기준 통과: {report.AutomatedCriteriaPassed}");
    Console.WriteLine("음악·침묵 환각 여부는 events.jsonl과 원본 음원을 대조해 수동 확인해야 합니다.");
}

static async Task<string[]> ReadKeywordFileAsync(
    string path,
    CancellationToken cancellationToken)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException("키워드 파일을 찾을 수 없습니다.", path);
    }

    var lines = await File.ReadAllLinesAsync(path, cancellationToken);
    return lines
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

static StreamWriter CreateNewUtf8Writer(string path)
{
    var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static async Task WriteJsonAsync<T>(
    string path,
    T value,
    CancellationToken cancellationToken)
{
    var parent = Path.GetDirectoryName(Path.GetFullPath(path));
    if (parent is not null)
    {
        Directory.CreateDirectory(parent);
    }

    await using var stream = new FileStream(
        path,
        FileMode.Create,
        FileAccess.Write,
        FileShare.Read,
        bufferSize: 16 * 1024,
        useAsync: true);
    await JsonSerializer.SerializeAsync(
        stream,
        value,
        JsonOptions(),
        cancellationToken);
}

static JsonSerializerOptions JsonOptions() => new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};

static void WarnIfDiskSpaceIsLow(string outputDirectory)
{
    var root = Path.GetPathRoot(Path.GetFullPath(outputDirectory));
    if (root is null)
    {
        return;
    }

    var drive = new DriveInfo(root);
    const long warningThresholdBytes = 10L * 1024 * 1024 * 1024;
    if (drive.AvailableFreeSpace < warningThresholdBytes)
    {
        Console.Error.WriteLine(
            $"경고: 출력 드라이브의 여유 공간이 10GB 미만입니다 " +
            $"({drive.AvailableFreeSpace / 1024d / 1024d / 1024d:F1}GB). 자동 삭제는 하지 않습니다.");
    }
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        ChurchSubtitle OpenAI 실시간 한국어 자막 PoC

        전사:
          ChurchSubtitle.Cli transcribe --pcm <24k-mono-s16le.pcm> --delay <low|medium|high> --output <directory> [--keywords <file>] [--prompt <text>]

        평가:
          ChurchSubtitle.Cli evaluate --reference <교정본.txt> --hypothesis <final.txt> --keywords <file> --runtime-metrics <runtime-metrics.json> --output <evaluation.json>

        API 키:
          OPENAI_API_KEY 환경변수로만 설정합니다.
        """);
}

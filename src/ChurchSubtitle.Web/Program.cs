using System.Net;
using ChurchSubtitle.Web.Endpoints;
using ChurchSubtitle.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var repositoryRoot = FindRepositoryRoot(
    builder.Environment.ContentRootPath,
    Directory.GetCurrentDirectory());
const long ExpectedPcmLength = 70_368_000;
var pcmPath = Path.Combine(
    repositoryRoot,
    "data",
    "poc-source-full",
    "service-full-24k-mono-s16le.pcm");
var pcmFile = new FileInfo(pcmPath);
if (!pcmFile.Exists)
{
    throw new InvalidOperationException(
        $"테스트 PCM 파일을 찾을 수 없습니다: {pcmPath}");
}

if (pcmFile.Length != ExpectedPcmLength)
{
    throw new InvalidOperationException(
        $"테스트 PCM 파일은 정확히 {ExpectedPcmLength:N0}바이트여야 합니다: {pcmPath}");
}

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException(
        "OPENAI_API_KEY 환경변수가 필요합니다. 키는 서버 환경에서만 설정하세요.");
}

var keywords = ReadKeywords(Path.Combine(repositoryRoot, "config", "keywords.txt"));
builder.Configuration["AllowedHosts"] = "127.0.0.1;localhost;[::1]";
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(5287));
builder.Services.AddSingleton<SingleSessionGate>();
builder.Services.AddSingleton<ITranscriptionSessionRunner>(
    new OpenAiTranscriptionSessionRunner(apiKey, keywords));
builder.Services.AddSingleton(new CaptionEndpointOptions(
    StartTimeout: TimeSpan.FromSeconds(5),
    AudioIdleTimeout: TimeSpan.FromSeconds(15),
    SendTimeout: TimeSpan.FromSeconds(5),
    CloseTimeout: TimeSpan.FromSeconds(2),
    AllowedOrigins: new HashSet<string>(StringComparer.Ordinal)
    {
        "http://127.0.0.1:5287",
        "http://localhost:5287",
        "http://[::1]:5287"
    }));

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is { } remoteAddress &&
        !IPAddress.IsLoopback(remoteAddress))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new { error = "이 서버는 로컬 연결만 허용합니다." },
            context.RequestAborted);
        return;
    }

    await next(context);
});
app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl =
            "no-store, no-cache, must-revalidate";
        context.Context.Response.Headers.Pragma = "no-cache";
        context.Context.Response.Headers.Expires = "0";
    }
});

app.Map("/ws/captions", CaptionWebSocketEndpoint.HandleAsync);
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/test-assets/service.pcm", () =>
    Results.File(
        pcmPath,
        contentType: "application/octet-stream",
        enableRangeProcessing: true));

app.Run();

static string FindRepositoryRoot(params string[] startingPaths)
{
    foreach (var startingPath in startingPaths)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startingPath));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ChurchSubtitle.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException(
        "ChurchSubtitle.sln을 기준으로 저장소 루트를 찾을 수 없습니다.");
}

static string[] ReadKeywords(string path)
{
    return File.ReadAllLines(path)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

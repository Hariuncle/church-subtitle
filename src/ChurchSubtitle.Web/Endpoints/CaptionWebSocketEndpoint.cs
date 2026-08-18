using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text;
using ChurchSubtitle.Core.Streaming;
using ChurchSubtitle.Web.Protocol;
using ChurchSubtitle.Web.Services;

namespace ChurchSubtitle.Web.Endpoints;

public static class CaptionWebSocketEndpoint
{
    private const int MaximumCommandBytes = 4096;
    private const int MaximumAudioFrameBytes = 96_000;
    private const string InvalidRequestStatus =
        "유효하지 않은 자막 요청입니다. 연결을 다시 시작하세요.";
    private const string ProviderErrorStatus =
        "자막 처리 중 오류가 발생했습니다. 잠시 후 다시 시도하세요.";

    public static async Task HandleAsync(
        HttpContext context,
        SingleSessionGate sessionGate,
        ITranscriptionSessionRunner runner,
        CaptionEndpointOptions options)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new { error = "WebSocket 연결이 필요합니다." },
                context.RequestAborted);
            return;
        }

        if (!HasAllowedOrigin(context, options.AllowedOrigins))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { error = "WebSocket Origin이 허용되지 않습니다." },
                context.RequestAborted);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var sessionId = $"web-{Guid.NewGuid():N}";
        var sink = new WebSocketCaptionSink(
            socket,
            sessionId,
            options.SendTimeout);

        StartCaptionSocketCommand start;
        try
        {
            using var startCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted);
            startCancellation.CancelAfter(options.StartTimeout);
            SocketMessage? firstMessage;
            try
            {
                firstMessage = await ReceiveMessageAsync(
                    socket,
                    MaximumCommandBytes,
                    MaximumCommandBytes,
                    startCancellation.Token);
            }
            catch (OperationCanceledException) when (
                !context.RequestAborted.IsCancellationRequested)
            {
                throw new InvalidDataException("The start command timed out.");
            }

            if (firstMessage is null)
            {
                await CloseIfPossibleAsync(
                    socket,
                    WebSocketCloseStatus.NormalClosure,
                    "client closed",
                    options.CloseTimeout,
                    CancellationToken.None);
                return;
            }

            if (firstMessage.Value.Type != WebSocketMessageType.Text)
            {
                throw new InvalidDataException("The first message must be a start command.");
            }

            var command = CaptionSocketProtocol.Parse(
                Encoding.UTF8.GetString(firstMessage.Value.Payload));
            start = command as StartCaptionSocketCommand ??
                throw new InvalidDataException("The first message must be a start command.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (InvalidDataException)
        {
            await SendErrorAndCloseAsync(
                socket,
                sink,
                InvalidRequestStatus,
                WebSocketCloseStatus.InvalidPayloadData,
                "invalid caption request",
                options.CloseTimeout);
            return;
        }

        using var lease = sessionGate.TryEnter();
        if (lease is null)
        {
            await SendErrorAndCloseAsync(
                socket,
                sink,
                "다른 자막 세션이 실행 중입니다.",
                WebSocketCloseStatus.PolicyViolation,
                "caption session busy",
                options.CloseTimeout);
            return;
        }

        await RunSessionAsync(
            socket,
            sink,
            runner,
            sessionId,
            start.Delay,
            options.AudioIdleTimeout,
            options.CloseTimeout,
            context.RequestAborted);
    }

    public static string? ValidateBinaryFrameLength(int length) =>
        length switch
        {
            <= 0 => "PCM 프레임은 비어 있을 수 없습니다.",
            > MaximumAudioFrameBytes => "PCM 프레임이 허용 크기를 초과했습니다.",
            _ when (length & 1) != 0 => "PCM 프레임은 16비트 샘플 경계에 맞아야 합니다.",
            _ => null
        };

    private static async Task RunSessionAsync(
        WebSocket socket,
        WebSocketCaptionSink sink,
        ITranscriptionSessionRunner runner,
        string sessionId,
        string delay,
        TimeSpan audioIdleTimeout,
        TimeSpan closeTimeout,
        CancellationToken requestCancellationToken)
    {
        var pipe = new Pipe();
        await using var pcmStream = new FixedChunkReadStream(
            pipe.Reader.AsStream(leaveOpen: false));
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellationToken);
        var receiveTask = ReceiveAudioAsync(
            socket,
            pipe.Writer,
            audioIdleTimeout,
            sessionCancellation.Token);
        Task<TranscriptionRunResult> runnerTask;
        try
        {
            runnerTask = runner.RunAsync(
                pcmStream,
                sessionId,
                delay,
                sink,
                sessionCancellation.Token);
        }
        catch (Exception exception)
        {
            runnerTask = Task.FromException<TranscriptionRunResult>(exception);
        }
        var writerCompleted = false;

        try
        {
            var firstCompleted = await Task.WhenAny(receiveTask, runnerTask);
            if (firstCompleted == runnerTask)
            {
                sessionCancellation.Cancel();
                await CompleteWriterAsync(pipe.Writer);
                writerCompleted = true;
                await ObserveAsync(receiveTask);
            }
            else
            {
                bool receivedEndCommand;
                try
                {
                    receivedEndCommand = await receiveTask;
                }
                catch (InvalidDataException)
                {
                    sessionCancellation.Cancel();
                    await CompleteWriterAsync(pipe.Writer);
                    writerCompleted = true;
                    await ObserveAsync(runnerTask);
                    await SendErrorAndCloseAsync(
                        socket,
                        sink,
                        InvalidRequestStatus,
                        WebSocketCloseStatus.InvalidPayloadData,
                        "invalid caption payload",
                        closeTimeout);
                    return;
                }

                await CompleteWriterAsync(pipe.Writer);
                writerCompleted = true;
                if (!receivedEndCommand)
                {
                    sessionCancellation.Cancel();
                    await ObserveAsync(runnerTask);
                    await CloseIfPossibleAsync(
                        socket,
                        WebSocketCloseStatus.NormalClosure,
                        "client disconnected",
                        closeTimeout,
                        CancellationToken.None);
                    return;
                }
            }

            var result = await runnerTask;
            if (!result.Completed)
            {
                await SendErrorAndCloseAsync(
                    socket,
                    sink,
                    "자막 연결을 복구했지만 오디오 연속성을 확인할 수 없습니다.",
                    WebSocketCloseStatus.InternalServerError,
                    "caption stream incomplete",
                    closeTimeout);
                return;
            }

            await TryPublishStatusAsync(
                sink,
                "자막 처리가 완료되었습니다.",
                isError: false,
                CancellationToken.None);
            await CloseIfPossibleAsync(
                socket,
                WebSocketCloseStatus.NormalClosure,
                "caption complete",
                closeTimeout,
                CancellationToken.None);
        }
        catch (OperationCanceledException) when (
            requestCancellationToken.IsCancellationRequested ||
            socket.State is WebSocketState.CloseReceived or WebSocketState.Closed or WebSocketState.Aborted)
        {
            // Expected client disconnect or host shutdown.
        }
        catch (Exception)
        {
            sessionCancellation.Cancel();
            await ObserveAsync(receiveTask);
            await SendErrorAndCloseAsync(
                socket,
                sink,
                ProviderErrorStatus,
                WebSocketCloseStatus.InternalServerError,
                "caption processing failed",
                closeTimeout);
        }
        finally
        {
            sessionCancellation.Cancel();
            if (!writerCompleted)
            {
                await CompleteWriterAsync(pipe.Writer);
            }

            await ObserveAsync(receiveTask);
            await ObserveAsync(runnerTask);
        }
    }

    private static async Task<bool> ReceiveAudioAsync(
        WebSocket socket,
        PipeWriter writer,
        TimeSpan audioIdleTimeout,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var idleCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            idleCancellation.CancelAfter(audioIdleTimeout);
            SocketMessage? message;
            try
            {
                message = await ReceiveMessageAsync(
                    socket,
                    MaximumAudioFrameBytes,
                    MaximumCommandBytes,
                    idleCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidDataException("The audio stream was idle for too long.");
            }

            if (message is null)
            {
                return false;
            }

            if (message.Value.Type == WebSocketMessageType.Binary)
            {
                var error = ValidateBinaryFrameLength(message.Value.Payload.Length);
                if (error is not null)
                {
                    throw new InvalidDataException(error);
                }

                await writer.WriteAsync(message.Value.Payload, cancellationToken);
                continue;
            }

            if (message.Value.Type != WebSocketMessageType.Text)
            {
                throw new InvalidDataException("Expected an end command or a PCM frame.");
            }

            var command = CaptionSocketProtocol.Parse(
                Encoding.UTF8.GetString(message.Value.Payload));
            if (command is not EndCaptionSocketCommand)
            {
                throw new InvalidDataException("Only an end command is allowed after start.");
            }

            return true;
        }
    }

    private static async Task<SocketMessage?> ReceiveMessageAsync(
        WebSocket socket,
        int maximumBytes,
        int maximumTextBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(16 * 1024, maximumBytes + 1)];
        using var message = new MemoryStream();
        WebSocketMessageType? messageType = null;

        while (true)
        {
            var result = await socket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            messageType ??= result.MessageType;
            if (messageType != result.MessageType)
            {
                throw new InvalidDataException("A fragmented message changed type.");
            }

            var messageMaximumBytes = result.MessageType == WebSocketMessageType.Text
                ? maximumTextBytes
                : maximumBytes;
            if (message.Length + result.Count > messageMaximumBytes)
            {
                throw new InvalidDataException("Caption socket message is too large.");
            }

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return new SocketMessage(messageType.Value, message.ToArray());
            }
        }
    }

    private static bool HasAllowedOrigin(
        HttpContext context,
        IReadOnlySet<string> allowedOrigins)
    {
        if (!context.Request.Headers.TryGetValue("Origin", out var origins))
        {
            return true;
        }

        var expectedOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
        return origins.Count == 1 &&
            origins[0] is { } origin &&
            allowedOrigins.Contains(origin) &&
            string.Equals(origin, expectedOrigin, StringComparison.Ordinal);
    }

    private static async Task SendErrorAndCloseAsync(
        WebSocket socket,
        WebSocketCaptionSink sink,
        string text,
        WebSocketCloseStatus status,
        string description,
        TimeSpan closeTimeout)
    {
        await TryPublishStatusAsync(
            sink,
            text,
            isError: true,
            CancellationToken.None);
        await CloseIfPossibleAsync(
            socket,
            status,
            description,
            closeTimeout,
            CancellationToken.None);
    }

    private static async Task TryPublishStatusAsync(
        WebSocketCaptionSink sink,
        string text,
        bool isError,
        CancellationToken cancellationToken)
    {
        try
        {
            await sink.PublishStatusAsync(text, isError, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The peer disconnected before the safe status could be delivered.
        }
    }

    private static async Task CloseIfPossibleAsync(
        WebSocket socket,
        WebSocketCloseStatus status,
        string description,
        TimeSpan closeTimeout,
        CancellationToken cancellationToken)
    {
        using var closeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        closeCancellation.CancelAfter(closeTimeout);
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                var closeTask = socket.CloseOutputAsync(
                    status,
                    description,
                    closeCancellation.Token);
                await closeTask.WaitAsync(closeCancellation.Token);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Closing is best-effort once the transport is no longer usable.
        }
    }

    private static async Task CompleteWriterAsync(PipeWriter writer)
    {
        try
        {
            await writer.CompleteAsync();
        }
        catch (InvalidOperationException)
        {
            // A concurrent failure path may already have completed the writer.
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // The controlling flow reports a fixed safe status; this only observes
            // counterpart task failures so they cannot become unobserved exceptions.
        }
    }

    private readonly record struct SocketMessage(
        WebSocketMessageType Type,
        byte[] Payload);
}

public sealed record CaptionEndpointOptions
{
    public CaptionEndpointOptions(
        TimeSpan StartTimeout,
        TimeSpan AudioIdleTimeout,
        TimeSpan SendTimeout,
        TimeSpan CloseTimeout,
        IReadOnlySet<string> AllowedOrigins)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            StartTimeout,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            AudioIdleTimeout,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            SendTimeout,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            CloseTimeout,
            TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(AllowedOrigins);
        if (AllowedOrigins.Count == 0)
        {
            throw new ArgumentException(
                "At least one browser origin must be allowed.",
                nameof(AllowedOrigins));
        }

        this.StartTimeout = StartTimeout;
        this.AudioIdleTimeout = AudioIdleTimeout;
        this.SendTimeout = SendTimeout;
        this.CloseTimeout = CloseTimeout;
        this.AllowedOrigins = new HashSet<string>(
            AllowedOrigins,
            StringComparer.Ordinal);
    }

    public TimeSpan StartTimeout { get; }

    public TimeSpan AudioIdleTimeout { get; }

    public TimeSpan SendTimeout { get; }

    public TimeSpan CloseTimeout { get; }

    public IReadOnlySet<string> AllowedOrigins { get; }
}

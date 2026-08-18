using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ChurchSubtitle.Core.Captions;
using ChurchSubtitle.Core.Streaming;
using ChurchSubtitle.Web.Endpoints;
using ChurchSubtitle.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ChurchSubtitle.Core.Tests;

public sealed class CaptionWebSocketLifecycleTests
{
    [Fact]
    public async Task FragmentedStartAudioAndEnd_DeliverExactPipeBytesAndComplete()
    {
        byte[]? receivedAudio = null;
        var runner = new DelegateRunner(async (audio, _, _, _, cancellationToken) =>
        {
            using var destination = new MemoryStream();
            await audio.CopyToAsync(destination, cancellationToken);
            receivedAudio = destination.ToArray();
            return CompletedResult();
        });
        await using var host = CreateHost(runner);
        using var socket = await ConnectAsync(host.Server, origin: "http://localhost");

        await SendFragmentsAsync(socket, WebSocketMessageType.Text, "{\"type\":\"start\",", "\"delay\":\"low\"}");
        await SendFragmentsAsync(socket, WebSocketMessageType.Binary, [1, 2], [3, 4, 5, 6]);
        await SendFragmentsAsync(socket, WebSocketMessageType.Text, "{\"type\":", "\"end\"}");

        var response = await ReceiveUntilCloseAsync(socket);
        Assert.Equal([1, 2, 3, 4, 5, 6], receivedAudio);
        Assert.Contains(response.Messages, message =>
            message.GetProperty("state").GetString() == "status" &&
            message.GetProperty("provider").GetString() == "server");
        Assert.Equal(WebSocketCloseStatus.NormalClosure, response.CloseStatus);
    }

    [Fact]
    public async Task Binary_audio_session_continues_until_explicit_end_command()
    {
        var firstAudioObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new MemoryStream();
        var runner = new DelegateRunner(async (audio, _, _, _, cancellationToken) =>
        {
            var firstFrame = new byte[4];
            var firstRead = await audio.ReadAsync(firstFrame, cancellationToken);
            await received.WriteAsync(firstFrame.AsMemory(0, firstRead), cancellationToken);
            firstAudioObserved.TrySetResult();
            await audio.CopyToAsync(received, cancellationToken);
            runnerCompleted.TrySetResult();
            return CompletedResult();
        });
        await using var host = CreateHost(runner);
        using var socket = await ConnectAsync(host.Server);

        await SendTextAsync(socket, "{\"type\":\"start\",\"delay\":\"low\"}");
        await socket.SendAsync(
            new byte[] { 1, 2, 3, 4 },
            WebSocketMessageType.Binary,
            endOfMessage: true,
            CancellationToken.None);
        await firstAudioObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(runnerCompleted.Task.IsCompleted);
        await socket.SendAsync(
            new byte[] { 5, 6, 7, 8 },
            WebSocketMessageType.Binary,
            endOfMessage: true,
            CancellationToken.None);
        Assert.False(runnerCompleted.Task.IsCompleted);

        await SendTextAsync(socket, "{\"type\":\"end\"}");
        var response = await ReceiveUntilCloseAsync(socket);
        await runnerCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], received.ToArray());
        Assert.Equal(WebSocketCloseStatus.NormalClosure, response.CloseStatus);
    }

    [Fact]
    public async Task BinaryBeforeStart_ReturnsSafeInvalidPayloadAndDoesNotAcquireGate()
    {
        var gate = new SingleSessionGate();
        var runner = new DelegateRunner((_, _, _, _, _) => Task.FromResult(CompletedResult()));
        await using var host = CreateHost(runner, gate);
        using var socket = await ConnectAsync(host.Server);

        await socket.SendAsync(
            new byte[] { 1, 2 },
            WebSocketMessageType.Binary,
            endOfMessage: true,
            CancellationToken.None);

        var response = await ReceiveUntilCloseAsync(socket);
        AssertSafeError(response, WebSocketCloseStatus.InvalidPayloadData);
        using var lease = gate.TryEnter();
        Assert.NotNull(lease);
        Assert.Equal(0, runner.InvocationCount);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(96_002)]
    public async Task InvalidAggregateAudioLength_ReturnsInvalidPayload(int length)
    {
        var runner = new DelegateRunner(async (audio, _, _, _, cancellationToken) =>
        {
            await audio.CopyToAsync(Stream.Null, cancellationToken);
            return CompletedResult();
        });
        await using var host = CreateHost(runner);
        using var socket = await ConnectAsync(host.Server);
        await SendTextAsync(socket, "{\"type\":\"start\",\"delay\":\"medium\"}");

        var bytes = new byte[length];
        var firstLength = length / 2;
        await socket.SendAsync(
            bytes.AsMemory(0, firstLength),
            WebSocketMessageType.Binary,
            endOfMessage: false,
            CancellationToken.None);
        await socket.SendAsync(
            bytes.AsMemory(firstLength),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            CancellationToken.None);

        var response = await ReceiveUntilCloseAsync(socket);
        AssertSafeError(response, WebSocketCloseStatus.InvalidPayloadData);
    }

    [Fact]
    public async Task ProviderFailure_CancelsBlockedReceiverAndReturnsOnlySafeError()
    {
        var runner = new DelegateRunner((_, _, _, _, _) =>
            throw new InvalidOperationException("secret provider detail"));
        await using var host = CreateHost(runner);
        using var socket = await ConnectAsync(host.Server);
        await SendTextAsync(socket, "{\"type\":\"start\",\"delay\":\"low\"}");

        var response = await ReceiveUntilCloseAsync(socket);

        AssertSafeError(response, WebSocketCloseStatus.InternalServerError);
        Assert.DoesNotContain("secret provider detail", response.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disconnect_CancelsRunnerAndReleasesGate()
    {
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new SingleSessionGate();
        var runner = new DelegateRunner(async (_, _, _, _, cancellationToken) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return CompletedResult();
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                throw;
            }
        });
        await using var host = CreateHost(runner, gate);
        var socket = await ConnectAsync(host.Server);
        await SendTextAsync(socket, "{\"type\":\"start\",\"delay\":\"low\"}");

        socket.Abort();
        socket.Dispose();

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        IDisposable? lease = null;
        await PollUntilAsync(() => (lease = gate.TryEnter()) is not null);
        lease!.Dispose();
    }

    [Fact]
    public async Task MismatchedBrowserOrigin_IsRejectedBeforeUpgrade()
    {
        var runner = new DelegateRunner((_, _, _, _, _) => Task.FromResult(CompletedResult()));
        await using var host = CreateHost(runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ConnectAsync(host.Server, origin: "https://attacker.example"));

        Assert.Contains("403", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, runner.InvocationCount);
    }

    [Fact]
    public async Task AttackerHostWithMatchingAttackerOrigin_IsRejectedBeforeUpgrade()
    {
        var runner = new DelegateRunner((_, _, _, _, _) => Task.FromResult(CompletedResult()));
        await using var host = CreateHost(
            runner,
            options: ProductionOptions());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConnectAsync(
                host.Server,
                origin: "http://attacker.example:5287",
                requestUri: new Uri("ws://attacker.example:5287/ws/captions")));

        Assert.Contains("403", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, runner.InvocationCount);
    }

    [Fact]
    public async Task AllowedCrossAliasOrigin_IsRejectedWhenItDoesNotMatchRequestHost()
    {
        var runner = new DelegateRunner((_, _, _, _, _) => Task.FromResult(CompletedResult()));
        await using var host = CreateHost(
            runner,
            options: ProductionOptions());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConnectAsync(
                host.Server,
                origin: "http://localhost:5287",
                requestUri: new Uri("ws://127.0.0.1:5287/ws/captions")));

        Assert.Contains("403", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, runner.InvocationCount);
    }

    [Theory]
    [InlineData("ws://localhost:5287/ws/captions", "http://localhost:5287")]
    [InlineData("ws://127.0.0.1:5287/ws/captions", "http://127.0.0.1:5287")]
    [InlineData("ws://[::1]:5287/ws/captions", "http://[::1]:5287")]
    public async Task FixedLocalBrowserOrigins_AreAllowed(string socketUri, string origin)
    {
        var runner = new DelegateRunner(async (audio, _, _, _, cancellationToken) =>
        {
            await audio.CopyToAsync(Stream.Null, cancellationToken);
            return CompletedResult();
        });
        await using var host = CreateHost(
            runner,
            options: ProductionOptions());
        using var socket = await ConnectAsync(
            host.Server,
            origin,
            new Uri(socketUri));
        await SendTextAsync(socket, "{\"type\":\"start\",\"delay\":\"low\"}");
        await SendTextAsync(socket, "{\"type\":\"end\"}");

        var response = await ReceiveUntilCloseAsync(socket);

        Assert.Equal(WebSocketCloseStatus.NormalClosure, response.CloseStatus);
        Assert.Equal(1, runner.InvocationCount);
    }

    [Fact]
    public async Task TransportTimeoutFailure_ReleasesGateWithSafeServerError()
    {
        var gate = new SingleSessionGate();
        var runner = new DelegateRunner((_, _, _, _, _) =>
            Task.FromException<TranscriptionRunResult>(
                new TimeoutException("Caption WebSocket send timed out.")));
        await using var host = CreateHost(runner, gate);
        using var socket = await ConnectAsync(host.Server);
        await SendTextAsync(socket, "{\"type\":\"start\",\"delay\":\"low\"}");

        var response = await ReceiveUntilCloseAsync(socket);

        AssertSafeError(response, WebSocketCloseStatus.InternalServerError);
        IDisposable? lease = null;
        await PollUntilAsync(() => (lease = gate.TryEnter()) is not null);
        lease!.Dispose();
    }

    [Fact]
    public async Task StartTimeout_ReturnsSafeInvalidPayloadWithoutHoldingGate()
    {
        var gate = new SingleSessionGate();
        var runner = new DelegateRunner((_, _, _, _, _) => Task.FromResult(CompletedResult()));
        await using var host = CreateHost(
            runner,
            gate,
            new CaptionEndpointOptions(
                StartTimeout: TimeSpan.FromMilliseconds(500),
                AudioIdleTimeout: TimeSpan.FromSeconds(5),
                SendTimeout: TimeSpan.FromSeconds(1),
                CloseTimeout: TimeSpan.FromSeconds(1),
                AllowedOrigins: new HashSet<string>(StringComparer.Ordinal)
                {
                    "http://localhost"
                }));
        using var socket = await ConnectAsync(host.Server);
        await Task.Delay(50);
        using (var leaseWhileWaitingForStart = gate.TryEnter())
        {
            Assert.NotNull(leaseWhileWaitingForStart);
        }

        var response = await ReceiveUntilCloseAsync(socket);

        AssertSafeError(response, WebSocketCloseStatus.InvalidPayloadData);
        using var lease = gate.TryEnter();
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task AudioIdleTimeout_CancelsRunnerAndReturnsSafeInvalidPayload()
    {
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new DelegateRunner(async (_, _, _, _, cancellationToken) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return CompletedResult();
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                throw;
            }
        });
        await using var host = CreateHost(
            runner,
            options: new CaptionEndpointOptions(
                StartTimeout: TimeSpan.FromSeconds(2),
                AudioIdleTimeout: TimeSpan.FromMilliseconds(50),
                SendTimeout: TimeSpan.FromSeconds(1),
                CloseTimeout: TimeSpan.FromSeconds(1),
                AllowedOrigins: new HashSet<string>(StringComparer.Ordinal)
                {
                    "http://localhost"
                }));
        using var socket = await ConnectAsync(host.Server);
        await SendTextAsync(socket, "{\"type\":\"start\",\"delay\":\"low\"}");

        var response = await ReceiveUntilCloseAsync(socket);

        AssertSafeError(response, WebSocketCloseStatus.InvalidPayloadData);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CloseBeforeStart_IsAcknowledgedWithoutAcquiringGate()
    {
        var gate = new SingleSessionGate();
        var runner = new DelegateRunner((_, _, _, _, _) => Task.FromResult(CompletedResult()));
        await using var host = CreateHost(runner, gate);
        using var socket = await ConnectAsync(host.Server);

        await socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "client close",
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(WebSocketState.Closed, socket.State);
        using var lease = gate.TryEnter();
        Assert.NotNull(lease);
    }

    private static EndpointTestHost CreateHost(
        ITranscriptionSessionRunner runner,
        SingleSessionGate? gate = null,
        CaptionEndpointOptions? options = null)
    {
        gate ??= new SingleSessionGate();
        options ??= new CaptionEndpointOptions(
            StartTimeout: TimeSpan.FromSeconds(2),
            AudioIdleTimeout: TimeSpan.FromSeconds(2),
            SendTimeout: TimeSpan.FromSeconds(1),
            CloseTimeout: TimeSpan.FromSeconds(1),
            AllowedOrigins: new HashSet<string>(StringComparer.Ordinal)
            {
                "http://localhost"
            });
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(gate);
                services.AddSingleton(runner);
                services.AddSingleton(options);
            })
            .Configure(app =>
            {
                app.UseWebSockets();
                app.Run(context => CaptionWebSocketEndpoint.HandleAsync(
                    context,
                    context.RequestServices.GetRequiredService<SingleSessionGate>(),
                    context.RequestServices.GetRequiredService<ITranscriptionSessionRunner>(),
                    context.RequestServices.GetRequiredService<CaptionEndpointOptions>()));
            });
        return new EndpointTestHost(new TestServer(builder));
    }

    private static async Task<WebSocket> ConnectAsync(
        TestServer server,
        string? origin = null,
        Uri? requestUri = null)
    {
        var client = server.CreateWebSocketClient();
        if (origin is not null)
        {
            client.ConfigureRequest = request => request.Headers["Origin"] = origin;
        }

        return await client.ConnectAsync(
            requestUri ?? new Uri("ws://localhost/ws/captions"),
            CancellationToken.None);
    }

    private static CaptionEndpointOptions ProductionOptions() => new(
        StartTimeout: TimeSpan.FromSeconds(2),
        AudioIdleTimeout: TimeSpan.FromSeconds(2),
        SendTimeout: TimeSpan.FromSeconds(1),
        CloseTimeout: TimeSpan.FromSeconds(1),
        AllowedOrigins: new HashSet<string>(StringComparer.Ordinal)
        {
            "http://127.0.0.1:5287",
            "http://localhost:5287",
            "http://[::1]:5287"
        });

    private static Task SendTextAsync(WebSocket socket, string text) =>
        socket.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);

    private static async Task SendFragmentsAsync(
        WebSocket socket,
        WebSocketMessageType messageType,
        params string[] fragments)
    {
        var byteFragments = fragments.Select(Encoding.UTF8.GetBytes).ToArray();
        await SendFragmentsAsync(socket, messageType, byteFragments);
    }

    private static async Task SendFragmentsAsync(
        WebSocket socket,
        WebSocketMessageType messageType,
        params byte[][] fragments)
    {
        for (var index = 0; index < fragments.Length; index++)
        {
            await socket.SendAsync(
                fragments[index],
                messageType,
                endOfMessage: index == fragments.Length - 1,
                CancellationToken.None);
        }
    }

    private static async Task<SocketResponse> ReceiveUntilCloseAsync(WebSocket socket)
    {
        var messages = new List<JsonElement>();
        var rawText = new StringBuilder();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(2));
            if (result.MessageType == WebSocketMessageType.Close)
            {
                var closeStatus = result.CloseStatus;
                return new SocketResponse(messages, rawText.ToString(), closeStatus);
            }

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            rawText.Append(json);
            messages.Add(JsonDocument.Parse(json).RootElement.Clone());
        }
    }

    private static void AssertSafeError(
        SocketResponse response,
        WebSocketCloseStatus expectedCloseStatus)
    {
        var status = Assert.Single(response.Messages);
        Assert.Equal("status", status.GetProperty("state").GetString());
        Assert.Equal("server:error", status.GetProperty("provider").GetString());
        Assert.Equal(expectedCloseStatus, response.CloseStatus);
    }

    private static async Task PollUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static TranscriptionRunResult CompletedResult() => new(
        Completed: true,
        ReconnectCount: 0);

    private sealed record SocketResponse(
        IReadOnlyList<JsonElement> Messages,
        string RawText,
        WebSocketCloseStatus? CloseStatus);

    private sealed class DelegateRunner(
        Func<Stream, string, string, ICaptionSink, CancellationToken,
            Task<TranscriptionRunResult>> run) : ITranscriptionSessionRunner
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public Task<TranscriptionRunResult> RunAsync(
            Stream pcmAudio,
            string sessionId,
            string delay,
            ICaptionSink sink,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            return run(pcmAudio, sessionId, delay, sink, cancellationToken);
        }
    }

    private sealed class EndpointTestHost(TestServer server) : IAsyncDisposable
    {
        public TestServer Server { get; } = server;

        public ValueTask DisposeAsync()
        {
            Server.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

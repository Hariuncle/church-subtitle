using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ChurchSubtitle.Core.Captions;
using ChurchSubtitle.Web.Services;

namespace ChurchSubtitle.Core.Tests;

public sealed class WebSocketCaptionSinkTests
{
    [Fact]
    public void Serialize_UsesCamelCaseStringStateAndOmitsAudioStartMs()
    {
        var update = new CaptionUpdate(
            "session-1",
            7,
            "line-2",
            3,
            "예배 자막",
            CaptionState.Final,
            "openai",
            new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero),
            AudioStartMs: 1234,
            SegmentOrdinal: 2,
            SegmentStartMs: 20_000);

        using var document = JsonDocument.Parse(WebSocketCaptionSink.Serialize(update));
        var root = document.RootElement;

        Assert.Equal("session-1", root.GetProperty("sessionId").GetString());
        Assert.Equal(7, root.GetProperty("sequence").GetInt64());
        Assert.Equal("final", root.GetProperty("state").GetString());
        Assert.False(root.TryGetProperty("SessionId", out _));
        Assert.False(root.TryGetProperty("audioStartMs", out _));
        Assert.False(root.TryGetProperty("segmentOrdinal", out _));
        Assert.False(root.TryGetProperty("segmentStartMs", out _));
    }

    [Fact]
    public async Task PublishStatusAsync_UsesStatusStateAndDocumentedErrorProvider()
    {
        var socket = new RecordingWebSocket();
        var sink = new WebSocketCaptionSink(
            socket,
            "session-1",
            sendTimeout: TimeSpan.FromSeconds(1));

        await sink.PublishStatusAsync("처리 중 오류가 발생했습니다.", isError: true);

        using var document = JsonDocument.Parse(Assert.Single(socket.Messages));
        var root = document.RootElement;
        Assert.Equal("status", root.GetProperty("state").GetString());
        Assert.Equal("server:error", root.GetProperty("provider").GetString());
        Assert.Equal("처리 중 오류가 발생했습니다.", root.GetProperty("text").GetString());
    }

    [Fact]
    public async Task PublishAsync_WhenSocketSendNeverCompletes_TimesOutAndAbortsSocket()
    {
        var socket = new NeverCompletingSendWebSocket();
        var sink = new WebSocketCaptionSink(
            socket,
            "session-1",
            sendTimeout: TimeSpan.FromMilliseconds(50));
        var update = new CaptionUpdate(
            "session-1",
            1,
            "line-1",
            0,
            "caption",
            CaptionState.Partial,
            "fake",
            DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => sink.PublishAsync(update, CancellationToken.None).AsTask())
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("Caption WebSocket send timed out.", exception.Message);
        Assert.True(socket.WasAborted);
    }

    private sealed class RecordingWebSocket : WebSocket
    {
        public List<string> Messages { get; } = [];

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => WebSocketState.Open;

        public override string? SubProtocol => null;

        public override void Abort()
        {
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            Messages.Add(Encoding.UTF8.GetString(buffer));
            return Task.CompletedTask;
        }
    }

    private sealed class NeverCompletingSendWebSocket : WebSocket
    {
        public bool WasAborted { get; private set; }

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => WasAborted
            ? WebSocketState.Aborted
            : WebSocketState.Open;

        public override string? SubProtocol => null;

        public override void Abort() => WasAborted = true;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken) => new TaskCompletionSource().Task;
    }
}

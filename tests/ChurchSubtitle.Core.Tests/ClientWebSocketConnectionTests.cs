using System.Net.WebSockets;
using ChurchSubtitle.Core.OpenAi;

namespace ChurchSubtitle.Core.Tests;

public sealed class ClientWebSocketConnectionTests
{
    [Fact]
    public async Task Receive_rejects_a_fragmented_message_above_the_configured_limit()
    {
        var socket = new FragmentedMessageWebSocket(
            new byte[6],
            new byte[6]);
        await using var connection = new ClientWebSocketConnection(
            socket,
            new Uri("wss://example.test/realtime"),
            maxReceiveMessageBytes: 10);

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            connection.ReceiveTextAsync(CancellationToken.None));

        Assert.Contains("maximum", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FragmentedMessageWebSocket(params byte[][] fragments) : WebSocket
    {
        private readonly Queue<byte[]> _fragments = new(fragments);

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;

        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            var fragment = _fragments.Dequeue();
            fragment.CopyTo(buffer.Array!, buffer.Offset);
            return Task.FromResult(new WebSocketReceiveResult(
                fragment.Length,
                WebSocketMessageType.Text,
                endOfMessage: _fragments.Count == 0));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

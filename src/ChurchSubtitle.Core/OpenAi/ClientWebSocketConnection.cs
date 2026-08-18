using System.Net.WebSockets;
using System.Text;

namespace ChurchSubtitle.Core.OpenAi;

public sealed class ClientWebSocketConnectionFactory : IRealtimeConnectionFactory
{
    public static Uri Endpoint { get; } = new(
        "wss://api.openai.com/v1/realtime?intent=transcription");

    public IRealtimeConnection Create(string apiKey) =>
        new ClientWebSocketConnection(Endpoint, apiKey);
}

public sealed class ClientWebSocketConnection : IRealtimeConnection
{
    private const int DefaultMaxReceiveMessageBytes = 1024 * 1024;
    private readonly WebSocket _socket;
    private readonly int _maxReceiveMessageBytes;
    private readonly Func<CancellationToken, Task> _connect;

    public ClientWebSocketConnection(Uri endpoint, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var socket = CreateSocket(apiKey);
        _socket = socket;
        _maxReceiveMessageBytes = DefaultMaxReceiveMessageBytes;
        _connect = cancellationToken => socket.ConnectAsync(
            endpoint,
            cancellationToken);
    }

    public ClientWebSocketConnection(
        WebSocket socket,
        Uri endpoint,
        int maxReceiveMessageBytes)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            maxReceiveMessageBytes,
            0);

        _socket = socket;
        _maxReceiveMessageBytes = maxReceiveMessageBytes;
        _connect = _ => Task.CompletedTask;
    }

    public Task ConnectAsync(CancellationToken cancellationToken) =>
        _connect(cancellationToken);

    public Task SendTextAsync(string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return _socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType is WebSocketMessageType.Close)
            {
                return null;
            }

            if (message.Length + result.Count > _maxReceiveMessageBytes)
            {
                throw new InvalidDataException(
                    $"Realtime message exceeded the maximum {_maxReceiveMessageBytes} byte size.");
            }

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }

    private static ClientWebSocket CreateSocket(string apiKey)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        return socket;
    }
}

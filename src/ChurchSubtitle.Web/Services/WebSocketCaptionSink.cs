using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChurchSubtitle.Core.Captions;

namespace ChurchSubtitle.Web.Services;

/// <summary>
/// Sends caption updates through one serialized WebSocket writer. Status updates
/// use provider <c>server</c>; safe error statuses use <c>server:error</c>, which
/// lets clients style failures without adding an off-protocol payload shape.
/// Each send is bounded; a timeout aborts the transport and is surfaced to the
/// provider so the owning session can cancel its receive and cleanup paths.
/// </summary>
public sealed class WebSocketCaptionSink : ICaptionSink
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly WebSocket _socket;
    private readonly string _sessionId;
    private readonly TimeSpan _sendTimeout;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private long _sequence;

    public WebSocketCaptionSink(
        WebSocket socket,
        string sessionId,
        TimeSpan sendTimeout)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            sendTimeout,
            TimeSpan.Zero);

        _socket = socket;
        _sessionId = sessionId;
        _sendTimeout = sendTimeout;
    }

    public static string Serialize(CaptionUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return JsonSerializer.Serialize(update, SerializerOptions);
    }

    public ValueTask PublishAsync(
        CaptionUpdate update,
        CancellationToken cancellationToken)
    {
        ObserveSequence(update.Sequence);
        return SendAsync(update, cancellationToken);
    }

    public ValueTask PublishStatusAsync(
        string text,
        bool isError = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var sequence = Interlocked.Increment(ref _sequence);
        var update = new CaptionUpdate(
            _sessionId,
            sequence,
            $"status-{sequence}",
            Revision: 0,
            text,
            CaptionState.Status,
            isError ? "server:error" : "server",
            DateTimeOffset.UtcNow);
        return SendAsync(update, cancellationToken);
    }

    private async ValueTask SendAsync(
        CaptionUpdate update,
        CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(Serialize(update));
        using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        sendCancellation.CancelAfter(_sendTimeout);
        var lockAcquired = false;
        try
        {
            await _sendLock.WaitAsync(sendCancellation.Token).ConfigureAwait(false);
            lockAcquired = true;
            if (_socket.State != WebSocketState.Open)
            {
                throw new WebSocketException("The caption WebSocket is not open.");
            }

            var sendTask = _socket.SendAsync(
                new ArraySegment<byte>(payload),
                WebSocketMessageType.Text,
                endOfMessage: true,
                sendCancellation.Token);
            await sendTask.WaitAsync(sendCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _socket.Abort();
            throw new TimeoutException("Caption WebSocket send timed out.");
        }
        catch (OperationCanceledException)
        {
            _socket.Abort();
            throw;
        }
        finally
        {
            if (lockAcquired)
            {
                _sendLock.Release();
            }
        }
    }

    private void ObserveSequence(long sequence)
    {
        var observed = Volatile.Read(ref _sequence);
        while (sequence > observed)
        {
            observed = Interlocked.CompareExchange(ref _sequence, sequence, observed);
        }
    }
}

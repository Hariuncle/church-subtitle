using System.Net.WebSockets;
using System.Text.Json;
using ChurchSubtitle.Core.Captions;
using ChurchSubtitle.Core.OpenAi;
using ChurchSubtitle.Core.Streaming;

namespace ChurchSubtitle.Core.Tests;

public sealed class OpenAiRealtimeTranscriptionProviderTests
{
    [Fact]
    public async Task Streams_pcm_and_publishes_partial_and_final_captions()
    {
        var connection = new ScriptedConnection(
        [
            """{"type":"conversation.item.input_audio_transcription.delta","item_id":"item-1","delta":"예수"}""",
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-1","transcript":"예수님"}"""
        ]);
        var sink = new RecordingSink();
        var provider = CreateProvider(new QueueConnectionFactory(connection));
        await using var pcm = new MemoryStream(new byte[4800]);

        var result = await provider.TranscribeAsync(
            pcm,
            Request(),
            sink,
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(0, result.ReconnectCount);
        Assert.Equal(CaptionState.Partial, sink.Updates[0].State);
        Assert.Equal("예수", sink.Updates[0].Text);
        Assert.Equal(CaptionState.Final, sink.Updates[1].State);
        Assert.Equal("예수님", sink.Updates[1].Text);
        Assert.Contains("\"type\":\"session.update\"", connection.Sent[0]);
        Assert.Contains(connection.Sent, message =>
            message.Contains("input_audio_buffer.append", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publishes_live_partial_before_first_ten_second_commit_ack()
    {
        var connection = new PartialBeforeCommitConnection();
        var provider = CreateProvider(new QueueConnectionFactory(connection));
        var sink = new SignalingSink();
        await using var pcm = new GatedContinuationStream(
            new byte[4800],
            Array.Empty<byte>());

        var transcription = provider.TranscribeAsync(
            pcm,
            Request(),
            sink,
            CancellationToken.None);

        await sink.PartialPublished.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(0, connection.CommitCount);
        Assert.False(transcription.IsCompleted);

        pcm.ReleaseContinuation();
        await transcription;

        var partial = Assert.Single(sink.Updates.Where(
            update => update.State == CaptionState.Partial));
        var final = Assert.Single(sink.Updates.Where(
            update => update.State == CaptionState.Final));
        Assert.Equal(partial.LineId, final.LineId);
        Assert.True(final.Revision > partial.Revision);
    }

    [Fact]
    public async Task Ten_second_commit_does_not_end_continuous_provider_input()
    {
        var connection = new CommitCompletingConnection(autoCompleteThrough: 2);
        var provider = CreateProvider(new QueueConnectionFactory(connection));
        await using var pcm = new GatedContinuationStream(
            new byte[480_000],
            new byte[4800]);

        var transcription = provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None);

        await connection.WaitForCommitCountAsync(1);
        Assert.False(transcription.IsCompleted);

        pcm.ReleaseContinuation();
        await transcription;

        Assert.Equal(2, connection.CommitCount);
        Assert.Equal(101, connection.Sent.Count(message =>
            EventType(message) == "input_audio_buffer.append"));
    }

    [Fact]
    public async Task Many_completed_commit_turns_keep_coordinator_state_bounded()
    {
        var tracker = new OpenAiRealtimeTranscriptionProvider.SessionCommittedTurnTracker();
        var timestamp = DateTimeOffset.Parse("2026-08-19T00:00:00Z");

        for (var index = 0; index < 10_000; index++)
        {
            var itemId = $"item-{index}";
            var acknowledged = tracker.RegisterCommit(index, index * 10_000L);
            Assert.Empty(tracker.ObserveCommitted(
                new AudioBufferCommittedServerEvent(itemId, timestamp)));
            await acknowledged;

            var ready = tracker.ObserveTranscript(new TranscriptServerEvent(
                new CaptionUpdate(
                    "session",
                    index + 1,
                    $"connection-1:{itemId}",
                    1,
                    "완료",
                    CaptionState.Final,
                    "openai",
                    timestamp),
                timestamp,
                itemId));
            var final = Assert.Single(ready);
            tracker.MarkPublished(final);
            tracker.PublishCheckpoint();
            await tracker.AllCompleted;
        }

        Assert.InRange(
            tracker.RetainedItemCount,
            0,
            OpenAiRealtimeTranscriptionProvider.SessionCommittedTurnTracker.MaxRetainedItems);
    }

    [Fact]
    public async Task Commits_each_ten_second_window_and_the_final_nonempty_remainder()
    {
        var connection = new CommitCompletingConnection(autoCompleteThrough: 2);
        var provider = CreateProvider(
            new QueueConnectionFactory(connection),
            new PacingThenBlockingDrainDelay(),
            drainPeriod: TimeSpan.FromSeconds(5));
        await using var pcm = new MemoryStream(new byte[4800 * 101]);

        var result = await provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None);

        var eventTypes = connection.Sent.Select(EventType).ToArray();
        Assert.Equal(101, eventTypes.Count(type => type == "input_audio_buffer.append"));
        Assert.Equal(2, eventTypes.Count(type => type == "input_audio_buffer.commit"));
        Assert.Equal("input_audio_buffer.commit", eventTypes[101]);
        Assert.Equal("input_audio_buffer.append", eventTypes[102]);
        Assert.Equal("input_audio_buffer.commit", eventTypes[^1]);
        var metrics = Assert.IsType<TranscriptionRuntimeMetrics>(result.Metrics);
        Assert.Equal(0, metrics.TurnCount);
        Assert.Empty(metrics.FirstPartialLatenciesMs);
        Assert.Empty(metrics.FinalLatenciesMs);
    }

    [Fact]
    public async Task Exact_commit_boundary_has_no_duplicate_commit_or_zero_padding()
    {
        var connection = new CommitCompletingConnection(autoCompleteThrough: 1);
        var provider = CreateProvider(
            new QueueConnectionFactory(connection),
            new PacingThenBlockingDrainDelay(),
            drainPeriod: TimeSpan.FromSeconds(5));
        await using var pcm = new MemoryStream(new byte[4800 * 100]);

        await provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None);

        var audioEvents = connection.Sent.Skip(1).Select(Parse).ToArray();
        Assert.Equal(100, audioEvents.Count(document =>
            document.RootElement.GetProperty("type").GetString() == "input_audio_buffer.append"));
        Assert.Single(audioEvents.Where(document =>
            document.RootElement.GetProperty("type").GetString() == "input_audio_buffer.commit"));
        Assert.All(
            audioEvents.Where(document => document.RootElement.TryGetProperty("audio", out _)),
            document => Assert.NotEmpty(document.RootElement.GetProperty("audio").GetString()!));
        Assert.Equal(
            "input_audio_buffer.commit",
            audioEvents[^1].RootElement.GetProperty("type").GetString());

        foreach (var document in audioEvents)
        {
            document.Dispose();
        }
    }

    [Fact]
    public async Task Commit_cadence_is_based_on_480000_bytes_not_short_read_count()
    {
        var connection = new CommitCompletingConnection(autoCompleteThrough: 5);
        var provider = CreateProvider(
            new QueueConnectionFactory(connection),
            new PacingThenBlockingDrainDelay(),
            drainPeriod: TimeSpan.FromSeconds(5));
        await using var pcm = new NonSeekableShortReadStream(
            new byte[480_000],
            maxReadSize: 1_000);

        await provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None);

        Assert.Single(connection.Sent.Where(message =>
            EventType(message) == "input_audio_buffer.commit"));
    }

    [Fact]
    public async Task Transient_failure_mid_window_replays_buffered_audio_for_nonseekable_input()
    {
        var failed = new ScriptedConnection([], failSendNumber: 3);
        var recovered = new ScriptedConnection(
        [
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-replayed","transcript":"회복됨"}"""
        ]);
        var provider = CreateProvider(new QueueConnectionFactory(failed, recovered));
        await using var pcm = new NonSeekableShortReadStream(
            new byte[4800 * 3],
            maxReadSize: 4800);

        var result = await provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None);

        Assert.Equal(3, recovered.Sent.Count(message =>
            EventType(message) == "input_audio_buffer.append"));
        Assert.Equal(1, result.ReconnectCount);
    }

    [Fact]
    public async Task Receive_failure_mid_window_replays_buffered_audio_for_nonseekable_input()
    {
        var failed = new ReceiveFailureAfterAppendsConnection(failAfterAppends: 2);
        var recovered = new ScriptedConnection(
        [
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-replayed","transcript":"회복됨"}"""
        ]);
        var provider = CreateProvider(new QueueConnectionFactory(failed, recovered));
        await using var pcm = new NonSeekableShortReadStream(
            new byte[4800 * 3],
            maxReadSize: 4800);

        var result = await provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None);

        Assert.Equal(3, recovered.Sent.Count(message =>
            EventType(message) == "input_audio_buffer.append"));
        Assert.Equal(1, result.ReconnectCount);
    }

    [Fact]
    public async Task Empty_audio_does_not_send_an_empty_commit()
    {
        var connection = new ScriptedConnection([]);
        var provider = CreateProvider(new QueueConnectionFactory(connection));
        await using var pcm = new MemoryStream([]);

        await provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None);

        Assert.Single(connection.Sent);
        Assert.Equal("session.update", EventType(connection.Sent[0]));
    }

    [Fact]
    public async Task Waits_for_every_committed_turn_completion_even_when_one_arrives_during_send()
    {
        var connection = new CommitCompletingConnection(autoCompleteThrough: 1);
        var delay = new PacingThenBlockingDrainDelay();
        var provider = CreateProvider(
            new QueueConnectionFactory(connection),
            delay,
            drainPeriod: TimeSpan.FromSeconds(5));
        await using var pcm = new MemoryStream(new byte[4800 * 200]);

        var transcription = provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None);

        await connection.WaitForCommitCountAsync(2);
        Assert.False(transcription.IsCompleted);

        connection.CompleteTurn(2);
        var result = await transcription;

        Assert.True(result.Completed);
        Assert.Equal(2, connection.CommitCount);
    }

    [Fact]
    public async Task Out_of_order_and_duplicate_finals_publish_once_in_commit_ordinal_order()
    {
        var connection = new OutOfOrderFinalConnection();
        var provider = CreateProvider(
            new QueueConnectionFactory(connection),
            new PacingThenBlockingDrainDelay(),
            drainPeriod: TimeSpan.FromSeconds(5));
        var sink = new RecordingSink();
        await using var pcm = new MemoryStream(new byte[4800 * 200]);

        await provider.TranscribeAsync(
            pcm,
            Request(),
            sink,
            CancellationToken.None);

        var finals = sink.Updates.Where(update => update.State == CaptionState.Final).ToArray();
        Assert.Equal(2, finals.Length);
        Assert.EndsWith(":item-1", finals[0].LineId, StringComparison.Ordinal);
        Assert.EndsWith(":item-2", finals[1].LineId, StringComparison.Ordinal);
        Assert.Equal([0L, 1L], finals.Select(update => update.SegmentOrdinal));
        Assert.Equal([0L, 10_000L], finals.Select(update => update.SegmentStartMs));
        Assert.All(finals, update => Assert.Null(update.AudioStartMs));
    }

    [Fact]
    public async Task Reused_provider_item_id_after_reconnect_has_a_distinct_line_namespace()
    {
        var failed = new ScriptedConnection(
        [
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-1","transcript":"첫 구간"}"""
        ],
        failSendNumber: 103);
        var recovered = new ScriptedConnection(
        [
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-1","transcript":"둘째 구간"}"""
        ]);
        var provider = CreateProvider(new QueueConnectionFactory(failed, recovered));
        var sink = new RecordingSink();
        await using var pcm = new MemoryStream(new byte[4800 * 200]);

        var result = await provider.TranscribeAsync(
            pcm,
            Request(),
            sink,
            CancellationToken.None);

        var finals = sink.Updates.Where(update => update.State == CaptionState.Final).ToArray();
        Assert.Equal(2, finals.Length);
        Assert.Equal("connection-1:item-1", finals[0].LineId);
        Assert.Equal("connection-2:item-1", finals[1].LineId);
        Assert.Equal(1, result.ReconnectCount);
    }

    [Fact]
    public async Task Receive_fault_concurrent_with_last_completion_is_not_reported_as_success()
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var connection = new CompletionThenFaultConnection();
            var provider = CreateProvider(
                new QueueConnectionFactory(connection),
                maxReconnectAttempts: 0);
            await using var pcm = new MemoryStream(new byte[4800]);

            var failure = await Assert.ThrowsAsync<TranscriptionRunFailedException>(() =>
                provider.TranscribeAsync(
                    pcm,
                    Request(),
                    new RecordingSink(),
                    CancellationToken.None));

            Assert.IsType<WebSocketException>(failure.InnerException);
        }
    }

    [Fact]
    public async Task Completion_arriving_before_commit_ack_is_bound_after_item_id_acknowledgement()
    {
        var connection = new EarlyCompletionConnection();
        var provider = CreateProvider(
            new QueueConnectionFactory(connection),
            new PacingThenBlockingDrainDelay(),
            drainPeriod: TimeSpan.FromSeconds(5));
        var sink = new RecordingSink();
        await using var pcm = new MemoryStream(new byte[4800]);

        await provider.TranscribeAsync(
            pcm,
            Request(),
            sink,
            CancellationToken.None);

        var final = Assert.Single(sink.Updates.Where(
            update => update.State == CaptionState.Final));
        Assert.Equal("connection-1:item-early", final.LineId);
        Assert.Equal(0, final.SegmentOrdinal);
        Assert.Equal(0, final.SegmentStartMs);
        Assert.Null(final.AudioStartMs);
    }

    [Fact]
    public async Task Times_out_when_a_committed_turn_never_completes()
    {
        var connection = new CommitCompletingConnection(autoCompleteThrough: 0);
        var provider = CreateProvider(
            new QueueConnectionFactory(connection),
            maxReconnectAttempts: 0);
        await using var pcm = new MemoryStream(new byte[4800]);

        var failure = await Assert.ThrowsAsync<TranscriptionRunFailedException>(() =>
            provider.TranscribeAsync(
                pcm,
                Request(),
                new RecordingSink(),
                CancellationToken.None));

        Assert.IsType<TimeoutException>(failure.InnerException);
        Assert.Contains("committed turn completion", failure.InnerException!.Message);
    }

    [Fact]
    public async Task Reconnects_to_the_same_provider_after_a_transient_socket_failure()
    {
        var failed = new ScriptedConnection([], new WebSocketException("network lost"));
        var recovered = new ScriptedConnection(
        [
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-2","transcript":"회복됨"}"""
        ]);
        var delay = new RecordingDelay();
        var provider = CreateProvider(
            new QueueConnectionFactory(failed, recovered),
            delay);
        var sink = new RecordingSink();
        await using var pcm = new MemoryStream(new byte[4800]);

        var result = await provider.TranscribeAsync(
            pcm,
            Request(),
            sink,
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Equal(1, result.ReconnectCount);
        Assert.Contains(TimeSpan.FromSeconds(1), delay.Delays);
        Assert.Contains(sink.Updates, update => update.Text == "회복됨");
    }

    [Fact]
    public async Task Api_error_stops_audio_sending_immediately()
    {
        var connection = new ScriptedConnection(
        [
            """{"type":"error","error":{"code":"invalid_request_error","message":"Bad session"}}"""
        ]);
        var provider = CreateProvider(new QueueConnectionFactory(connection));
        await using var pcm = new MemoryStream(new byte[48000]);

        var failure = await Assert.ThrowsAsync<TranscriptionRunFailedException>(() =>
            provider.TranscribeAsync(
                pcm,
                Request(),
                new RecordingSink(),
                CancellationToken.None));

        var error = Assert.IsType<OpenAiRealtimeApiException>(failure.InnerException);
        Assert.Equal("invalid_request_error", error.Code);
        Assert.NotNull(failure.PartialResult.Metrics);
        Assert.Single(connection.Sent);
        Assert.Contains("session.update", connection.Sent[0]);
    }

    [Fact]
    public async Task Send_failure_cancels_and_observes_the_receive_loop()
    {
        var connection = new ScriptedConnection([], failSendNumber: 2);
        var provider = CreateProvider(
            new QueueConnectionFactory(connection),
            maxReconnectAttempts: 0);
        await using var pcm = new MemoryStream(new byte[4800]);

        var failure = await Assert.ThrowsAsync<TranscriptionRunFailedException>(() =>
            provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None));

        Assert.IsType<WebSocketException>(failure.InnerException);
        Assert.True(connection.ReceiveCancellationObserved);
    }

    [Fact]
    public async Task Pending_speech_without_a_completed_transcript_fails_the_run()
    {
        var connection = new ScriptedConnection(
        [
            """{"type":"input_audio_buffer.speech_started","item_id":"item-pending","audio_start_ms":0}"""
        ]);
        var provider = CreateProvider(
            new QueueConnectionFactory(connection),
            maxReconnectAttempts: 0);
        await using var pcm = new MemoryStream(new byte[4800]);

        var failure = await Assert.ThrowsAsync<TranscriptionRunFailedException>(() =>
            provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None));

        Assert.IsType<TimeoutException>(failure.InnerException);
        Assert.Equal(1, failure.PartialResult.Metrics?.TurnCount);
    }

    [Fact]
    public async Task Reconnect_retries_the_chunk_whose_send_failed_without_replaying_older_audio()
    {
        var failed = new ScriptedConnection([], failSendNumber: 2);
        var recovered = new ScriptedConnection(
        [
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-2","transcript":"회복됨"}"""
        ]);
        var provider = CreateProvider(new QueueConnectionFactory(failed, recovered));
        await using var pcm = new MemoryStream(new byte[4800]);

        var result = await provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None);

        var recoveredAudioMessages = recovered.Sent.Count(message =>
            message.Contains("input_audio_buffer.append", StringComparison.Ordinal));
        Assert.Equal(1, recoveredAudioMessages);
        Assert.Equal(1, result.ReconnectCount);
        Assert.False(result.Completed);
    }

    [Fact]
    public async Task Commit_send_failure_replays_the_whole_uncommitted_window_on_reconnect()
    {
        var failed = new ScriptedConnection([], failSendNumber: 102);
        var recovered = new ScriptedConnection(
        [
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-replayed","transcript":"회복됨"}"""
        ]);
        var provider = CreateProvider(new QueueConnectionFactory(failed, recovered));
        await using var pcm = new MemoryStream(new byte[4800 * 100]);

        var result = await provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None);

        Assert.Equal(100, recovered.Sent.Count(message =>
            EventType(message) == "input_audio_buffer.append"));
        Assert.Single(recovered.Sent.Where(message =>
            EventType(message) == "input_audio_buffer.commit"));
        Assert.Equal(1, result.ReconnectCount);
        Assert.False(result.Completed);
    }

    [Fact]
    public async Task Retry_exhaustion_stops_after_three_reconnect_attempts()
    {
        var factory = new QueueConnectionFactory(
            new ScriptedConnection([], new WebSocketException("failure 1")),
            new ScriptedConnection([], new WebSocketException("failure 2")),
            new ScriptedConnection([], new WebSocketException("failure 3")),
            new ScriptedConnection([], new WebSocketException("failure 4")));
        var provider = CreateProvider(factory);
        await using var pcm = new MemoryStream(new byte[4800]);

        var failure = await Assert.ThrowsAsync<TranscriptionRunFailedException>(() =>
            provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None));

        Assert.IsType<WebSocketException>(failure.InnerException);
        Assert.Equal(4, factory.CreatedCount);
        Assert.Equal(3, failure.PartialResult.ReconnectCount);
    }

    [Fact]
    public async Task Metrics_include_events_observed_before_a_reconnect()
    {
        var failed = new ScriptedConnection(
        [
            """{"type":"input_audio_buffer.speech_started","item_id":"item-before","audio_start_ms":0}""",
            """{"type":"input_audio_buffer.speech_stopped","item_id":"item-before","audio_end_ms":0}""",
            """{"type":"conversation.item.input_audio_transcription.delta","item_id":"item-before","delta":"이전"}""",
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-before","transcript":"이전 문장"}"""
        ],
        new WebSocketException("network lost"),
        receiveExceptionAfterMessages: true);
        var recovered = new ScriptedConnection(
        [
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-after","transcript":"이후 문장"}"""
        ]);
        var provider = CreateProvider(new QueueConnectionFactory(failed, recovered));
        await using var pcm = new MemoryStream(new byte[4800]);

        var result = await provider.TranscribeAsync(
            pcm,
            Request(),
            new RecordingSink(),
            CancellationToken.None);

        Assert.NotNull(result.Metrics);
        Assert.Equal(1, result.Metrics.TurnCount);
        Assert.Equal(1, result.Metrics.PartialUpdateCount);
        Assert.False(result.Completed);
    }

    private static OpenAiRealtimeTranscriptionProvider CreateProvider(
        IRealtimeConnectionFactory factory,
        IAsyncDelay? delay = null,
        int maxReconnectAttempts = 3,
        TimeSpan? drainPeriod = null)
    {
        var effectiveDrainPeriod = drainPeriod ?? TimeSpan.FromMilliseconds(25);
        return new(
            factory,
            delay ?? new RecordingDelay(),
            new ReconnectPolicy(maxReconnectAttempts),
            effectiveDrainPeriod);
    }

    private static string? EventType(string json)
    {
        using var document = Parse(json);
        return document.RootElement.GetProperty("type").GetString();
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    private static OpenAiTranscriptionRequest Request() =>
        new(
            "test-api-key",
            "session-1",
            new OpenAiTranscriptionOptions(
                "low",
                "한국어 교회 예배 설교",
                ["예수님"]));

    private sealed class QueueConnectionFactory(params IRealtimeConnection[] connections)
        : IRealtimeConnectionFactory
    {
        private readonly Queue<IRealtimeConnection> _connections = new(connections);

        public int CreatedCount { get; private set; }

        public IRealtimeConnection Create(string apiKey)
        {
            CreatedCount++;
            return _connections.Dequeue();
        }
    }

    private sealed class ScriptedConnection(
        IEnumerable<string> messages,
        Exception? receiveException = null,
        int? failSendNumber = null,
        bool receiveExceptionAfterMessages = false) : IRealtimeConnection
    {
        private readonly Queue<string> _messages = new(messages);
        private readonly Queue<string> _ackItemIds = new(messages
            .Select(TryGetItemId)
            .Where(itemId => itemId is not null)
            .Distinct(StringComparer.Ordinal)!);
        private readonly SemaphoreSlim _messagesAvailable = new(messages.Count());
        private readonly object _sync = new();
        private Exception? _receiveException = receiveException;
        private int _sendCount;
        private int _generatedItemId;

        public List<string> Sent { get; } = [];

        public bool ReceiveCancellationObserved { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendTextAsync(string message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            _sendCount++;
            if (_sendCount == failSendNumber)
            {
                return Task.FromException(new WebSocketException("send failed"));
            }

            if (EventType(message) == "input_audio_buffer.commit")
            {
                lock (_sync)
                {
                    var itemId = _ackItemIds.Count > 0
                        ? _ackItemIds.Dequeue()
                        : $"generated-{++_generatedItemId}";
                    _messages.Enqueue(
                        $$"""{"type":"input_audio_buffer.committed","item_id":"{{itemId}}","previous_item_id":null}""");
                    _messagesAvailable.Release();
                }
            }

            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
        {
            if (_receiveException is not null &&
                (!receiveExceptionAfterMessages || _messages.Count == 0))
            {
                var exception = _receiveException;
                _receiveException = null;
                throw exception;
            }

            try
            {
                await _messagesAvailable.WaitAsync(cancellationToken);
                lock (_sync)
                {
                    return _messages.Dequeue();
                }
            }
            finally
            {
                ReceiveCancellationObserved = cancellationToken.IsCancellationRequested;
            }
        }

        public ValueTask DisposeAsync()
        {
            _messagesAvailable.Dispose();
            return ValueTask.CompletedTask;
        }

        private static string? TryGetItemId(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("item_id", out var itemId)
                ? itemId.GetString()
                : null;
        }
    }

    private sealed class RecordingSink : ICaptionSink
    {
        public List<CaptionUpdate> Updates { get; } = [];

        public ValueTask PublishAsync(CaptionUpdate update, CancellationToken cancellationToken)
        {
            Updates.Add(update);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SignalingSink : ICaptionSink
    {
        public List<CaptionUpdate> Updates { get; } = [];

        public Task PartialPublished => _partialPublished.Task;

        private readonly TaskCompletionSource _partialPublished = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask PublishAsync(CaptionUpdate update, CancellationToken cancellationToken)
        {
            Updates.Add(update);
            if (update.State == CaptionState.Partial)
            {
                _partialPublished.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDelay : IAsyncDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            Delays.Add(duration);
            return duration == TimeSpan.FromMilliseconds(25)
                ? Task.Delay(duration, cancellationToken)
                : Task.CompletedTask;
        }
    }

    private sealed class PacingThenBlockingDrainDelay : IAsyncDelay
    {
        private readonly TaskCompletionSource _drain = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
            duration == TimeSpan.FromSeconds(5)
                ? _drain.Task.WaitAsync(cancellationToken)
                : Task.CompletedTask;
    }

    private sealed class CommitCompletingConnection(int autoCompleteThrough)
        : IRealtimeConnection
    {
        private readonly Queue<string> _messages = [];
        private readonly SemaphoreSlim _messagesAvailable = new(0);
        private readonly TaskCompletionSource _secondCommitObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstCommitObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _sync = new();

        public List<string> Sent { get; } = [];

        public int CommitCount { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendTextAsync(string message, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                Sent.Add(message);
                if (EventType(message) == "input_audio_buffer.commit")
                {
                    CommitCount++;
                    _firstCommitObserved.TrySetResult();
                    EnqueueAcknowledgement(CommitCount);
                    if (CommitCount <= autoCompleteThrough)
                    {
                        EnqueueCompletion(CommitCount);
                    }

                    if (CommitCount >= 2)
                    {
                        _secondCommitObserved.TrySetResult();
                    }
                }
            }

            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
        {
            await _messagesAvailable.WaitAsync(cancellationToken);
            lock (_sync)
            {
                return _messages.Dequeue();
            }
        }

        public void CompleteTurn(int turnNumber)
        {
            lock (_sync)
            {
                EnqueueCompletion(turnNumber);
            }
        }

        public Task WaitForCommitCountAsync(int count) => count <= 1
            ? _firstCommitObserved.Task
            : _secondCommitObserved.Task;

        private void EnqueueCompletion(int turnNumber)
        {
            _messages.Enqueue(
                $$"""{"type":"conversation.item.input_audio_transcription.completed","item_id":"item-{{turnNumber}}","transcript":""}""");
            _messagesAvailable.Release();
        }

        private void EnqueueAcknowledgement(int turnNumber)
        {
            _messages.Enqueue(
                $$"""{"type":"input_audio_buffer.committed","item_id":"item-{{turnNumber}}","previous_item_id":null}""");
            _messagesAvailable.Release();
        }

        public ValueTask DisposeAsync()
        {
            _messagesAvailable.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NonSeekableShortReadStream(byte[] bytes, int maxReadSize)
        : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = Math.Min(Math.Min(count, maxReadSize), bytes.Length - _position);
            bytes.AsSpan(_position, read).CopyTo(buffer.AsSpan(offset, read));
            _position += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = Math.Min(
                Math.Min(buffer.Length, maxReadSize),
                bytes.Length - _position);
            bytes.AsMemory(_position, read).CopyTo(buffer);
            _position += read;
            return ValueTask.FromResult(read);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class GatedContinuationStream(byte[] initial, byte[] continuation)
        : Stream
    {
        private readonly TaskCompletionSource _continuationReleased = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _initialPosition;
        private int _continuationPosition;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void ReleaseContinuation() => _continuationReleased.TrySetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_initialPosition < initial.Length)
            {
                var read = Math.Min(buffer.Length, initial.Length - _initialPosition);
                initial.AsMemory(_initialPosition, read).CopyTo(buffer);
                _initialPosition += read;
                return read;
            }

            await _continuationReleased.Task.WaitAsync(cancellationToken);
            if (_continuationPosition >= continuation.Length)
            {
                return 0;
            }

            var continuationRead = Math.Min(
                buffer.Length,
                continuation.Length - _continuationPosition);
            continuation.AsMemory(_continuationPosition, continuationRead).CopyTo(buffer);
            _continuationPosition += continuationRead;
            return continuationRead;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ReceiveFailureAfterAppendsConnection(int failAfterAppends)
        : IRealtimeConnection
    {
        private readonly TaskCompletionSource<string?> _receive = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _appendCount;

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendTextAsync(string message, CancellationToken cancellationToken)
        {
            if (EventType(message) == "input_audio_buffer.append" &&
                ++_appendCount == failAfterAppends)
            {
                _receive.TrySetException(new WebSocketException("receive failed mid-window"));
            }

            return Task.CompletedTask;
        }

        public Task<string?> ReceiveTextAsync(CancellationToken cancellationToken) =>
            _receive.Task.WaitAsync(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class OutOfOrderFinalConnection : QueuedConnection
    {
        private int _commitCount;

        protected override void OnMessageSent(string message)
        {
            if (EventType(message) != "input_audio_buffer.commit")
            {
                return;
            }

            _commitCount++;
            Enqueue(
                $$"""{"type":"input_audio_buffer.committed","item_id":"item-{{_commitCount}}","previous_item_id":null}""");
            if (_commitCount == 2)
            {
                Enqueue(Completed("item-2", "둘째"));
                Enqueue(Completed("item-2", "중복"));
                Enqueue(Completed("item-1", "첫째"));
            }
        }
    }

    private sealed class CompletionThenFaultConnection : QueuedConnection
    {
        protected override void OnMessageSent(string message)
        {
            if (EventType(message) == "input_audio_buffer.commit")
            {
                Enqueue(
                    """{"type":"input_audio_buffer.committed","item_id":"item-1","previous_item_id":null}""");
                Enqueue(Completed("item-1", "완료"));
                ArmFault();
            }
        }
    }

    private sealed class EarlyCompletionConnection : QueuedConnection
    {
        protected override void OnMessageSent(string message)
        {
            if (EventType(message) == "input_audio_buffer.commit")
            {
                Enqueue(Completed("item-early", "빠른 완료"));
                Enqueue(
                    """{"type":"input_audio_buffer.committed","item_id":"item-early","previous_item_id":null}""");
            }
        }
    }

    private sealed class PartialBeforeCommitConnection : QueuedConnection
    {
        private bool _partialSent;

        public int CommitCount { get; private set; }

        protected override void OnMessageSent(string message)
        {
            var type = EventType(message);
            if (type == "input_audio_buffer.append" && !_partialSent)
            {
                _partialSent = true;
                Enqueue(
                    """{"type":"conversation.item.input_audio_transcription.delta","item_id":"item-live","delta":"실시간"}""");
            }
            else if (type == "input_audio_buffer.commit")
            {
                CommitCount++;
                Enqueue(
                    """{"type":"input_audio_buffer.committed","item_id":"item-live","previous_item_id":null}""");
                Enqueue(Completed("item-live", "실시간 자막"));
            }
        }
    }

    private abstract class QueuedConnection : IRealtimeConnection
    {
        private readonly Queue<string> _messages = [];
        private readonly SemaphoreSlim _available = new(0);
        private readonly object _sync = new();
        private bool _faultArmed;

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendTextAsync(string message, CancellationToken cancellationToken)
        {
            OnMessageSent(message);
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_faultArmed && _messages.Count == 0)
                {
                    throw new WebSocketException("receive failed after completion");
                }
            }

            await _available.WaitAsync(cancellationToken);
            lock (_sync)
            {
                return _messages.Dequeue();
            }
        }

        protected abstract void OnMessageSent(string message);

        protected void Enqueue(string message)
        {
            lock (_sync)
            {
                _messages.Enqueue(message);
                _available.Release();
            }
        }

        protected void ArmFault()
        {
            lock (_sync)
            {
                _faultArmed = true;
            }
        }

        protected static string Completed(string itemId, string transcript) =>
            $$"""{"type":"conversation.item.input_audio_transcription.completed","item_id":"{{itemId}}","transcript":"{{transcript}}"}""";

        public ValueTask DisposeAsync()
        {
            _available.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

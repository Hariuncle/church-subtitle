using System.Diagnostics;
using System.Net.WebSockets;
using ChurchSubtitle.Core.Captions;
using ChurchSubtitle.Core.Streaming;

namespace ChurchSubtitle.Core.OpenAi;

public sealed class OpenAiRealtimeTranscriptionProvider
    : ISttProvider<OpenAiTranscriptionRequest>
{
    private static readonly TimeSpan ChunkDuration = TimeSpan.FromMilliseconds(100);
    private const int CommittedWindowBytes = 480_000;
    private readonly IRealtimeConnectionFactory _connectionFactory;
    private readonly IAsyncDelay _delay;
    private readonly ReconnectPolicy _reconnectPolicy;
    private readonly TimeSpan _drainPeriod;

    public OpenAiRealtimeTranscriptionProvider(
        IRealtimeConnectionFactory connectionFactory,
        IAsyncDelay delay,
        ReconnectPolicy reconnectPolicy,
        TimeSpan drainPeriod)
    {
        _connectionFactory = connectionFactory;
        _delay = delay;
        _reconnectPolicy = reconnectPolicy;
        _drainPeriod = drainPeriod;
    }

    public async Task<TranscriptionRunResult> TranscribeAsync(
        Stream pcmAudio,
        OpenAiTranscriptionRequest request,
        ICaptionSink sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pcmAudio);
        if (!pcmAudio.CanRead)
        {
            throw new ArgumentException("PCM audio stream must be readable.", nameof(pcmAudio));
        }

        var projector = new TranscriptProjector(request.SessionId, "openai");
        var gate = new CaptionUpdateGate(TimeSpan.FromMilliseconds(200));
        var reconnectCount = 0;
        var connectionOrdinal = 0;
        var latencyTracker = new RealtimeLatencyTracker(DateTimeOffset.UtcNow);
        var streamingState = new BufferedStreamingState(CommittedWindowBytes);
        var audioDurationMs = pcmAudio.CanSeek
            ? (long)Math.Round(
                pcmAudio.Length * 1000d /
                PcmStreamFormat.Pcm24KhzMono16Bit.BytesPerSecond)
            : 0;

        while (true)
        {
            connectionOrdinal++;
            streamingState.PrepareForConnection();
            await using var connection = _connectionFactory.Create(request.ApiKey);
            try
            {
                var metrics = await RunSessionAsync(
                    connection,
                    pcmAudio,
                    request.Options,
                    projector,
                    gate,
                    sink,
                    latencyTracker,
                    streamingState,
                    $"connection-{connectionOrdinal}",
                    audioDurationMs,
                    cancellationToken);
                return new TranscriptionRunResult(
                    Completed: reconnectCount == 0,
                    reconnectCount,
                    metrics);
            }
            catch (Exception exception) when (
                IsTransient(exception) &&
                _reconnectPolicy.CanRetry(reconnectCount + 1))
            {
                reconnectCount++;
                await _delay.DelayAsync(
                    _reconnectPolicy.GetDelay(reconnectCount),
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                throw new TranscriptionRunFailedException(
                    $"OpenAI transcription failed: {exception.Message}",
                    new TranscriptionRunResult(
                        Completed: false,
                        reconnectCount,
                        latencyTracker.CreateMetrics(audioDurationMs)),
                    exception);
            }
        }
    }

    private async Task<TranscriptionRuntimeMetrics> RunSessionAsync(
        IRealtimeConnection connection,
        Stream pcmAudio,
        OpenAiTranscriptionOptions options,
        TranscriptProjector projector,
        CaptionUpdateGate gate,
        ICaptionSink sink,
        RealtimeLatencyTracker latencyTracker,
        BufferedStreamingState streamingState,
        string connectionNamespace,
        long audioDurationMs,
        CancellationToken cancellationToken)
    {
        await connection.ConnectAsync(cancellationToken);
        await connection.SendTextAsync(
            OpenAiMessageFactory.CreateSessionUpdate(options),
            cancellationToken);

        latencyTracker.BeginSession(DateTimeOffset.UtcNow);
        var committedTurns = new SessionCommittedTurnTracker();
        using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var receiveTask = ReceiveLoopAsync(
            connection,
            projector,
            gate,
            sink,
            latencyTracker,
            committedTurns,
            connectionNamespace,
            receiveCancellation.Token);

        Task? sendTask = null;
        try
        {
            if (receiveTask.IsCompleted)
            {
                await receiveTask;
                throw new WebSocketException(
                    "The Realtime connection closed before audio streaming began.");
            }

            sendTask = SendAudioAsync(
                connection,
                pcmAudio,
                streamingState,
                committedTurns,
                receiveTask,
                receiveCancellation.Token);
            await Task.WhenAny(receiveTask, sendTask);
            if (receiveTask.IsCompleted)
            {
                receiveCancellation.Cancel();
                await receiveTask;
                throw new WebSocketException(
                    "The Realtime connection closed before audio streaming completed.");
            }

            await sendTask;
            await WaitForServerSignalAsync(
                committedTurns.AllCompleted,
                receiveTask,
                "committed turn completion(s)",
                cancellationToken);

            if (receiveTask.IsCompleted)
            {
                await receiveTask;
                throw new WebSocketException(
                    "The Realtime connection closed before the session completed.");
            }

            return latencyTracker.CreateMetrics(audioDurationMs);
        }
        finally
        {
            receiveCancellation.Cancel();
            if (sendTask is not null)
            {
                await ObserveForCleanupAsync(sendTask);
            }

            await ObserveForCleanupAsync(receiveTask);
        }
    }

    private async Task SendAudioAsync(
        IRealtimeConnection connection,
        Stream pcmAudio,
        BufferedStreamingState state,
        SessionCommittedTurnTracker committedTurns,
        Task receiveTask,
        CancellationToken cancellationToken)
    {
        var chunkBytes = PcmStreamFormat.Pcm24KhzMono16Bit.BytesPerChunk(
            ChunkDuration);
        var pacingClock = Stopwatch.StartNew();
        long sentChunkCount = 0;

        while (true)
        {
            while (!state.IsReadyToCommit)
            {
                if (state.BytesReadyToSend < chunkBytes &&
                    !state.InputEnded &&
                    !state.WindowIsFull)
                {
                    var bytesRead = await state.ReadMoreAsync(
                        pcmAudio,
                        chunkBytes - state.BytesReadyToSend,
                        cancellationToken);
                    if (bytesRead == 0)
                    {
                        state.MarkInputEnded();
                    }
                }

                var sendLength = state.NextSendLength(chunkBytes);
                if (sendLength == 0)
                {
                    break;
                }

                await connection.SendTextAsync(
                    OpenAiMessageFactory.CreateAudioAppend(
                        state.NextSendBytes(sendLength).Span),
                    cancellationToken);
                state.MarkSent(sendLength);
                sentChunkCount++;
                await DelayUntilNextChunkAsync(
                    pacingClock,
                    sentChunkCount,
                    cancellationToken);
            }

            if (state.WindowLength == 0 && state.InputEnded)
            {
                return;
            }

            if (!state.IsReadyToCommit)
            {
                continue;
            }

            var acknowledgement = committedTurns.RegisterCommit(
                state.WindowOrdinal,
                state.WindowOffsetMs);
            await connection.SendTextAsync(
                OpenAiMessageFactory.CreateAudioCommit(),
                cancellationToken);
            await WaitForServerSignalAsync(
                acknowledgement,
                receiveTask,
                "commit acknowledgement",
                cancellationToken);
            state.MarkAcknowledged();
        }
    }

    private async Task WaitForServerSignalAsync(
        Task signal,
        Task receiveTask,
        string description,
        CancellationToken cancellationToken)
    {
        if (receiveTask.IsCompleted)
        {
            await receiveTask;
            throw new WebSocketException(
                $"The Realtime connection closed before {description}.");
        }

        if (signal.IsCompleted)
        {
            await signal;
            if (receiveTask.IsCompleted)
            {
                await receiveTask;
                throw new WebSocketException(
                    $"The Realtime connection closed before {description} settled.");
            }

            return;
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var timeoutTask = _delay.DelayAsync(
            _drainPeriod,
            timeoutCancellation.Token);
        var completedTask = await Task.WhenAny(receiveTask, signal, timeoutTask);
        if (completedTask != timeoutTask)
        {
            timeoutCancellation.Cancel();
            await ObserveForCleanupAsync(timeoutTask);
        }

        if (receiveTask.IsCompleted)
        {
            await receiveTask;
            throw new WebSocketException(
                $"The Realtime connection closed before {description}.");
        }

        if (signal.IsCompleted)
        {
            await signal;
            return;
        }

        await timeoutTask;
        if (receiveTask.IsCompleted)
        {
            await receiveTask;
        }

        if (signal.IsCompleted)
        {
            await signal;
            return;
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }

    private Task DelayUntilNextChunkAsync(
        Stopwatch pacingClock,
        long sentChunkCount,
        CancellationToken cancellationToken)
    {
        var target = TimeSpan.FromTicks(ChunkDuration.Ticks * sentChunkCount);
        var remaining = target - pacingClock.Elapsed;
        return remaining > TimeSpan.Zero
            ? _delay.DelayAsync(remaining, cancellationToken)
            : Task.CompletedTask;
    }

    private static async Task ReceiveLoopAsync(
        IRealtimeConnection connection,
        TranscriptProjector projector,
        CaptionUpdateGate gate,
        ICaptionSink sink,
        RealtimeLatencyTracker latencyTracker,
        SessionCommittedTurnTracker committedTurns,
        string connectionNamespace,
        CancellationToken cancellationToken)
    {
        var parser = new OpenAiServerEventParser(
            projector,
            itemIdNamespace: connectionNamespace);
        try
        {
            while (true)
            {
                var receiveOperation = connection.ReceiveTextAsync(cancellationToken);
                committedTurns.TrackReceiveOperation(receiveOperation);
                committedTurns.PublishCheckpoint();
                var json = await receiveOperation;
                if (json is null)
                {
                    committedTurns.MarkReceiveFailure();
                    return;
                }

                var serverEvent = parser.Parse(json, DateTimeOffset.UtcNow);
                if (serverEvent is not null)
                {
                    latencyTracker.Observe(serverEvent);
                }

                switch (serverEvent)
                {
                    case AudioBufferCommittedServerEvent committed:
                        foreach (var transcript in committedTurns.ObserveCommitted(committed))
                        {
                            await PublishTranscriptAsync(
                                transcript,
                                gate,
                                sink,
                                cancellationToken);
                            committedTurns.MarkPublished(transcript);
                        }

                        break;
                    case TranscriptServerEvent transcript:
                        foreach (var ready in committedTurns.ObserveTranscript(transcript))
                        {
                            await PublishTranscriptAsync(
                                ready,
                                gate,
                                sink,
                                cancellationToken);
                            committedTurns.MarkPublished(ready);
                        }

                        break;
                    case ProviderErrorServerEvent error:
                        throw new OpenAiRealtimeApiException(error.Code, error.Message);
                }

                committedTurns.FinishReceiveMessage();
            }
        }
        catch
        {
            committedTurns.MarkReceiveFailure();
            throw;
        }
    }

    private static ValueTask PublishTranscriptAsync(
        TranscriptServerEvent transcript,
        CaptionUpdateGate gate,
        ICaptionSink sink,
        CancellationToken cancellationToken) =>
        gate.ShouldPublish(transcript.Update)
            ? sink.PublishAsync(transcript.Update, cancellationToken)
            : ValueTask.CompletedTask;

    private static bool IsTransient(Exception exception) =>
        exception is WebSocketException or IOException;

    private static async Task ObserveForCleanupAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // The main control flow propagates the primary failure. Cleanup observes
            // the counterpart so no socket, send, or receive task faults unobserved.
        }
    }

    private sealed class BufferedStreamingState(int capacity)
    {
        private readonly byte[] _window = new byte[capacity];
        private long _acknowledgedBytes;

        public int WindowLength { get; private set; }
        public int SentLength { get; private set; }
        public bool InputEnded { get; private set; }
        public long WindowOrdinal { get; private set; }
        public long WindowOffsetMs => (long)Math.Round(
            _acknowledgedBytes * 1000d /
            PcmStreamFormat.Pcm24KhzMono16Bit.BytesPerSecond);
        public int BytesReadyToSend => WindowLength - SentLength;
        public bool WindowIsFull => WindowLength == _window.Length;
        public bool IsReadyToCommit =>
            SentLength == WindowLength &&
            WindowLength > 0 &&
            (WindowIsFull || InputEnded);

        public void PrepareForConnection() => SentLength = 0;

        public async ValueTask<int> ReadMoreAsync(
            Stream source,
            int desiredBytes,
            CancellationToken cancellationToken)
        {
            var available = _window.Length - WindowLength;
            var requested = Math.Min(available, Math.Max(1, desiredBytes));
            var read = await source.ReadAsync(
                _window.AsMemory(WindowLength, requested),
                cancellationToken);
            WindowLength += read;
            return read;
        }

        public void MarkInputEnded() => InputEnded = true;

        public int NextSendLength(int chunkBytes)
        {
            var ready = BytesReadyToSend;
            if (ready >= chunkBytes)
            {
                return chunkBytes;
            }

            return (InputEnded || WindowIsFull) ? ready : 0;
        }

        public ReadOnlyMemory<byte> NextSendBytes(int length) =>
            _window.AsMemory(SentLength, length);

        public void MarkSent(int length) => SentLength += length;

        public void MarkAcknowledged()
        {
            _acknowledgedBytes += WindowLength;
            WindowOrdinal++;
            WindowLength = 0;
            SentLength = 0;
        }
    }

    internal sealed class SessionCommittedTurnTracker
    {
        internal const int MaxRetainedItems = 256;
        private readonly object _sync = new();
        private readonly Queue<PendingCommit> _awaitingAcknowledgements = [];
        private readonly Dictionary<string, Segment> _segments = [];
        private readonly Dictionary<string, TranscriptServerEvent> _earlyFinals = [];
        private readonly HashSet<string> _recentCompletedItems = [];
        private readonly Queue<string> _recentCompletedOrder = [];
        private readonly SortedDictionary<long, TranscriptServerEvent> _finalsByOrdinal = [];
        private TaskCompletionSource _allCompleted = CompletedSource();
        private long? _nextFinalOrdinal;
        private int _registeredCount;
        private int _completedCount;
        private int _publishedCompletedCount;
        private int _receiveOutcome;

        public Task AllCompleted
        {
            get
            {
                lock (_sync)
                {
                    return _allCompleted.Task;
                }
            }
        }

        internal int RetainedItemCount
        {
            get
            {
                lock (_sync)
                {
                    return _segments.Count +
                        _earlyFinals.Count +
                        _finalsByOrdinal.Count +
                        _recentCompletedItems.Count;
                }
            }
        }

        public Task RegisterCommit(long ordinal, long offsetMs)
        {
            lock (_sync)
            {
                if (_awaitingAcknowledgements.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Only one commit acknowledgement may be pending.");
                }

                var pending = new PendingCommit(ordinal, offsetMs);
                _awaitingAcknowledgements.Enqueue(pending);
                _nextFinalOrdinal ??= ordinal;
                _registeredCount++;
                Interlocked.CompareExchange(ref _receiveOutcome, 0, 1);
                if (_registeredCount > _completedCount && _allCompleted.Task.IsCompleted)
                {
                    _allCompleted = NewSource();
                }

                return pending.Acknowledged.Task;
            }
        }

        public IReadOnlyList<TranscriptServerEvent> ObserveCommitted(
            AudioBufferCommittedServerEvent committed)
        {
            lock (_sync)
            {
                if (_awaitingAcknowledgements.Count == 0)
                {
                    throw new OpenAiRealtimeApiException(
                        "unexpected_commit_ack",
                        "Received a commit acknowledgement with no pending commit.");
                }

                if (_segments.ContainsKey(committed.ItemId) ||
                    _recentCompletedItems.Contains(committed.ItemId))
                {
                    throw new OpenAiRealtimeApiException(
                        "duplicate_commit_item",
                        "A provider item ID was reused within one Realtime session.");
                }

                var pending = _awaitingAcknowledgements.Dequeue();
                var segment = new Segment(pending.Ordinal, pending.OffsetMs);
                if (_segments.Count >= MaxRetainedItems)
                {
                    throw new OpenAiRealtimeApiException(
                        "too_many_pending_transcriptions",
                        "Too many committed turns are waiting for final transcripts.");
                }

                _segments.Add(committed.ItemId, segment);
                var ready = new List<TranscriptServerEvent>();
                if (_earlyFinals.Remove(committed.ItemId, out var early))
                {
                    AddMappedTranscript(early, segment, ready);
                }

                pending.Acknowledged.TrySetResult();
                return ready;
            }
        }

        public IReadOnlyList<TranscriptServerEvent> ObserveTranscript(
            TranscriptServerEvent transcript)
        {
            var providerItemId = transcript.ProviderItemId ?? transcript.Update.LineId;
            lock (_sync)
            {
                if (_recentCompletedItems.Contains(providerItemId))
                {
                    return [];
                }

                if (!_segments.TryGetValue(providerItemId, out var segment))
                {
                    if (transcript.Update.State is CaptionState.Partial)
                    {
                        return [transcript];
                    }

                    if (!_earlyFinals.ContainsKey(providerItemId))
                    {
                        if (_earlyFinals.Count >= MaxRetainedItems)
                        {
                            throw new OpenAiRealtimeApiException(
                                "too_many_unmapped_transcripts",
                                "Too many final transcripts arrived without commit acknowledgements.");
                        }

                        _earlyFinals.Add(providerItemId, transcript);
                    }

                    return [];
                }

                var ready = new List<TranscriptServerEvent>();
                AddMappedTranscript(transcript, segment, ready);
                return ready;
            }
        }

        public void MarkPublished(TranscriptServerEvent transcript)
        {
            if (transcript.Update.State is not CaptionState.Final)
            {
                return;
            }

            lock (_sync)
            {
                _publishedCompletedCount++;
            }
        }

        public void PublishCheckpoint()
        {
            lock (_sync)
            {
                if (_publishedCompletedCount >= _registeredCount &&
                    Interlocked.CompareExchange(ref _receiveOutcome, 1, 0) == 0)
                {
                    _allCompleted.TrySetResult();
                }
            }
        }

        public void TrackReceiveOperation(Task<string?> receiveOperation)
        {
            bool completionReady;
            lock (_sync)
            {
                completionReady = _publishedCompletedCount >= _registeredCount;
            }

            _ = receiveOperation.ContinueWith(
                static (_, state) =>
                    ((SessionCommittedTurnTracker)state!).MarkReceiveFailure(),
                this,
                CancellationToken.None,
                completionReady
                    ? TaskContinuationOptions.ExecuteSynchronously
                    : TaskContinuationOptions.OnlyOnFaulted |
                        TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public void MarkReceiveFailure() =>
            Interlocked.CompareExchange(ref _receiveOutcome, 2, 0);

        public void FinishReceiveMessage() =>
            Interlocked.CompareExchange(ref _receiveOutcome, 0, 2);

        private void AddMappedTranscript(
            TranscriptServerEvent transcript,
            Segment segment,
            ICollection<TranscriptServerEvent> ready)
        {
            var mapped = transcript with
            {
                Update = transcript.Update with
                {
                    SegmentOrdinal = segment.Ordinal,
                    SegmentStartMs = segment.OffsetMs
                }
            };
            if (mapped.Update.State is not CaptionState.Final)
            {
                ready.Add(mapped);
                return;
            }

            var providerItemId = mapped.ProviderItemId ?? mapped.Update.LineId;
            if (!_recentCompletedItems.Add(providerItemId))
            {
                return;
            }

            _recentCompletedOrder.Enqueue(providerItemId);
            if (_recentCompletedOrder.Count > MaxRetainedItems)
            {
                _recentCompletedItems.Remove(_recentCompletedOrder.Dequeue());
            }

            _segments.Remove(providerItemId);

            _completedCount++;
            if (_finalsByOrdinal.Count >= MaxRetainedItems)
            {
                throw new OpenAiRealtimeApiException(
                    "too_many_out_of_order_transcripts",
                    "Too many final transcripts are waiting for chronological delivery.");
            }

            _finalsByOrdinal[segment.Ordinal] = mapped;
            while (_nextFinalOrdinal is long next &&
                   _finalsByOrdinal.Remove(next, out var chronological))
            {
                ready.Add(chronological);
                _nextFinalOrdinal = next + 1;
            }

        }

        private static TaskCompletionSource CompletedSource()
        {
            var source = NewSource();
            source.SetResult();
            return source;
        }

        private static TaskCompletionSource NewSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed record Segment(long Ordinal, long OffsetMs);

        private sealed record PendingCommit(long Ordinal, long OffsetMs)
        {
            public TaskCompletionSource Acknowledged { get; } = NewSource();
        }
    }
}

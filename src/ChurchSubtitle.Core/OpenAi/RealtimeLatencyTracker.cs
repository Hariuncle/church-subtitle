using ChurchSubtitle.Core.Captions;

namespace ChurchSubtitle.Core.OpenAi;

public sealed record TranscriptionRuntimeMetrics(
    IReadOnlyList<long> FirstPartialLatenciesMs,
    IReadOnlyList<long> FinalLatenciesMs,
    int PartialUpdateCount,
    int TurnCount,
    long AudioDurationMs = 0);

public sealed class RealtimeLatencyTracker
{
    public const int MaxActiveTrackingItems = 1024;
    private const int MaxLatencySamples = 4096;
    private readonly object _sync = new();
    private DateTimeOffset _sessionStart;
    private readonly Dictionary<string, long> _speechStarts = [];
    private readonly Dictionary<string, long> _speechStops = [];
    private readonly HashSet<string> _partialLatencyCaptured = [];
    private readonly HashSet<string> _finalLatencyCaptured = [];
    private readonly List<long> _firstPartialLatencies = [];
    private readonly List<long> _finalLatencies = [];
    private int _partialUpdateCount;
    private int _turnCount;

    public RealtimeLatencyTracker(DateTimeOffset sessionStart)
    {
        _sessionStart = sessionStart;
    }

    public void BeginSession(DateTimeOffset sessionStart)
    {
        lock (_sync)
        {
            _sessionStart = sessionStart;
        }
    }

    public void Observe(OpenAiServerEvent serverEvent)
    {
        lock (_sync)
        {
            switch (serverEvent)
            {
                case SpeechBoundaryServerEvent
                {
                    Kind: SpeechBoundaryKind.Started
                } started:
                    EnsureTrackingCapacity(started.ItemId);
                    _speechStarts[started.ItemId] = started.AudioOffsetMs;
                    _turnCount++;
                    break;
                case SpeechBoundaryServerEvent
                {
                    Kind: SpeechBoundaryKind.Stopped
                } stopped:
                    EnsureTrackingCapacity(stopped.ItemId);
                    _speechStops[stopped.ItemId] = stopped.AudioOffsetMs;
                    break;
                case TranscriptServerEvent
                {
                    Update.State: CaptionState.Partial
                } partial:
                    _partialUpdateCount++;
                    TryCaptureLatency(
                        partial.Update.LineId,
                        partial.ReceivedAtUtc,
                        _speechStarts,
                        _partialLatencyCaptured,
                        _firstPartialLatencies);
                    break;
                case TranscriptServerEvent
                {
                    Update.State: CaptionState.Final
                } final:
                    TryCaptureLatency(
                        final.Update.LineId,
                        final.ReceivedAtUtc,
                        _speechStops,
                        _finalLatencyCaptured,
                        _finalLatencies);
                    RemoveTrackingState(final.Update.LineId);
                    break;
            }
        }
    }

    public int PendingTurnCount
    {
        get
        {
            lock (_sync)
            {
                return _speechStarts.Count;
            }
        }
    }

    public int ActiveTrackingItemCount
    {
        get
        {
            lock (_sync)
            {
                return _speechStarts.Keys
                    .Concat(_speechStops.Keys)
                    .Concat(_partialLatencyCaptured)
                    .Concat(_finalLatencyCaptured)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
            }
        }
    }

    public TranscriptionRuntimeMetrics CreateMetrics(long audioDurationMs = 0)
    {
        lock (_sync)
        {
            return new(
                _firstPartialLatencies.ToArray(),
                _finalLatencies.ToArray(),
                _partialUpdateCount,
                _turnCount,
                audioDurationMs);
        }
    }

    private void TryCaptureLatency(
        string itemId,
        DateTimeOffset receivedAtUtc,
        IReadOnlyDictionary<string, long> audioOffsets,
        ISet<string> capturedItems,
        ICollection<long> destination)
    {
        if (capturedItems.Contains(itemId) ||
            !audioOffsets.TryGetValue(itemId, out var audioOffsetMs))
        {
            return;
        }

        var receiveOffsetMs = (long)(receivedAtUtc - _sessionStart).TotalMilliseconds;
        var latencyMs = receiveOffsetMs - audioOffsetMs;
        if (latencyMs >= 0 && destination.Count < MaxLatencySamples)
        {
            destination.Add(latencyMs);
            capturedItems.Add(itemId);
        }
    }

    private void EnsureTrackingCapacity(string itemId)
    {
        if (_speechStarts.ContainsKey(itemId) ||
            _speechStops.ContainsKey(itemId))
        {
            return;
        }

        var trackedItems = _speechStarts.Keys
            .Concat(_speechStops.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (trackedItems.Length >= MaxActiveTrackingItems)
        {
            RemoveTrackingState(trackedItems[0]);
        }
    }

    private void RemoveTrackingState(string itemId)
    {
        _speechStarts.Remove(itemId);
        _speechStops.Remove(itemId);
        _partialLatencyCaptured.Remove(itemId);
        _finalLatencyCaptured.Remove(itemId);
    }
}

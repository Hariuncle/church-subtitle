using ChurchSubtitle.Core.Captions;
using ChurchSubtitle.Core.OpenAi;

namespace ChurchSubtitle.Core.Tests;

public sealed class RealtimeLatencyTrackerTests
{
    [Fact]
    public void Tracks_first_partial_and_final_latency_from_vad_audio_offsets()
    {
        var sessionStart = DateTimeOffset.Parse("2026-08-18T01:00:00Z");
        var tracker = new RealtimeLatencyTracker(sessionStart);
        tracker.Observe(new SpeechBoundaryServerEvent(
            "item-1", SpeechBoundaryKind.Started, 1000, sessionStart.AddMilliseconds(1100)));
        tracker.Observe(Transcript("item-1", CaptionState.Partial, sessionStart.AddMilliseconds(2500)));
        tracker.Observe(Transcript("item-1", CaptionState.Partial, sessionStart.AddMilliseconds(2700)));
        tracker.Observe(new SpeechBoundaryServerEvent(
            "item-1", SpeechBoundaryKind.Stopped, 3000, sessionStart.AddMilliseconds(3500)));
        tracker.Observe(Transcript("item-1", CaptionState.Final, sessionStart.AddMilliseconds(3800)));

        var metrics = tracker.CreateMetrics();

        Assert.Equal([1500], metrics.FirstPartialLatenciesMs);
        Assert.Equal([800], metrics.FinalLatenciesMs);
        Assert.Equal(2, metrics.PartialUpdateCount);
        Assert.Equal(1, metrics.TurnCount);
    }

    [Fact]
    public void Missing_vad_offsets_do_not_fabricate_latency_samples()
    {
        var sessionStart = DateTimeOffset.Parse("2026-08-18T01:00:00Z");
        var tracker = new RealtimeLatencyTracker(sessionStart);

        tracker.Observe(Transcript("item-1", CaptionState.Final, sessionStart.AddSeconds(1)));

        var metrics = tracker.CreateMetrics();
        Assert.Empty(metrics.FirstPartialLatenciesMs);
        Assert.Empty(metrics.FinalLatenciesMs);
    }

    [Fact]
    public void Many_finalized_turns_evict_tracking_state_and_bound_metric_samples()
    {
        var sessionStart = DateTimeOffset.Parse("2026-08-18T01:00:00Z");
        var tracker = new RealtimeLatencyTracker(sessionStart);

        for (var index = 0; index < 10_000; index++)
        {
            var itemId = $"item-{index}";
            tracker.Observe(new SpeechBoundaryServerEvent(
                itemId,
                SpeechBoundaryKind.Started,
                index * 10,
                sessionStart));
            tracker.Observe(Transcript(
                itemId,
                CaptionState.Partial,
                sessionStart.AddMilliseconds(index * 10 + 1)));
            tracker.Observe(new SpeechBoundaryServerEvent(
                itemId,
                SpeechBoundaryKind.Stopped,
                index * 10 + 5,
                sessionStart));
            tracker.Observe(Transcript(
                itemId,
                CaptionState.Final,
                sessionStart.AddMilliseconds(index * 10 + 6)));
        }

        var metrics = tracker.CreateMetrics();
        Assert.Equal(0, tracker.ActiveTrackingItemCount);
        Assert.InRange(metrics.FirstPartialLatenciesMs.Count, 1, 4096);
        Assert.InRange(metrics.FinalLatenciesMs.Count, 1, 4096);
    }

    [Fact]
    public void Missing_finals_cannot_grow_active_tracking_state_without_bound()
    {
        var sessionStart = DateTimeOffset.Parse("2026-08-18T01:00:00Z");
        var tracker = new RealtimeLatencyTracker(sessionStart);

        for (var index = 0; index < 10_000; index++)
        {
            var itemId = $"item-{index}";
            tracker.Observe(new SpeechBoundaryServerEvent(
                itemId,
                SpeechBoundaryKind.Started,
                index,
                sessionStart));
            tracker.Observe(Transcript(
                itemId,
                CaptionState.Partial,
                sessionStart.AddMilliseconds(index + 1)));
        }

        Assert.InRange(
            tracker.ActiveTrackingItemCount,
            0,
            RealtimeLatencyTracker.MaxActiveTrackingItems);
    }

    private static TranscriptServerEvent Transcript(
        string itemId,
        CaptionState state,
        DateTimeOffset timestamp) =>
        new(
            new CaptionUpdate(
                "session", 1, itemId, 1, "자막", state, "openai", timestamp),
            timestamp);
}

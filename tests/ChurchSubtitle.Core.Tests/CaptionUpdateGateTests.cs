using ChurchSubtitle.Core.Captions;

namespace ChurchSubtitle.Core.Tests;

public sealed class CaptionUpdateGateTests
{
    [Fact]
    public void Partial_updates_are_limited_to_five_per_second_per_line()
    {
        var gate = new CaptionUpdateGate(TimeSpan.FromMilliseconds(200));
        var start = new DateTimeOffset(2026, 8, 18, 1, 0, 0, TimeSpan.Zero);

        Assert.True(gate.ShouldPublish(Update(CaptionState.Partial, start)));
        Assert.False(gate.ShouldPublish(Update(CaptionState.Partial, start.AddMilliseconds(100))));
        Assert.True(gate.ShouldPublish(Update(CaptionState.Partial, start.AddMilliseconds(200))));
    }

    [Fact]
    public void Final_updates_are_never_throttled()
    {
        var gate = new CaptionUpdateGate(TimeSpan.FromMilliseconds(200));
        var start = new DateTimeOffset(2026, 8, 18, 1, 0, 0, TimeSpan.Zero);
        gate.ShouldPublish(Update(CaptionState.Partial, start));

        Assert.True(gate.ShouldPublish(Update(CaptionState.Final, start.AddMilliseconds(50))));
    }

    [Fact]
    public void Partial_limit_is_global_across_overlapping_lines()
    {
        var gate = new CaptionUpdateGate(TimeSpan.FromMilliseconds(200));
        var start = new DateTimeOffset(2026, 8, 18, 1, 0, 0, TimeSpan.Zero);

        Assert.True(gate.ShouldPublish(Update(CaptionState.Partial, start, "line-1")));
        Assert.False(gate.ShouldPublish(Update(
            CaptionState.Partial,
            start.AddMilliseconds(100),
            "line-2")));
    }

    private static CaptionUpdate Update(
        CaptionState state,
        DateTimeOffset timestamp,
        string lineId = "line") =>
        new("session", 1, lineId, 1, "자막", state, "openai", timestamp);
}

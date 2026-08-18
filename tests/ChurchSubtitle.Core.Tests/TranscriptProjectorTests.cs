using ChurchSubtitle.Core.Captions;

namespace ChurchSubtitle.Core.Tests;

public sealed class TranscriptProjectorTests
{
    [Fact]
    public void Delta_accumulates_text_and_increments_revision_for_the_same_item()
    {
        var projector = new TranscriptProjector("session-1", "openai");
        var firstTime = new DateTimeOffset(2026, 8, 18, 1, 0, 0, TimeSpan.Zero);

        var first = projector.ApplyDelta("item-1", "예수", firstTime);
        var second = projector.ApplyDelta("item-1", "님", firstTime.AddMilliseconds(100));

        Assert.Equal("예수", first.Text);
        Assert.Equal(CaptionState.Partial, first.State);
        Assert.Equal(1, first.Revision);
        Assert.Equal("예수님", second.Text);
        Assert.Equal(2, second.Revision);
        Assert.Equal(first.LineId, second.LineId);
        Assert.True(second.Sequence > first.Sequence);
    }

    [Fact]
    public void Completed_replaces_partial_text_with_authoritative_final_transcript()
    {
        var projector = new TranscriptProjector("session-1", "openai");
        var timestamp = new DateTimeOffset(2026, 8, 18, 1, 0, 0, TimeSpan.Zero);
        projector.ApplyDelta("item-1", "창세기 일장", timestamp);

        var completed = projector.ApplyCompleted(
            "item-1",
            "창세기 1장",
            timestamp.AddSeconds(1));

        Assert.Equal("창세기 1장", completed.Text);
        Assert.Equal(CaptionState.Final, completed.State);
        Assert.Equal(2, completed.Revision);
        Assert.Equal("openai", completed.Provider);
        Assert.Equal(0, projector.ActiveLineCount);
    }

    [Fact]
    public void Many_finalized_items_do_not_accumulate_line_state()
    {
        var projector = new TranscriptProjector("session-1", "openai");
        var timestamp = new DateTimeOffset(2026, 8, 19, 1, 0, 0, TimeSpan.Zero);

        for (var index = 0; index < 10_000; index++)
        {
            projector.ApplyDelta($"item-{index}", "부분", timestamp);
            projector.ApplyCompleted($"item-{index}", "완료", timestamp);
        }

        Assert.Equal(0, projector.ActiveLineCount);
    }

    [Fact]
    public void Active_partial_lines_are_bounded_without_revision_regression()
    {
        var projector = new TranscriptProjector("session-1", "openai");
        var timestamp = new DateTimeOffset(2026, 8, 19, 1, 0, 0, TimeSpan.Zero);
        var first = projector.ApplyDelta("item-0", "처음", timestamp);

        for (var index = 1; index <= TranscriptProjector.MaxActiveLineCount; index++)
        {
            projector.ApplyDelta($"item-{index}", "부분", timestamp);
        }

        var revisited = projector.ApplyDelta("item-0", "다시", timestamp);

        Assert.Equal(TranscriptProjector.MaxActiveLineCount, projector.ActiveLineCount);
        Assert.True(revisited.Revision > first.Revision);
    }
}

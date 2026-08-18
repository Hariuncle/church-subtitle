using System.Text;
using System.Text.Json;
using ChurchSubtitle.Core.Captions;

namespace ChurchSubtitle.Core.Tests;

public sealed class CaptionSinkTests
{
    [Fact]
    public async Task Jsonl_sink_writes_one_machine_readable_event_per_line()
    {
        var output = new StringBuilder();
        await using var writer = new StringWriter(output);
        var sink = new JsonlCaptionSink(writer);

        await sink.PublishAsync(Update(CaptionState.Partial, "예수"), CancellationToken.None);
        await sink.PublishAsync(Update(CaptionState.Final, "예수님"), CancellationToken.None);

        var lines = output.ToString().Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal("partial", first.RootElement.GetProperty("state").GetString());
        Assert.Equal("예수", first.RootElement.GetProperty("text").GetString());
        Assert.False(first.RootElement.TryGetProperty("audioStartMs", out _));
    }

    [Fact]
    public async Task Final_transcript_sink_ignores_partials_and_writes_only_final_text()
    {
        var output = new StringBuilder();
        await using var writer = new StringWriter(output);
        var sink = new FinalTranscriptSink(writer);

        await sink.PublishAsync(Update(CaptionState.Partial, "예수"), CancellationToken.None);
        await sink.PublishAsync(Update(CaptionState.Final, "예수님"), CancellationToken.None);
        await sink.CompleteAsync(CancellationToken.None);

        Assert.Equal($"예수님{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task Final_transcript_sink_orders_completed_items_by_audio_start()
    {
        var output = new StringBuilder();
        await using var writer = new StringWriter(output);
        var sink = new FinalTranscriptSink(writer);

        await sink.PublishAsync(
            Update(CaptionState.Final, "두 번째") with { AudioStartMs = 2_000, Sequence = 1 },
            CancellationToken.None);
        await sink.PublishAsync(
            Update(CaptionState.Final, "첫 번째") with { AudioStartMs = 1_000, Sequence = 2 },
            CancellationToken.None);
        await sink.CompleteAsync(CancellationToken.None);

        Assert.Equal(
            $"첫 번째{Environment.NewLine}두 번째{Environment.NewLine}",
            output.ToString());
    }

    [Fact]
    public async Task Final_transcript_sink_orders_manual_commit_segments_by_ordinal()
    {
        var output = new StringBuilder();
        await using var writer = new StringWriter(output);
        var sink = new FinalTranscriptSink(writer);

        await sink.PublishAsync(
            Update(CaptionState.Final, "둘째 segment") with
            {
                SegmentOrdinal = 1,
                Sequence = 1
            },
            CancellationToken.None);
        await sink.PublishAsync(
            Update(CaptionState.Final, "첫째 segment") with
            {
                SegmentOrdinal = 0,
                Sequence = 2
            },
            CancellationToken.None);
        await sink.CompleteAsync(CancellationToken.None);

        Assert.Equal(
            $"첫째 segment{Environment.NewLine}둘째 segment{Environment.NewLine}",
            output.ToString());
    }

    [Fact]
    public async Task Console_sink_renders_partial_in_place_and_final_with_newline()
    {
        var output = new StringBuilder();
        await using var writer = new StringWriter(output);
        var sink = new ConsoleCaptionSink(writer);

        await sink.PublishAsync(Update(CaptionState.Partial, "예수"), CancellationToken.None);
        await sink.PublishAsync(Update(CaptionState.Final, "예수님"), CancellationToken.None);

        Assert.Contains("\r예수", output.ToString());
        Assert.EndsWith($"예수님{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task Final_journal_flushes_each_final_immediately()
    {
        var output = new StringBuilder();
        await using var writer = new StringWriter(output);
        var sink = new FinalTranscriptJournalSink(writer);

        await sink.PublishAsync(Update(CaptionState.Partial, "예수"), CancellationToken.None);
        await sink.PublishAsync(Update(CaptionState.Final, "예수님"), CancellationToken.None);

        Assert.Equal($"예수님{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task Composite_sink_fans_out_to_every_sink()
    {
        var first = new RecordingSink();
        var second = new RecordingSink();
        var sink = new CompositeCaptionSink(first, second);

        await sink.PublishAsync(Update(CaptionState.Final, "말씀"), CancellationToken.None);

        Assert.Single(first.Updates);
        Assert.Single(second.Updates);
    }

    private static CaptionUpdate Update(CaptionState state, string text) =>
        new(
            "session", 1, "line", 1, text, state, "openai",
            DateTimeOffset.Parse("2026-08-18T01:00:00Z"));

    private sealed class RecordingSink : ICaptionSink
    {
        public List<CaptionUpdate> Updates { get; } = [];

        public ValueTask PublishAsync(CaptionUpdate update, CancellationToken cancellationToken)
        {
            Updates.Add(update);
            return ValueTask.CompletedTask;
        }
    }
}

using ChurchSubtitle.Core.Evaluation;

namespace ChurchSubtitle.Core.Tests;

public sealed class TranscriptionEvaluationTests
{
    [Fact]
    public void Cer_ignores_spacing_and_punctuation_but_counts_character_substitutions()
    {
        Assert.Equal(0, TranscriptionEvaluation.CharacterErrorRate(
            "창세기 1장입니다.",
            "창세기1장입니다"));
        Assert.Equal(1d / 3d, TranscriptionEvaluation.CharacterErrorRate(
            "하나님",
            "하나민"),
            precision: 10);
    }

    [Fact]
    public void Keyword_accuracy_counts_reference_occurrences_and_ignores_unused_terms()
    {
        var accuracy = TranscriptionEvaluation.KeywordAccuracy(
            "예수님 예수님 창세기 말씀",
            "예수님 창세기 말씀",
            ["예수님", "창세기", "출애굽기"]);

        Assert.Equal(2d / 3d, accuracy, precision: 10);
        Assert.Equal(3, TranscriptionEvaluation.KeywordExpectedOccurrenceCount(
            "예수님 예수님 창세기 말씀",
            ["예수님", "창세기", "출애굽기"]));
    }

    [Fact]
    public void Keyword_accuracy_is_zero_when_reference_has_no_target_occurrences()
    {
        Assert.Equal(0, TranscriptionEvaluation.KeywordAccuracy(
            "일반 안내",
            "일반 안내",
            ["예수님"]));
    }

    [Fact]
    public void Latency_summary_reports_nearest_rank_p50_and_p95()
    {
        var summary = TranscriptionEvaluation.SummarizeLatencies(
            [100, 200, 300, 400, 500]);

        Assert.Equal(300, summary.P50Milliseconds);
        Assert.Equal(500, summary.P95Milliseconds);
    }

    [Fact]
    public void Empty_latency_set_produces_no_percentiles()
    {
        var summary = TranscriptionEvaluation.SummarizeLatencies([]);

        Assert.Null(summary.P50Milliseconds);
        Assert.Null(summary.P95Milliseconds);
    }
}

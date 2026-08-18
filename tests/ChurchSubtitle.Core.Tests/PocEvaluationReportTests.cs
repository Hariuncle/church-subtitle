using ChurchSubtitle.Core.Evaluation;
using ChurchSubtitle.Core.OpenAi;

namespace ChurchSubtitle.Core.Tests;

public sealed class PocEvaluationReportTests
{
    [Fact]
    public void Automated_criteria_pass_when_accuracy_latency_and_connection_targets_are_met()
    {
        var report = PocEvaluationReport.Create(
            reference: "예수님 말씀",
            hypothesis: "예수님 말씀",
            keywords: ["예수님"],
            runtimeMetrics: new TranscriptionRuntimeMetrics(
                [800, 1000],
                [1200, 2000],
                PartialUpdateCount: 5,
                TurnCount: 2,
                AudioDurationMs: 900_000),
            reconnectCount: 0);

        Assert.True(report.AutomatedCriteriaPassed);
        Assert.True(report.CerPassed);
        Assert.True(report.KeywordAccuracyPassed);
        Assert.Equal(1, report.KeywordExpectedOccurrenceCount);
        Assert.True(report.PartialLatencyPassed);
        Assert.True(report.FinalLatencyPassed);
        Assert.True(report.ConnectionStabilityPassed);
        Assert.True(report.DurationPassed);
        Assert.True(report.ManualHallucinationReviewRequired);
    }

    [Fact]
    public void No_keyword_samples_fail_the_keyword_criterion()
    {
        var report = PocEvaluationReport.Create(
            "일반 안내",
            "일반 안내",
            ["예수님"],
            new TranscriptionRuntimeMetrics([500], [500], 1, 1, 900_000),
            reconnectCount: 0);

        Assert.Equal(0, report.KeywordExpectedOccurrenceCount);
        Assert.False(report.KeywordAccuracyPassed);
        Assert.False(report.AutomatedCriteriaPassed);
    }

    [Fact]
    public void Missing_latency_samples_fail_the_automated_latency_criteria()
    {
        var report = PocEvaluationReport.Create(
            "말씀",
            "말씀",
            [],
            new TranscriptionRuntimeMetrics([], [], 0, 0, 900_000),
            reconnectCount: 0);

        Assert.False(report.PartialLatencyPassed);
        Assert.False(report.FinalLatencyPassed);
        Assert.False(report.AutomatedCriteriaPassed);
    }

    [Fact]
    public void Short_audio_fails_the_required_fifteen_minute_criterion()
    {
        var report = PocEvaluationReport.Create(
            "예수님 말씀",
            "예수님 말씀",
            ["예수님"],
            new TranscriptionRuntimeMetrics([500], [500], 1, 1, 420_000),
            reconnectCount: 0);

        Assert.False(report.DurationPassed);
        Assert.False(report.AutomatedCriteriaPassed);
    }
}

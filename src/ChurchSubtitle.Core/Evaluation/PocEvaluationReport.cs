using ChurchSubtitle.Core.OpenAi;

namespace ChurchSubtitle.Core.Evaluation;

public sealed record PocEvaluationReport(
    double CharacterErrorRate,
    double KeywordAccuracy,
    int KeywordExpectedOccurrenceCount,
    LatencySummary FirstPartialLatency,
    LatencySummary FinalLatency,
    int PartialUpdateCount,
    int TurnCount,
    int ReconnectCount,
    long AudioDurationMs,
    bool CerPassed,
    bool KeywordAccuracyPassed,
    bool PartialLatencyPassed,
    bool FinalLatencyPassed,
    bool ConnectionStabilityPassed,
    bool DurationPassed,
    bool AutomatedCriteriaPassed,
    bool ManualHallucinationReviewRequired)
{
    public static PocEvaluationReport Create(
        string reference,
        string hypothesis,
        IReadOnlyCollection<string> keywords,
        TranscriptionRuntimeMetrics runtimeMetrics,
        int reconnectCount)
    {
        var cer = TranscriptionEvaluation.CharacterErrorRate(reference, hypothesis);
        var keywordAccuracy = TranscriptionEvaluation.KeywordAccuracy(
            reference,
            hypothesis,
            keywords);
        var keywordExpectedOccurrenceCount =
            TranscriptionEvaluation.KeywordExpectedOccurrenceCount(reference, keywords);
        var partialLatency = TranscriptionEvaluation.SummarizeLatencies(
            runtimeMetrics.FirstPartialLatenciesMs);
        var finalLatency = TranscriptionEvaluation.SummarizeLatencies(
            runtimeMetrics.FinalLatenciesMs);
        var cerPassed = cer <= 0.10;
        var keywordsPassed = keywordExpectedOccurrenceCount > 0 && keywordAccuracy >= 0.95;
        var partialPassed = partialLatency.P95Milliseconds is <= 2000;
        var finalPassed = finalLatency.P95Milliseconds is <= 3000;
        var connectionPassed = reconnectCount == 0;
        var durationPassed = runtimeMetrics.AudioDurationMs is >= 895_000 and <= 905_000;

        return new PocEvaluationReport(
            cer,
            keywordAccuracy,
            keywordExpectedOccurrenceCount,
            partialLatency,
            finalLatency,
            runtimeMetrics.PartialUpdateCount,
            runtimeMetrics.TurnCount,
            reconnectCount,
            runtimeMetrics.AudioDurationMs,
            cerPassed,
            keywordsPassed,
            partialPassed,
            finalPassed,
            connectionPassed,
            durationPassed,
            cerPassed &&
            keywordsPassed &&
            partialPassed &&
            finalPassed &&
            connectionPassed &&
            durationPassed,
            ManualHallucinationReviewRequired: true);
    }
}

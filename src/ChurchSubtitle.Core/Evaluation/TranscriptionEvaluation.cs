using System.Globalization;
using System.Text;

namespace ChurchSubtitle.Core.Evaluation;

public sealed record LatencySummary(long? P50Milliseconds, long? P95Milliseconds);

public static class TranscriptionEvaluation
{
    public static double CharacterErrorRate(string reference, string hypothesis)
    {
        var expected = Normalize(reference);
        var actual = Normalize(hypothesis);
        if (expected.Length == 0)
        {
            return actual.Length == 0 ? 0 : 1;
        }

        return (double)LevenshteinDistance(expected, actual) / expected.Length;
    }

    public static double KeywordAccuracy(
        string reference,
        string hypothesis,
        IReadOnlyCollection<string> keywords)
    {
        var normalizedReference = Normalize(reference);
        var normalizedHypothesis = Normalize(hypothesis);
        var expected = KeywordExpectedOccurrenceCount(reference, keywords);
        var matches = 0;
        foreach (var keyword in keywords)
        {
            var normalizedKeyword = Normalize(keyword);
            if (normalizedKeyword.Length == 0)
            {
                continue;
            }

            var expectedOccurrences = CountOccurrences(normalizedReference, normalizedKeyword);
            matches += Math.Min(
                expectedOccurrences,
                CountOccurrences(normalizedHypothesis, normalizedKeyword));
        }

        return expected == 0 ? 0 : (double)matches / expected;
    }

    public static int KeywordExpectedOccurrenceCount(
        string reference,
        IReadOnlyCollection<string> keywords)
    {
        var normalizedReference = Normalize(reference);
        return keywords.Sum(keyword =>
        {
            var normalizedKeyword = Normalize(keyword);
            return normalizedKeyword.Length == 0
                ? 0
                : CountOccurrences(normalizedReference, normalizedKeyword);
        });
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    public static LatencySummary SummarizeLatencies(IReadOnlyCollection<long> milliseconds)
    {
        if (milliseconds.Count == 0)
        {
            return new LatencySummary(null, null);
        }

        var sorted = milliseconds.Order().ToArray();
        return new LatencySummary(
            NearestRank(sorted, 0.50),
            NearestRank(sorted, 0.95));
    }

    private static long NearestRank(IReadOnlyList<long> sorted, double percentile)
    {
        var index = Math.Max(0, (int)Math.Ceiling(percentile * sorted.Count) - 1);
        return sorted[index];
    }

    private static string Normalize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.GetUnicodeCategory(character) is
                UnicodeCategory.UppercaseLetter or
                UnicodeCategory.LowercaseLetter or
                UnicodeCategory.TitlecaseLetter or
                UnicodeCategory.ModifierLetter or
                UnicodeCategory.OtherLetter or
                UnicodeCategory.DecimalDigitNumber)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}

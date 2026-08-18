namespace ChurchSubtitle.Core.Streaming;

public sealed class ReconnectPolicy
{
    private readonly int _maxAttempts;

    public ReconnectPolicy(int maxAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 0);
        _maxAttempts = maxAttempts;
    }

    public bool CanRetry(int attempt) => attempt > 0 && attempt <= _maxAttempts;

    public TimeSpan GetDelay(int attempt)
    {
        if (!CanRetry(attempt))
        {
            throw new ArgumentOutOfRangeException(nameof(attempt));
        }

        return TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
    }
}

namespace ChurchSubtitle.Core.Captions;

public sealed class CaptionUpdateGate
{
    private readonly TimeSpan _minimumPartialInterval;
    private DateTimeOffset? _lastPartial;

    public CaptionUpdateGate(TimeSpan minimumPartialInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            minimumPartialInterval,
            TimeSpan.Zero);
        _minimumPartialInterval = minimumPartialInterval;
    }

    public bool ShouldPublish(CaptionUpdate update)
    {
        if (update.State is not CaptionState.Partial)
        {
            return true;
        }

        if (_lastPartial is { } last &&
            update.EmittedAtUtc - last < _minimumPartialInterval)
        {
            return false;
        }

        _lastPartial = update.EmittedAtUtc;
        return true;
    }
}

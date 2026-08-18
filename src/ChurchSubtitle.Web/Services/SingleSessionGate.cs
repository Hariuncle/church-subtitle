namespace ChurchSubtitle.Web.Services;

public sealed class SingleSessionGate
{
    private int _entered;

    public IDisposable? TryEnter()
    {
        if (Interlocked.CompareExchange(ref _entered, 1, 0) != 0)
        {
            return null;
        }

        return new Lease(this);
    }

    private void Release() => Interlocked.Exchange(ref _entered, 0);

    private sealed class Lease(SingleSessionGate gate) : IDisposable
    {
        private SingleSessionGate? _gate = gate;

        public void Dispose()
        {
            var gateToRelease = Interlocked.Exchange(ref _gate, null);
            gateToRelease?.Release();
        }
    }
}

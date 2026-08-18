using ChurchSubtitle.Web.Services;

namespace ChurchSubtitle.Core.Tests;

public sealed class SingleSessionGateTests
{
    [Fact]
    public void Rejects_a_second_lease_while_the_first_is_held()
    {
        var gate = new SingleSessionGate();

        using var first = gate.TryEnter();

        Assert.NotNull(first);
        Assert.Null(gate.TryEnter());
    }

    [Fact]
    public void Allows_another_lease_after_disposal()
    {
        var gate = new SingleSessionGate();
        var first = Assert.IsAssignableFrom<IDisposable>(gate.TryEnter());

        first.Dispose();

        using var second = gate.TryEnter();
        Assert.NotNull(second);
    }

    [Fact]
    public void Releasing_a_lease_is_idempotent()
    {
        var gate = new SingleSessionGate();
        var first = Assert.IsAssignableFrom<IDisposable>(gate.TryEnter());

        first.Dispose();
        var second = Assert.IsAssignableFrom<IDisposable>(gate.TryEnter());

        first.Dispose();
        Assert.Null(gate.TryEnter());

        second.Dispose();
        using var next = gate.TryEnter();
        Assert.NotNull(next);
    }

    [Fact]
    public void Only_one_concurrent_caller_acquires_a_lease()
    {
        const int callerCount = 32;
        var gate = new SingleSessionGate();
        var timeout = TimeSpan.FromSeconds(5);
        using var ready = new CountdownEvent(callerCount);
        using var start = new ManualResetEventSlim();

        var leases = new IDisposable?[callerCount];
        var observedStart = new bool[callerCount];
        var threads = Enumerable.Range(0, callerCount)
            .Select(index => new Thread(() =>
            {
                ready.Signal();
                observedStart[index] = start.Wait(timeout);
                if (observedStart[index])
                {
                    leases[index] = gate.TryEnter();
                }
            }))
            .ToArray();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        var allCallersReady = ready.Wait(timeout);
        start.Set();
        var allCallersCompleted = true;
        foreach (var thread in threads)
        {
            allCallersCompleted &= thread.Join(timeout);
        }

        Assert.True(allCallersReady, "All concurrent callers should reach the start barrier within five seconds.");
        Assert.True(allCallersCompleted, "All concurrent callers should finish within five seconds of release.");
        Assert.All(observedStart, observed =>
            Assert.True(observed, "Every concurrent caller should observe the released start barrier."));
        var lease = Assert.Single(leases.Where(candidate => candidate is not null));
        lease!.Dispose();
    }
}

using ChurchSubtitle.Core.Streaming;

namespace ChurchSubtitle.Core.Tests;

public sealed class StreamingPolicyTests
{
    [Fact]
    public void Pcm24k_mono_sixteen_bit_uses_4800_byte_chunks_for_100ms()
    {
        Assert.Equal(4800, PcmStreamFormat.Pcm24KhzMono16Bit.BytesPerChunk(
            TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void Reconnect_policy_retries_three_times_within_ten_seconds()
    {
        var policy = new ReconnectPolicy(maxAttempts: 3);

        Assert.True(policy.CanRetry(1));
        Assert.True(policy.CanRetry(2));
        Assert.True(policy.CanRetry(3));
        Assert.False(policy.CanRetry(4));
        Assert.Equal(TimeSpan.FromSeconds(1), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(4), policy.GetDelay(3));
        Assert.True(
            policy.GetDelay(1) + policy.GetDelay(2) + policy.GetDelay(3) <
            TimeSpan.FromSeconds(10));
    }
}

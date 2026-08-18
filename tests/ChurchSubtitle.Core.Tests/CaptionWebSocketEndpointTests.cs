using ChurchSubtitle.Web.Endpoints;

namespace ChurchSubtitle.Core.Tests;

public sealed class CaptionWebSocketEndpointTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(4_800)]
    [InlineData(96_000)]
    public void ValidateBinaryFrameLength_AcceptsNonEmptyEvenPcmFrames(int length)
    {
        Assert.Null(CaptionWebSocketEndpoint.ValidateBinaryFrameLength(length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4_799)]
    [InlineData(96_001)]
    public void ValidateBinaryFrameLength_RejectsEmptyOddOrOversizedFrames(int length)
    {
        Assert.NotNull(CaptionWebSocketEndpoint.ValidateBinaryFrameLength(length));
    }
}

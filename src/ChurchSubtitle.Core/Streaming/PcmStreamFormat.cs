namespace ChurchSubtitle.Core.Streaming;

public sealed record PcmStreamFormat(int SampleRate, int Channels, int BitsPerSample)
{
    public static PcmStreamFormat Pcm24KhzMono16Bit { get; } = new(24000, 1, 16);

    public int BytesPerSecond => SampleRate * Channels * (BitsPerSample / 8);

    public int BytesPerChunk(TimeSpan duration)
    {
        var bytes = BytesPerSecond * duration.TotalSeconds;
        return checked((int)Math.Round(bytes, MidpointRounding.AwayFromZero));
    }
}

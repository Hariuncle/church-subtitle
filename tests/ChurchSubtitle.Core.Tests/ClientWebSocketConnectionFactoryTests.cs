using ChurchSubtitle.Core.OpenAi;

namespace ChurchSubtitle.Core.Tests;

public sealed class ClientWebSocketConnectionFactoryTests
{
    [Fact]
    public void Uses_the_transcription_intent_endpoint_without_a_model_query()
    {
        Assert.Equal(
            new Uri("wss://api.openai.com/v1/realtime?intent=transcription"),
            ClientWebSocketConnectionFactory.Endpoint);
    }
}

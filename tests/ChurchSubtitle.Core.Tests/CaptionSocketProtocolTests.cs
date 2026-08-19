using ChurchSubtitle.Web.Protocol;

namespace ChurchSubtitle.Core.Tests;

public sealed class CaptionSocketProtocolTests
{
    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    public void Parses_start_command_with_supported_delay(string delay)
    {
        var command = CaptionSocketProtocol.Parse($$"""
            { "type": "start", "delay": "{{delay}}" }
            """);

        var start = Assert.IsType<StartCaptionSocketCommand>(command);
        Assert.Equal(delay, start.Delay);
    }

    [Fact]
    public void Parses_end_command()
    {
        var command = CaptionSocketProtocol.Parse("""
            { "type": "end" }
            """);

        Assert.IsType<EndCaptionSocketCommand>(command);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"type\": \"pause\" }")]
    [InlineData("{ \"type\": \"START\", \"delay\": \"low\" }")]
    [InlineData("{ \"type\": \"start\" }")]
    [InlineData("{ \"type\": \"start\", \"delay\": \"xhigh\" }")]
    [InlineData("{ \"type\": \"start\", \"delay\": \"LOW\" }")]
    [InlineData("{ \"type\": \"start\", \"delay\": 1 }")]
    public void Rejects_unknown_or_incomplete_commands(string json)
    {
        Assert.Throws<InvalidDataException>(() => CaptionSocketProtocol.Parse(json));
    }

    [Theory]
    [InlineData("{ \"Type\": \"start\", \"delay\": \"low\" }")]
    [InlineData("{ \"type\": \"start\", \"Delay\": \"low\" }")]
    public void Rejects_wrong_property_name_casing(string json)
    {
        Assert.Throws<InvalidDataException>(() => CaptionSocketProtocol.Parse(json));
    }

    [Theory]
    [InlineData("{ \"type\": 1 }")]
    [InlineData("{ \"type\": null }")]
    [InlineData("{ \"type\": true }")]
    public void Rejects_non_string_type(string json)
    {
        Assert.Throws<InvalidDataException>(() => CaptionSocketProtocol.Parse(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{ \"type\": ")]
    public void Rejects_malformed_or_non_object_json(string json)
    {
        Assert.Throws<InvalidDataException>(() => CaptionSocketProtocol.Parse(json));
    }

    [Fact]
    public void Rejects_null_command()
    {
        Assert.Throws<InvalidDataException>(() => CaptionSocketProtocol.Parse(null));
    }

    [Fact]
    public void Rejects_command_larger_than_4096_characters()
    {
        var json = "{ \"type\": \"end\" }".PadRight(4097);

        Assert.Throws<InvalidDataException>(() => CaptionSocketProtocol.Parse(json));
    }

    [Fact]
    public void Allows_command_at_4096_character_limit()
    {
        var json = "{ \"type\": \"end\" }".PadRight(4096);

        Assert.IsType<EndCaptionSocketCommand>(CaptionSocketProtocol.Parse(json));
    }

    [Theory]
    [InlineData("{ \"type\": \"start\", \"delay\": \"low\", \"extra\": true }")]
    [InlineData("{ \"type\": \"end\", \"delay\": \"low\" }")]
    [InlineData("{ \"type\": \"end\", \"extra\": true }")]
    [InlineData("{ \"type\": \"end\", \"type\": \"end\" }")]
    [InlineData("{ \"type\": \"start\", \"delay\": \"low\", \"delay\": \"low\" }")]
    public void Rejects_unexpected_payload(string json)
    {
        Assert.Throws<InvalidDataException>(() => CaptionSocketProtocol.Parse(json));
    }
}

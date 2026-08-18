using ChurchSubtitle.Core.Configuration;

namespace ChurchSubtitle.Core.Tests;

public sealed class PocCommandParserTests
{
    [Fact]
    public void Parses_transcribe_command()
    {
        var parsed = PocCommandParser.Parse(
        [
            "transcribe",
            "--pcm", "service.pcm",
            "--delay", "medium",
            "--output", "runs/medium",
            "--keywords", "config/keywords.txt",
            "--prompt", "한국어 교회 예배"
        ]);

        var command = Assert.IsType<TranscribeCommandOptions>(parsed);
        Assert.Equal("service.pcm", command.PcmPath);
        Assert.Equal("medium", command.Delay);
        Assert.Equal("runs/medium", command.OutputDirectory);
        Assert.Equal("config/keywords.txt", command.KeywordsPath);
        Assert.Equal("한국어 교회 예배", command.Prompt);
    }

    [Fact]
    public void Parses_evaluate_command()
    {
        var parsed = PocCommandParser.Parse(
        [
            "evaluate",
            "--reference", "reference.txt",
            "--hypothesis", "final.txt",
            "--keywords", "keywords.txt",
            "--runtime-metrics", "runtime-metrics.json",
            "--output", "evaluation.json"
        ]);

        var command = Assert.IsType<EvaluateCommandOptions>(parsed);
        Assert.Equal("reference.txt", command.ReferencePath);
        Assert.Equal("runtime-metrics.json", command.RuntimeMetricsPath);
    }

    [Fact]
    public void Rejects_missing_required_option()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            PocCommandParser.Parse(["transcribe", "--delay", "low"]));

        Assert.Contains("--pcm", error.Message);
    }
}

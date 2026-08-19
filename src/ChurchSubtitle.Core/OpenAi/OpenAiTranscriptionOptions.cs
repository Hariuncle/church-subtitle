namespace ChurchSubtitle.Core.OpenAi;

public sealed record OpenAiTranscriptionOptions
{
    public OpenAiTranscriptionOptions(
        string Delay,
        string Prompt,
        IReadOnlyList<string> Keywords)
    {
        if (Delay is not ("low" or "medium" or "high"))
        {
            throw new ArgumentException(
                "The PoC supports only 'low', 'medium', or 'high' delay.",
                nameof(Delay));
        }

        foreach (var keyword in Keywords)
        {
            if (keyword.IndexOfAny(['<', '>', '\r', '\n']) >= 0)
            {
                throw new ArgumentException(
                    "Keywords cannot contain <, >, carriage returns, or line feeds.",
                    nameof(Keywords));
            }
        }

        this.Delay = Delay;
        this.Prompt = Prompt;
        this.Keywords = Keywords;
    }

    public string Delay { get; }

    public string Prompt { get; }

    public IReadOnlyList<string> Keywords { get; }
}

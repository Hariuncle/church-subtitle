namespace ChurchSubtitle.Core.Captions;

public sealed class TranscriptProjector
{
    public const int MaxActiveLineCount = 512;
    private readonly string _sessionId;
    private readonly string _provider;
    private readonly Dictionary<string, LineState> _lines = [];
    private long _sequence;
    private int _revision;

    public TranscriptProjector(string sessionId, string provider)
    {
        _sessionId = sessionId;
        _provider = provider;
    }

    public int ActiveLineCount => _lines.Count;

    public CaptionUpdate ApplyDelta(string itemId, string delta, DateTimeOffset emittedAtUtc)
    {
        var line = GetOrCreate(itemId);
        line.Text += delta;
        line.Revision = NextRevision();
        return CreateUpdate(itemId, line, CaptionState.Partial, emittedAtUtc);
    }

    public void RegisterSpeechStart(string itemId, long audioStartMs)
    {
        GetOrCreate(itemId).AudioStartMs = audioStartMs;
    }

    public CaptionUpdate ApplyCompleted(
        string itemId,
        string transcript,
        DateTimeOffset emittedAtUtc)
    {
        var line = GetOrCreate(itemId);
        line.Text = transcript;
        line.Revision = NextRevision();
        var update = CreateUpdate(itemId, line, CaptionState.Final, emittedAtUtc);
        _lines.Remove(itemId);
        return update;
    }

    private LineState GetOrCreate(string itemId)
    {
        if (!_lines.TryGetValue(itemId, out var line))
        {
            if (_lines.Count >= MaxActiveLineCount)
            {
                _lines.Remove(_lines.Keys.First());
            }

            line = new LineState();
            _lines[itemId] = line;
        }

        return line;
    }

    private int NextRevision()
    {
        if (_revision == int.MaxValue)
        {
            throw new InvalidOperationException("Caption revision limit was reached.");
        }

        return ++_revision;
    }

    private CaptionUpdate CreateUpdate(
        string itemId,
        LineState line,
        CaptionState state,
        DateTimeOffset emittedAtUtc) =>
        new(
            _sessionId,
            ++_sequence,
            itemId,
            line.Revision,
            line.Text,
            state,
            _provider,
            emittedAtUtc,
            line.AudioStartMs);

    private sealed class LineState
    {
        public string Text { get; set; } = string.Empty;

        public int Revision { get; set; }

        public long? AudioStartMs { get; set; }
    }
}

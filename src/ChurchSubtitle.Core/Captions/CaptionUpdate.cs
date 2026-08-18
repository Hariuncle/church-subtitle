using System.Text.Json.Serialization;

namespace ChurchSubtitle.Core.Captions;

public enum CaptionState
{
    Partial,
    Final,
    Clear,
    Status
}

public sealed record CaptionUpdate(
    string SessionId,
    long Sequence,
    string LineId,
    int Revision,
    string Text,
    CaptionState State,
    string Provider,
    DateTimeOffset EmittedAtUtc,
    [property: JsonIgnore]
    long? AudioStartMs = null,
    [property: JsonIgnore]
    long? SegmentOrdinal = null,
    [property: JsonIgnore]
    long? SegmentStartMs = null);

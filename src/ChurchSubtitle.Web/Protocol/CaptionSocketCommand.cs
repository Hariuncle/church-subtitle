namespace ChurchSubtitle.Web.Protocol;

public abstract record CaptionSocketCommand;

public sealed record StartCaptionSocketCommand(string Delay) : CaptionSocketCommand;

public sealed record EndCaptionSocketCommand : CaptionSocketCommand;

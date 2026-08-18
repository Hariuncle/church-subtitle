namespace ChurchSubtitle.Core.OpenAi;

public interface IRealtimeConnection : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);

    Task SendTextAsync(string message, CancellationToken cancellationToken);

    Task<string?> ReceiveTextAsync(CancellationToken cancellationToken);
}

public interface IRealtimeConnectionFactory
{
    IRealtimeConnection Create(string apiKey);
}

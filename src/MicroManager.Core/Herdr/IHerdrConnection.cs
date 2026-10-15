namespace MicroManager.Core.Herdr;

/// A connection to Herdr's local socket API: newline-delimited JSON, exactly
/// one request per connection (except `events.subscribe`, which pushes events
/// until the client disconnects).
public interface IHerdrConnection : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task WriteLineAsync(string json, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken = default);
}

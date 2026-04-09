using System.Threading.Channels;

namespace ClankYankers.Daemon.Contracts;

internal interface IDaemonInteractiveSession : IAsyncDisposable
{
    string SessionId { get; }

    ChannelReader<string> Output { get; }

    Task<int?> Completion { get; }

    Task WriteInputAsync(string data, CancellationToken cancellationToken);

    Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

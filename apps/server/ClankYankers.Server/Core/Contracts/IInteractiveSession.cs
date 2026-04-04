using ClankYankers.Server.Core.Models;
using System.Threading.Channels;

namespace ClankYankers.Server.Core.Contracts;

public interface IInteractiveSession : IAsyncDisposable
{
    string SessionId { get; }

    ChannelReader<TerminalOutputChunk> Output { get; }

    Task<int?> Completion { get; }

    Task WriteInputAsync(string data, CancellationToken cancellationToken);

    Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

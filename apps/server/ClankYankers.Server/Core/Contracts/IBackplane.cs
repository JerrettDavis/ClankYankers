using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Core.Contracts;

public interface IBackplane
{
    string Kind { get; }

    Task<IInteractiveSession> StartAsync(
        string sessionId,
        HostConfig host,
        LaunchSpec launchSpec,
        CancellationToken cancellationToken);
}

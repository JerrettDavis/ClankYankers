using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Core.Contracts;

public interface IPtyProcessFactory
{
    Task<IInteractiveSession> StartAsync(
        string sessionId,
        LaunchSpec launchSpec,
        CancellationToken cancellationToken);
}

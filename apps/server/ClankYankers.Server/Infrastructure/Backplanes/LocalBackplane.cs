using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Infrastructure.Backplanes;

public sealed class LocalBackplane(IPtyProcessFactory ptyProcessFactory) : IBackplane
{
    public string Kind => "local";

    public Task<IInteractiveSession> StartAsync(
        string sessionId,
        HostConfig host,
        LaunchSpec launchSpec,
        CancellationToken cancellationToken) =>
        ptyProcessFactory.StartAsync(sessionId, launchSpec, cancellationToken);
}

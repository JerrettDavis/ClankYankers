using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Infrastructure.Pty;

public sealed class WindowsConPtyProcessFactory : IPtyProcessFactory
{
    public Task<IInteractiveSession> StartAsync(
        string sessionId,
        LaunchSpec launchSpec,
        CancellationToken cancellationToken) =>
        WindowsConPtyProcess.StartAsync(sessionId, launchSpec, cancellationToken);
}

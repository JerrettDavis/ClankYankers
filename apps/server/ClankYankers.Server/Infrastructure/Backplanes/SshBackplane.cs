using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Infrastructure.Ssh;

namespace ClankYankers.Server.Infrastructure.Backplanes;

public sealed class SshBackplane(ILogger<SshBackplane> logger) : IBackplane
{
    public string Kind => "ssh";

    public async Task<IInteractiveSession> StartAsync(
        string sessionId,
        HostConfig host,
        LaunchSpec launchSpec,
        CancellationToken cancellationToken)
    {
        using var _ = logger.BeginScope("ssh-session:{SessionId}", sessionId);

        var client = SshConnectionFactory.CreateClient(host);
        SshInteractiveSession? session = null;

        try
        {
            client.Connect();
            var shellStream = client.CreateShellStream("xterm-256color", (uint)launchSpec.Cols, (uint)launchSpec.Rows, 0, 0, 4096);
            session = new SshInteractiveSession(sessionId, client, shellStream);
            await session.InitializeAsync(host, launchSpec, cancellationToken);

            logger.LogInformation("Started SSH session {SessionId} on {Address}:{Port}", sessionId, host.SshAddress, host.SshPort ?? 22);
            return session;
        }
        catch
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
            else
            {
                client.Dispose();
            }

            throw;
        }
    }
}

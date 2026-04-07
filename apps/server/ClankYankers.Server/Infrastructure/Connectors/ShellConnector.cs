using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Infrastructure.Connectors;

public sealed class ShellConnector : IAgentConnector
{
    public string Kind => "shell";

    public LaunchSpec BuildLaunchSpec(
        string sessionId,
        CreateSessionRequest request,
        HostConfig host,
        ConnectorDefinition definition) =>
        // Host-configured shell keeps the connector agnostic to local vs Docker execution.
        new()
        {
            SessionId = sessionId,
            DisplayCommand = string.Join(' ', Enumerable.Repeat(host.ShellExecutable, 1).Concat(host.ShellArguments)),
            FileName = host.ShellExecutable,
            Arguments = host.ShellArguments,
            WorkingDirectory = ConnectorLaunchSupport.ResolveWorkingDirectory(host, request),
            Cols = request.Cols,
            Rows = request.Rows
        };
}

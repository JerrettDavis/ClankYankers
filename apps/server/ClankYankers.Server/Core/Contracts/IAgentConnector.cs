using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Core.Contracts;

public interface IAgentConnector
{
    string Kind { get; }

    LaunchSpec BuildLaunchSpec(
        string sessionId,
        CreateSessionRequest request,
        HostConfig host,
        ConnectorDefinition definition);
}

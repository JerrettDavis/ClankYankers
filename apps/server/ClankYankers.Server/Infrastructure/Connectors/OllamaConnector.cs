using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Infrastructure.Connectors;

public sealed class OllamaConnector : IAgentConnector
{
    public string Id => "ollama";

    public LaunchSpec BuildLaunchSpec(
        string sessionId,
        CreateSessionRequest request,
        HostConfig host,
        ConnectorDefinition definition)
    {
        var model = definition.DefaultModel ?? "qwen3.5:9b";

        return new LaunchSpec
        {
            SessionId = sessionId,
            DisplayCommand = $"ollama run {model}",
            FileName = "ollama",
            Arguments = ["run", model],
            WorkingDirectory = host.WorkingDirectory,
            Cols = request.Cols,
            Rows = request.Rows
        };
    }
}

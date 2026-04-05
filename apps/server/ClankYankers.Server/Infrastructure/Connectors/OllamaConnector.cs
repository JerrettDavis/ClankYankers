using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Infrastructure.Connectors;

public sealed class OllamaConnector : IAgentConnector
{
    public string Kind => "ollama";

    public LaunchSpec BuildLaunchSpec(
        string sessionId,
        CreateSessionRequest request,
        HostConfig host,
        ConnectorDefinition definition)
    {
        var fileName = ConnectorLaunchSupport.ResolveCommand(definition, "ollama");
        var arguments = new List<string> { "run" };
        arguments.AddRange(ConnectorLaunchSupport.ResolveBaseArguments(definition, [])
            .Where(argument => !argument.Equals("run", StringComparison.OrdinalIgnoreCase)));
        var model = ConnectorLaunchSupport.ResolveModel(definition, request, "qwen3.5:9b") ?? "qwen3.5:9b";
        arguments.Add(model);

        return new LaunchSpec
        {
            SessionId = sessionId,
            DisplayCommand = ConnectorLaunchSupport.BuildDisplayCommand(fileName, arguments),
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = host.WorkingDirectory,
            Cols = request.Cols,
            Rows = request.Rows
        };
    }
}

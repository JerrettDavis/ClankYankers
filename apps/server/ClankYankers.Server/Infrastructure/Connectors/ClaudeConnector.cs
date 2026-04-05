using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Infrastructure.Connectors;

public sealed class ClaudeConnector : IAgentConnector
{
    private static readonly HashSet<string> ReservedArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "--model",
        "--permission-mode",
        "--dangerously-skip-permissions",
        "--allowedTools"
    };

    private static readonly HashSet<string> ReservedArgumentsWithValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "--model",
        "--permission-mode",
        "--allowedTools"
    };

    public string Kind => "claude";

    public LaunchSpec BuildLaunchSpec(
        string sessionId,
        CreateSessionRequest request,
        HostConfig host,
        ConnectorDefinition definition)
    {
        var fileName = ConnectorLaunchSupport.ResolveCommand(definition, "claude");
        var arguments = SanitizeBaseArguments(definition).ToList();

        ConnectorLaunchSupport.AppendOption(arguments, "--model", ConnectorLaunchSupport.ResolveModel(definition, request));
        if (ConnectorLaunchSupport.ResolveSkipPermissions(definition))
        {
            arguments.Add("--dangerously-skip-permissions");
        }
        else
        {
            ConnectorLaunchSupport.AppendOption(arguments, "--permission-mode", ConnectorLaunchSupport.ResolvePermissionMode(definition));
        }

        var allowedTools = ConnectorLaunchSupport.ResolveAllowedTools(definition);
        if (allowedTools.Count > 0)
        {
            arguments.Add("--allowedTools");
            arguments.Add(string.Join(",", allowedTools));
        }

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

    private static IEnumerable<string> SanitizeBaseArguments(ConnectorDefinition definition)
    {
        var skipNext = false;

        foreach (var argument in ConnectorLaunchSupport.ResolveBaseArguments(definition, []))
        {
            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            if (!ReservedArguments.Contains(argument))
            {
                yield return argument;
                continue;
            }

            if (ReservedArgumentsWithValues.Contains(argument))
            {
                skipNext = true;
            }
        }
    }
}

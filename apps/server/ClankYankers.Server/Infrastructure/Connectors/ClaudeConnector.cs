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
        "--allowedTools",
        "--agent"
    };

    private static readonly HashSet<string> ReservedArgumentsWithValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "--model",
        "--permission-mode",
        "--allowedTools",
        "--agent"
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
        ConnectorLaunchSupport.AppendOption(arguments, "--agent", ConnectorLaunchSupport.ResolveAgent(request));
        if (ConnectorLaunchSupport.ResolveSkipPermissions(definition, request))
        {
            arguments.Add("--dangerously-skip-permissions");
        }
        else
        {
            ConnectorLaunchSupport.AppendOption(arguments, "--permission-mode", ConnectorLaunchSupport.ResolvePermissionMode(definition, request));
        }

        var allowedTools = ConnectorLaunchSupport.ResolveAllowedTools(definition, request);
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
            WorkingDirectory = ConnectorLaunchSupport.ResolveWorkingDirectory(host, request),
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

            if (!TryMatchReservedArgument(argument, out var expectsValue))
            {
                yield return argument;
                continue;
            }

            if (expectsValue && !argument.Contains('=', StringComparison.Ordinal))
            {
                skipNext = true;
            }
        }
    }

    private static bool TryMatchReservedArgument(string argument, out bool expectsValue)
    {
        var trimmed = argument.Trim();
        foreach (var reserved in ReservedArguments)
        {
            if (trimmed.Equals(reserved, StringComparison.OrdinalIgnoreCase))
            {
                expectsValue = ReservedArgumentsWithValues.Contains(reserved);
                return true;
            }

            if (trimmed.StartsWith($"{reserved}=", StringComparison.OrdinalIgnoreCase))
            {
                expectsValue = false;
                return true;
            }
        }

        expectsValue = false;
        return false;
    }
}

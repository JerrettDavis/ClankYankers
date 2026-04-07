using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Infrastructure.Connectors;

internal static class ConnectorLaunchSupport
{
    public static string ResolveCommand(ConnectorDefinition definition, string fallback) =>
        string.IsNullOrWhiteSpace(definition.LaunchCommand)
            ? fallback
            : definition.LaunchCommand.Trim();

    public static IReadOnlyList<string> ResolveBaseArguments(
        ConnectorDefinition definition,
        IReadOnlyList<string> fallback)
    {
        var configured = definition.LaunchArguments
            .Select(argument => argument.Trim())
            .Where(argument => argument.Length > 0)
            .ToArray();

        return configured.Length > 0 ? configured : fallback;
    }

    public static string? ResolveModel(ConnectorDefinition definition, CreateSessionRequest request, string? fallback = null) =>
        CoalesceValue(request.Model, definition.DefaultModel, fallback);

    public static string? ResolvePermissionMode(ConnectorDefinition definition, CreateSessionRequest request) =>
        CoalesceValue(request.PermissionMode, definition.DefaultPermissionMode);

    public static IReadOnlyList<string> ResolveAllowedTools(ConnectorDefinition definition, CreateSessionRequest request) =>
        NormalizeList(request.AllowedTools) ?? NormalizeList(definition.AllowedTools) ?? [];

    public static bool ResolveSkipPermissions(ConnectorDefinition definition, CreateSessionRequest request) =>
        request.SkipPermissions ?? definition.SkipPermissions;

    public static string? ResolveAgent(CreateSessionRequest request) => CoalesceValue(request.Agent);

    public static string? ResolveWorkingDirectory(HostConfig host, CreateSessionRequest request) =>
        CoalesceValue(request.WorkingDirectory, host.WorkingDirectory);

    public static void AppendOption(ICollection<string> arguments, string flag, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        arguments.Add(flag);
        arguments.Add(value.Trim());
    }

    public static string BuildDisplayCommand(string fileName, IEnumerable<string> arguments) =>
        string.Join(' ', Enumerable.Repeat(fileName, 1).Concat(arguments));

    private static IReadOnlyList<string>? NormalizeList(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        return values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? CoalesceValue(params string?[] values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .FirstOrDefault();
}

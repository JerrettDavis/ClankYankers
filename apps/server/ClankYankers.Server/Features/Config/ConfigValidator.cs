using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Features.Config;

public static class ConfigValidator
{
    private static readonly string[] SupportedBackplaneKinds = ["local", "docker"];
    private static readonly string[] SupportedConnectorKinds = ["shell", "ollama", "claude"];
    private static readonly string[] ReservedClaudeArguments =
        ["--model", "--permission-mode", "--dangerously-skip-permissions", "--allowedTools"];

    public static IDictionary<string, string[]> Validate(AppConfig config)
    {
        var errors = new Dictionary<string, string[]>();

        if (config.Backplanes.Count == 0)
        {
            errors["backplanes"] = ["At least one backplane is required."];
        }

        if (config.Hosts.Count == 0)
        {
            errors["hosts"] = ["At least one host is required."];
        }

        var enabledBackplaneIds = config.Backplanes
            .Where(backplane => backplane.Enabled)
            .Select(backplane => backplane.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (enabledBackplaneIds.Count == 0)
        {
            errors["backplanes.enabled"] = ["At least one enabled backplane is required."];
        }

        var blankBackplaneIds = config.Backplanes
            .Where(backplane => string.IsNullOrWhiteSpace(backplane.Id))
            .Select((_, index) => $"index {index}")
            .ToArray();

        if (blankBackplaneIds.Length > 0)
        {
            errors["backplanes.id.required"] = [$"Backplanes must have non-empty ids: {string.Join(", ", blankBackplaneIds)}."];
        }

        var blankBackplaneNames = config.Backplanes
            .Where(backplane => string.IsNullOrWhiteSpace(backplane.DisplayName))
            .Select(backplane => string.IsNullOrWhiteSpace(backplane.Id) ? "<blank-id>" : backplane.Id)
            .ToArray();

        if (blankBackplaneNames.Length > 0)
        {
            errors["backplanes.displayName"] = [$"Backplanes must have display names: {string.Join(", ", blankBackplaneNames)}."];
        }

        var backplaneIds = config.Backplanes.Select(backplane => backplane.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var backplaneKindsById = config.Backplanes
            .Where(backplane => !string.IsNullOrWhiteSpace(backplane.Id))
            .GroupBy(backplane => backplane.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Kind, StringComparer.OrdinalIgnoreCase);

        var blankHostIds = config.Hosts
            .Where(host => string.IsNullOrWhiteSpace(host.Id))
            .Select((_, index) => $"index {index}")
            .ToArray();

        if (blankHostIds.Length > 0)
        {
            errors["hosts.id.required"] = [$"Hosts must have non-empty ids: {string.Join(", ", blankHostIds)}."];
        }

        var blankHostNames = config.Hosts
            .Where(host => string.IsNullOrWhiteSpace(host.DisplayName))
            .Select(host => string.IsNullOrWhiteSpace(host.Id) ? "<blank-id>" : host.Id)
            .ToArray();

        if (blankHostNames.Length > 0)
        {
            errors["hosts.displayName"] = [$"Hosts must have display names: {string.Join(", ", blankHostNames)}."];
        }

        var unknownBackplanes = config.Hosts
            .Where(host => !backplaneIds.Contains(host.BackplaneId))
            .Select(host => host.Id)
            .ToArray();

        if (unknownBackplanes.Length > 0)
        {
            errors["hosts.backplaneId"] = [$"Unknown backplane references: {string.Join(", ", unknownBackplanes)}"];
        }

        var duplicateHostIds = config.Hosts
            .GroupBy(host => host.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateHostIds.Length > 0)
        {
            errors["hosts.id"] = [$"Duplicate host ids: {string.Join(", ", duplicateHostIds)}"];
        }

        if (!config.Hosts.Any(host => host.Enabled && enabledBackplaneIds.Contains(host.BackplaneId)))
        {
            errors["hosts.enabled"] = ["At least one enabled host on an enabled backplane is required."];
        }

        var hostsMissingShellExecutable = config.Hosts
            .Where(host => host.Enabled && string.IsNullOrWhiteSpace(host.ShellExecutable))
            .Select(host => string.IsNullOrWhiteSpace(host.Id) ? "<blank-id>" : host.Id)
            .ToArray();

        if (hostsMissingShellExecutable.Length > 0)
        {
            errors["hosts.shellExecutable"] =
                [$"Enabled hosts require a shell executable: {string.Join(", ", hostsMissingShellExecutable)}."];
        }

        var dockerHostsMissingEndpoint = config.Hosts
            .Where(host => host.Enabled
                && backplaneKindsById.TryGetValue(host.BackplaneId, out var kind)
                && kind.Equals("docker", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(host.DockerEndpoint))
            .Select(host => string.IsNullOrWhiteSpace(host.Id) ? "<blank-id>" : host.Id)
            .ToArray();

        if (dockerHostsMissingEndpoint.Length > 0)
        {
            errors["hosts.dockerEndpoint"] =
                [$"Enabled Docker hosts require a Docker endpoint: {string.Join(", ", dockerHostsMissingEndpoint)}."];
        }

        var dockerHostsMissingImage = config.Hosts
            .Where(host => host.Enabled
                && backplaneKindsById.TryGetValue(host.BackplaneId, out var kind)
                && kind.Equals("docker", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(host.DockerImage))
            .Select(host => string.IsNullOrWhiteSpace(host.Id) ? "<blank-id>" : host.Id)
            .ToArray();

        if (dockerHostsMissingImage.Length > 0)
        {
            errors["hosts.dockerImage"] =
                [$"Enabled Docker hosts require a Docker image: {string.Join(", ", dockerHostsMissingImage)}."];
        }

        var duplicateBackplaneIds = config.Backplanes
            .GroupBy(backplane => backplane.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateBackplaneIds.Length > 0)
        {
            errors["backplanes.id"] = [$"Duplicate backplane ids: {string.Join(", ", duplicateBackplaneIds)}"];
        }

        var unsupportedBackplaneKinds = config.Backplanes
            .Where(backplane => !SupportedBackplaneKinds.Contains(backplane.Kind, StringComparer.OrdinalIgnoreCase))
            .Select(backplane => $"{backplane.Id} ({backplane.Kind})")
            .ToArray();

        if (unsupportedBackplaneKinds.Length > 0)
        {
            errors["backplanes.kind"] =
                [$"Unsupported backplane kinds: {string.Join(", ", unsupportedBackplaneKinds)}. Supported kinds: {string.Join(", ", SupportedBackplaneKinds)}."];
        }

        var duplicateConnectorIds = config.Connectors
            .GroupBy(connector => connector.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateConnectorIds.Length > 0)
        {
            errors["connectors.id"] = [$"Duplicate connector ids: {string.Join(", ", duplicateConnectorIds)}"];
        }

        var blankConnectorIds = config.Connectors
            .Where(connector => string.IsNullOrWhiteSpace(connector.Id))
            .Select((_, index) => $"index {index}")
            .ToArray();

        if (blankConnectorIds.Length > 0)
        {
            errors["connectors.id.required"] = [$"Connectors must have non-empty ids: {string.Join(", ", blankConnectorIds)}."];
        }

        var blankConnectorNames = config.Connectors
            .Where(connector => string.IsNullOrWhiteSpace(connector.DisplayName))
            .Select(connector => string.IsNullOrWhiteSpace(connector.Id) ? "<blank-id>" : connector.Id)
            .ToArray();

        if (blankConnectorNames.Length > 0)
        {
            errors["connectors.displayName"] = [$"Connectors must have display names: {string.Join(", ", blankConnectorNames)}."];
        }

        var unknownConnectorKinds = config.Connectors
            .Where(connector => !SupportedConnectorKinds.Contains(connector.Kind, StringComparer.OrdinalIgnoreCase))
            .Select(connector => $"{connector.Id} ({connector.Kind})")
            .ToArray();

        if (unknownConnectorKinds.Length > 0)
        {
            errors["connectors.kind"] =
                [$"Unsupported connector kinds: {string.Join(", ", unknownConnectorKinds)}. Supported kinds: {string.Join(", ", SupportedConnectorKinds)}."];
        }

        if (!config.Connectors.Any(connector => connector.Enabled))
        {
            errors["connectors.enabled"] = ["At least one enabled connector is required."];
        }

        var claudeConnectorsWithReservedArguments = config.Connectors
            .Where(connector => connector.Enabled
                && connector.Kind.Equals("claude", StringComparison.OrdinalIgnoreCase)
                && connector.LaunchArguments.Any(argument => ReservedClaudeArguments.Contains(argument, StringComparer.OrdinalIgnoreCase)))
            .Select(connector => string.IsNullOrWhiteSpace(connector.Id) ? "<blank-id>" : connector.Id)
            .ToArray();

        if (claudeConnectorsWithReservedArguments.Length > 0)
        {
            errors["connectors.launchArguments"] =
                [$"Claude connectors cannot set reserved launch arguments directly: {string.Join(", ", claudeConnectorsWithReservedArguments)}."];
        }

        return errors;
    }
}

using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Features.Config;

public static class ConfigValidator
{
    public const int MaxExperimentVariantCount = 24;

    private static readonly string[] ReservedClaudeArguments =
        ["--model", "--permission-mode", "--dangerously-skip-permissions", "--allowedTools", "--agent"];

    public static IDictionary<string, string[]> Validate(
        AppConfig config,
        IEnumerable<string>? supportedBackplaneKinds = null,
        IEnumerable<string>? supportedConnectorKinds = null)
    {
        var errors = new Dictionary<string, string[]>();
        var supportedBackplanes = (supportedBackplaneKinds ?? ["local", "docker", "ssh", "remote"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var supportedConnectors = (supportedConnectorKinds ?? ["shell", "ollama", "claude"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

        var dockerHostsMissingEndpoint = GetEnabledHostsForKind(config, backplaneKindsById, "docker")
            .Where(host => string.IsNullOrWhiteSpace(host.DockerEndpoint))
            .Select(GetDisplayableHostId)
            .ToArray();

        if (dockerHostsMissingEndpoint.Length > 0)
        {
            errors["hosts.dockerEndpoint"] =
                [$"Enabled Docker hosts require a Docker endpoint: {string.Join(", ", dockerHostsMissingEndpoint)}."];
        }

        var dockerHostsMissingImage = GetEnabledHostsForKind(config, backplaneKindsById, "docker")
            .Where(host => string.IsNullOrWhiteSpace(host.DockerImage))
            .Select(GetDisplayableHostId)
            .ToArray();

        if (dockerHostsMissingImage.Length > 0)
        {
            errors["hosts.dockerImage"] =
                [$"Enabled Docker hosts require a Docker image: {string.Join(", ", dockerHostsMissingImage)}."];
        }

        var sshHostsMissingAddress = GetEnabledHostsForKind(config, backplaneKindsById, "ssh")
            .Where(host => string.IsNullOrWhiteSpace(host.SshAddress))
            .Select(GetDisplayableHostId)
            .ToArray();

        if (sshHostsMissingAddress.Length > 0)
        {
            errors["hosts.sshAddress"] =
                [$"Enabled SSH hosts require an address: {string.Join(", ", sshHostsMissingAddress)}."];
        }

        var sshHostsMissingUsername = GetEnabledHostsForKind(config, backplaneKindsById, "ssh")
            .Where(host => string.IsNullOrWhiteSpace(host.SshUsername))
            .Select(GetDisplayableHostId)
            .ToArray();

        if (sshHostsMissingUsername.Length > 0)
        {
            errors["hosts.sshUsername"] =
                [$"Enabled SSH hosts require a username: {string.Join(", ", sshHostsMissingUsername)}."];
        }

        var sshHostsWithInvalidPort = GetEnabledHostsForKind(config, backplaneKindsById, "ssh")
            .Where(host => host.SshPort is <= 0 or > 65535)
            .Select(GetDisplayableHostId)
            .ToArray();

        if (sshHostsWithInvalidPort.Length > 0)
        {
            errors["hosts.sshPort"] =
                [$"Enabled SSH hosts must use a port between 1 and 65535: {string.Join(", ", sshHostsWithInvalidPort)}."];
        }

        var sshHostsMissingAuth = GetEnabledHostsForKind(config, backplaneKindsById, "ssh")
            .Where(host =>
                string.IsNullOrWhiteSpace(host.SshPassword)
                && string.IsNullOrWhiteSpace(host.SshPrivateKeyPath)
                && !host.SshUseKeyboardInteractive)
            .Select(GetDisplayableHostId)
            .ToArray();

        if (sshHostsMissingAuth.Length > 0)
        {
            errors["hosts.sshAuthentication"] =
                [$"Enabled SSH hosts require a password, private key, or keyboard-interactive auth: {string.Join(", ", sshHostsMissingAuth)}."];
        }

        var sshHostsWithCertificateButNoKey = GetEnabledHostsForKind(config, backplaneKindsById, "ssh")
            .Where(host => !string.IsNullOrWhiteSpace(host.SshCertificatePath) && string.IsNullOrWhiteSpace(host.SshPrivateKeyPath))
            .Select(GetDisplayableHostId)
            .ToArray();

        if (sshHostsWithCertificateButNoKey.Length > 0)
        {
            errors["hosts.sshCertificatePath"] =
                [$"Enabled SSH hosts require a private key path when a certificate path is configured: {string.Join(", ", sshHostsWithCertificateButNoKey)}."];
        }

        var sshHostsMissingTrustPolicy = GetEnabledHostsForKind(config, backplaneKindsById, "ssh")
            .Where(host =>
                !host.SshAllowAnyHostKey
                && string.IsNullOrWhiteSpace(host.SshHostKeyFingerprint)
                && string.IsNullOrWhiteSpace(host.SshTrustedCaFingerprint))
            .Select(GetDisplayableHostId)
            .ToArray();

        if (sshHostsMissingTrustPolicy.Length > 0)
        {
            errors["hosts.sshTrust"] =
                [$"Enabled SSH hosts must pin a host key, trust a CA fingerprint, or explicitly allow any host key: {string.Join(", ", sshHostsMissingTrustPolicy)}."];
        }

        var remoteHostsMissingUrl = GetEnabledHostsForKind(config, backplaneKindsById, "remote")
            .Where(host => string.IsNullOrWhiteSpace(host.RemoteDaemonUrl))
            .Select(GetDisplayableHostId)
            .ToArray();

        if (remoteHostsMissingUrl.Length > 0)
        {
            errors["hosts.remoteDaemonUrl"] =
                [$"Enabled remote hosts require a daemon URL: {string.Join(", ", remoteHostsMissingUrl)}."];
        }

        var remoteHostsWithInvalidUrl = GetEnabledHostsForKind(config, backplaneKindsById, "remote")
            .Where(host => !string.IsNullOrWhiteSpace(host.RemoteDaemonUrl) && !Uri.TryCreate(host.RemoteDaemonUrl, UriKind.Absolute, out _))
            .Select(GetDisplayableHostId)
            .ToArray();

        if (remoteHostsWithInvalidUrl.Length > 0)
        {
            errors["hosts.remoteDaemonUrl.invalid"] =
                [$"Enabled remote hosts must use an absolute daemon URL: {string.Join(", ", remoteHostsWithInvalidUrl)}."];
        }

        var remoteHostsWithInvalidExecutor = GetEnabledHostsForKind(config, backplaneKindsById, "remote")
            .Where(host => !string.IsNullOrWhiteSpace(host.RemoteExecutorKind)
                && !host.RemoteExecutorKind.Equals("process", StringComparison.OrdinalIgnoreCase)
                && !host.RemoteExecutorKind.Equals("docker", StringComparison.OrdinalIgnoreCase))
            .Select(GetDisplayableHostId)
            .ToArray();

        if (remoteHostsWithInvalidExecutor.Length > 0)
        {
            errors["hosts.remoteExecutorKind"] =
                [$"Enabled remote hosts must use executor kinds of 'process' or 'docker': {string.Join(", ", remoteHostsWithInvalidExecutor)}."];
        }

        var remoteDockerHostsMissingImage = GetEnabledHostsForKind(config, backplaneKindsById, "remote")
            .Where(host => host.RemoteExecutorKind?.Equals("docker", StringComparison.OrdinalIgnoreCase) == true
                && string.IsNullOrWhiteSpace(host.RemoteDockerImage))
            .Select(GetDisplayableHostId)
            .ToArray();

        if (remoteDockerHostsMissingImage.Length > 0)
        {
            errors["hosts.remoteDockerImage"] =
                [$"Enabled remote Docker hosts require a Docker image: {string.Join(", ", remoteDockerHostsMissingImage)}."];
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
            .Where(backplane => backplane.Enabled
                && !supportedBackplanes.Contains(backplane.Kind, StringComparer.OrdinalIgnoreCase))
            .Select(backplane => $"{backplane.Id} ({backplane.Kind})")
            .ToArray();

        if (unsupportedBackplaneKinds.Length > 0)
        {
            errors["backplanes.kind"] =
                [$"Unsupported backplane kinds: {string.Join(", ", unsupportedBackplaneKinds)}. Supported kinds: {string.Join(", ", supportedBackplanes)}."];
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
            .Where(connector => connector.Enabled
                && !supportedConnectors.Contains(connector.Kind, StringComparer.OrdinalIgnoreCase))
            .Select(connector => $"{connector.Id} ({connector.Kind})")
            .ToArray();

        if (unknownConnectorKinds.Length > 0)
        {
            errors["connectors.kind"] =
                [$"Unsupported connector kinds: {string.Join(", ", unknownConnectorKinds)}. Supported kinds: {string.Join(", ", supportedConnectors)}."];
        }

        if (!config.Connectors.Any(connector => connector.Enabled))
        {
            errors["connectors.enabled"] = ["At least one enabled connector is required."];
        }

        var claudeConnectorsWithReservedArguments = config.Connectors
            .Where(connector => connector.Enabled
                && connector.Kind.Equals("claude", StringComparison.OrdinalIgnoreCase)
                && connector.LaunchArguments.Any(IsReservedClaudeArgument))
            .Select(connector => string.IsNullOrWhiteSpace(connector.Id) ? "<blank-id>" : connector.Id)
            .ToArray();

        if (claudeConnectorsWithReservedArguments.Length > 0)
        {
            errors["connectors.launchArguments"] =
                [$"Claude connectors cannot set reserved launch arguments directly: {string.Join(", ", claudeConnectorsWithReservedArguments)}."];
        }

        var duplicateExperimentIds = config.Experiments
            .GroupBy(experiment => experiment.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateExperimentIds.Length > 0)
        {
            errors["experiments.id"] = [$"Duplicate experiment ids: {string.Join(", ", duplicateExperimentIds)}"];
        }

        var blankExperimentIds = config.Experiments
            .Where(experiment => string.IsNullOrWhiteSpace(experiment.Id))
            .Select((_, index) => $"index {index}")
            .ToArray();

        if (blankExperimentIds.Length > 0)
        {
            errors["experiments.id.required"] =
                [$"Experiments must have non-empty ids: {string.Join(", ", blankExperimentIds)}."];
        }

        var blankExperimentNames = config.Experiments
            .Where(experiment => string.IsNullOrWhiteSpace(experiment.DisplayName))
            .Select(experiment => string.IsNullOrWhiteSpace(experiment.Id) ? "<blank-id>" : experiment.Id)
            .ToArray();

        if (blankExperimentNames.Length > 0)
        {
            errors["experiments.displayName"] =
                [$"Experiments must have display names: {string.Join(", ", blankExperimentNames)}."];
        }

        var hostIds = config.Hosts
            .Where(host => !string.IsNullOrWhiteSpace(host.Id))
            .GroupBy(host => host.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var connectorIds = config.Connectors
            .Where(connector => !string.IsNullOrWhiteSpace(connector.Id))
            .GroupBy(connector => connector.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var experimentsMissingHosts = config.Experiments
            .Where(experiment => experiment.Enabled && experiment.HostIds.All(string.IsNullOrWhiteSpace))
            .Select(experiment => experiment.Id)
            .ToArray();

        if (experimentsMissingHosts.Length > 0)
        {
            errors["experiments.hostIds"] =
                [$"Enabled experiments require at least one host id: {string.Join(", ", experimentsMissingHosts)}."];
        }

        var experimentsMissingConnectors = config.Experiments
            .Where(experiment => experiment.Enabled && experiment.ConnectorIds.All(string.IsNullOrWhiteSpace))
            .Select(experiment => experiment.Id)
            .ToArray();

        if (experimentsMissingConnectors.Length > 0)
        {
            errors["experiments.connectorIds"] =
                [$"Enabled experiments require at least one connector id: {string.Join(", ", experimentsMissingConnectors)}."];
        }

        var experimentsWithUnknownHosts = config.Experiments
            .SelectMany(experiment => experiment.HostIds
                .Where(hostId => !string.IsNullOrWhiteSpace(hostId) && !hostIds.ContainsKey(hostId))
                .Select(hostId => $"{experiment.Id} ({hostId})"))
            .ToArray();

        if (experimentsWithUnknownHosts.Length > 0)
        {
            errors["experiments.hostIds.unknown"] =
                [$"Experiments reference unknown hosts: {string.Join(", ", experimentsWithUnknownHosts)}."];
        }

        var experimentsWithDisabledHosts = config.Experiments
            .SelectMany(experiment => experiment.HostIds
                .Where(hostId => !string.IsNullOrWhiteSpace(hostId)
                    && hostIds.TryGetValue(hostId, out var host)
                    && !host.Enabled)
                .Select(hostId => $"{experiment.Id} ({hostId})"))
            .ToArray();

        if (experimentsWithDisabledHosts.Length > 0)
        {
            errors["experiments.hostIds.disabled"] =
                [$"Experiments reference disabled hosts: {string.Join(", ", experimentsWithDisabledHosts)}."];
        }

        var experimentsWithUnknownConnectors = config.Experiments
            .SelectMany(experiment => experiment.ConnectorIds
                .Where(connectorId => !string.IsNullOrWhiteSpace(connectorId) && !connectorIds.ContainsKey(connectorId))
                .Select(connectorId => $"{experiment.Id} ({connectorId})"))
            .ToArray();

        if (experimentsWithUnknownConnectors.Length > 0)
        {
            errors["experiments.connectorIds.unknown"] =
                [$"Experiments reference unknown connectors: {string.Join(", ", experimentsWithUnknownConnectors)}."];
        }

        var experimentsWithDisabledConnectors = config.Experiments
            .SelectMany(experiment => experiment.ConnectorIds
                .Where(connectorId => !string.IsNullOrWhiteSpace(connectorId)
                    && connectorIds.TryGetValue(connectorId, out var connector)
                    && !connector.Enabled)
                .Select(connectorId => $"{experiment.Id} ({connectorId})"))
            .ToArray();

        if (experimentsWithDisabledConnectors.Length > 0)
        {
            errors["experiments.connectorIds.disabled"] =
                [$"Experiments reference disabled connectors: {string.Join(", ", experimentsWithDisabledConnectors)}."];
        }

        var experimentsWithInvalidDimensions = config.Experiments
            .Where(experiment => experiment.Cols is < 24 or > 240 || experiment.Rows is < 12 or > 120)
            .Select(experiment => experiment.Id)
            .ToArray();

        if (experimentsWithInvalidDimensions.Length > 0)
        {
            errors["experiments.dimensions"] =
                [$"Experiments must stay within supported terminal dimensions: {string.Join(", ", experimentsWithInvalidDimensions)}."];
        }

        var oversizedExperiments = config.Experiments
            .Where(experiment => CalculateVariantCount(experiment) > MaxExperimentVariantCount)
            .Select(experiment => $"{experiment.Id} ({CalculateVariantCount(experiment)} variants)")
            .ToArray();

        if (oversizedExperiments.Length > 0)
        {
            errors["experiments.variantCount"] =
                [$"Experiments cannot exceed {MaxExperimentVariantCount} variants: {string.Join(", ", oversizedExperiments)}."];
        }

        return errors;
    }

    private static bool IsReservedClaudeArgument(string argument)
    {
        var trimmed = argument.Trim();
        return ReservedClaudeArguments.Any(reserved =>
            trimmed.Equals(reserved, StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith($"{reserved}=", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<HostConfig> GetEnabledHostsForKind(
        AppConfig config,
        IReadOnlyDictionary<string, string> backplaneKindsById,
        string expectedKind) =>
        config.Hosts.Where(host =>
            host.Enabled
            && backplaneKindsById.TryGetValue(host.BackplaneId, out var kind)
            && kind.Equals(expectedKind, StringComparison.OrdinalIgnoreCase));

    private static string GetDisplayableHostId(HostConfig host) =>
        string.IsNullOrWhiteSpace(host.Id) ? "<blank-id>" : host.Id;

    public static void ThrowIfInvalid(
        AppConfig config,
        IEnumerable<string>? supportedBackplaneKinds = null,
        IEnumerable<string>? supportedConnectorKinds = null,
        string messagePrefix = "Persisted config is invalid.")
    {
        var errors = Validate(config, supportedBackplaneKinds, supportedConnectorKinds);
        if (errors.Count == 0)
        {
            return;
        }

        var detail = string.Join(" ", errors.Values.SelectMany(messages => messages));
        throw new InvalidOperationException($"{messagePrefix} {detail}".Trim());
    }

    private static int CalculateVariantCount(ExperimentDefinition experiment)
    {
        var hostCount = experiment.HostIds
            .Where(hostId => !string.IsNullOrWhiteSpace(hostId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var connectorCount = experiment.ConnectorIds
            .Where(connectorId => !string.IsNullOrWhiteSpace(connectorId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var modelCount = experiment.Models
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .DefaultIfEmpty(string.Empty)
            .Count();

        return hostCount * connectorCount * modelCount;
    }
}

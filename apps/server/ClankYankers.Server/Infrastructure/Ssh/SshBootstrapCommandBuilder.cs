using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Infrastructure.Ssh;

internal static class SshBootstrapCommandBuilder
{
    public static string? Build(HostConfig host, LaunchSpec launchSpec)
    {
        var flavor = ResolveShellFlavor(host);

        if (IsHostShellLaunch(host, launchSpec))
        {
            return flavor switch
            {
                SshShellFlavor.PowerShell => BuildPowerShellInitializationCommand(launchSpec),
                _ => BuildPosixInitializationCommand(launchSpec)
            };
        }

        return flavor switch
        {
            SshShellFlavor.PowerShell => BuildPowerShellCommand(launchSpec),
            _ => BuildPosixCommand(launchSpec)
        };
    }

    private static string BuildPosixCommand(LaunchSpec launchSpec)
    {
        var environmentPrefix = string.Join(' ', launchSpec.Environment
            .Where(pair => pair.Value is not null)
            .Select(pair =>
            {
                ValidateEnvironmentKey(pair.Key);
                return $"{pair.Key}={QuotePosix(pair.Value!)}";
            }));
        var prefix = string.IsNullOrWhiteSpace(launchSpec.WorkingDirectory)
            ? string.Empty
            : $"cd {QuotePosix(launchSpec.WorkingDirectory.Trim())} && ";
        var command = string.Join(' ', Enumerable.Repeat(launchSpec.FileName, 1)
            .Concat(launchSpec.Arguments)
            .Select(QuotePosix));

        var environment = string.IsNullOrWhiteSpace(environmentPrefix)
            ? string.Empty
            : $"{environmentPrefix} ";

        return $"{prefix}exec {environment}{command}\n";
    }

    private static string BuildPowerShellCommand(LaunchSpec launchSpec)
    {
        var parts = new List<string>();

        AddPowerShellEnvironmentAssignments(parts, launchSpec);

        if (!string.IsNullOrWhiteSpace(launchSpec.WorkingDirectory))
        {
            parts.Add($"Set-Location -LiteralPath {QuotePowerShell(launchSpec.WorkingDirectory.Trim())}");
        }

        var command = string.Join(' ', Enumerable.Repeat(launchSpec.FileName, 1)
            .Concat(launchSpec.Arguments)
            .Select(QuotePowerShell));

        parts.Add($"& {command}");
        parts.Add("exit $LASTEXITCODE");
        return string.Join("; ", parts) + "\n";
    }

    private static string? BuildPosixInitializationCommand(LaunchSpec launchSpec)
    {
        var commands = launchSpec.Environment
            .Where(pair => pair.Value is not null)
            .Select(pair =>
            {
                ValidateEnvironmentKey(pair.Key);
                return $"export {pair.Key}={QuotePosix(pair.Value!)}";
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(launchSpec.WorkingDirectory))
        {
            commands.Add($"cd {QuotePosix(launchSpec.WorkingDirectory.Trim())}");
        }

        return commands.Count == 0
            ? null
            : string.Join("\n", commands) + "\n";
    }

    private static string? BuildPowerShellInitializationCommand(LaunchSpec launchSpec)
    {
        var parts = new List<string>();
        AddPowerShellEnvironmentAssignments(parts, launchSpec);

        if (!string.IsNullOrWhiteSpace(launchSpec.WorkingDirectory))
        {
            parts.Add($"Set-Location -LiteralPath {QuotePowerShell(launchSpec.WorkingDirectory.Trim())}");
        }

        return parts.Count == 0
            ? null
            : string.Join("; ", parts) + "\n";
    }

    private static void AddPowerShellEnvironmentAssignments(List<string> parts, LaunchSpec launchSpec)
    {
        foreach (var (key, value) in launchSpec.Environment.Where(pair => pair.Value is not null))
        {
            ValidateEnvironmentKey(key);
            parts.Add($"$env:{key} = {QuotePowerShell(value!)}");
        }
    }

    private static void ValidateEnvironmentKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.All(character => char.IsLetterOrDigit(character) || character == '_') || char.IsDigit(key[0]))
        {
            throw new InvalidOperationException($"Environment variable key '{key}' contains invalid characters.");
        }
    }

    private static bool IsHostShellLaunch(HostConfig host, LaunchSpec launchSpec) =>
        launchSpec.FileName.Equals(host.ShellExecutable, StringComparison.OrdinalIgnoreCase)
        && launchSpec.Arguments.SequenceEqual(host.ShellArguments, StringComparer.OrdinalIgnoreCase);

    private static SshShellFlavor ResolveShellFlavor(HostConfig host)
    {
        var executable = Path.GetFileNameWithoutExtension(host.ShellExecutable)?.Trim().ToLowerInvariant() ?? string.Empty;

        return executable switch
        {
            "pwsh" or "powershell" => SshShellFlavor.PowerShell,
            _ => SshShellFlavor.Posix
        };
    }

    private static string QuotePosix(string value) =>
        $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";

    private static string QuotePowerShell(string value) =>
        $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private enum SshShellFlavor
    {
        Posix,
        PowerShell
    }
}

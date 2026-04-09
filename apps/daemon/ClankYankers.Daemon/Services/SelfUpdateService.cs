using ClankYankers.Remote.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ClankYankers.Daemon.Services;

internal sealed class SelfUpdateService(
    IHostApplicationLifetime lifetime,
    ILogger<SelfUpdateService> logger)
{
    private const string SupportedPackageId = "ClankYankers.Daemon";

    public Task ScheduleAsync(RemoteSelfUpdateRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.PackageId.Equals(SupportedPackageId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Package '{request.PackageId}' is not an allowed self-update target.");
        }

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the current daemon executable path.");
        var currentArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var updateCommand = BuildUpdateCommand(executablePath, currentArgs, request);

        logger.LogInformation("Scheduling daemon self-update for package {PackageId}", request.PackageId);
        StartDetachedUpdater(updateCommand);
        lifetime.StopApplication();

        return Task.CompletedTask;
    }

    private static string BuildUpdateCommand(string executablePath, IReadOnlyList<string> currentArgs, RemoteSelfUpdateRequest request)
    {
        var versionArg = string.IsNullOrWhiteSpace(request.Version)
            ? string.Empty
            : $" --version {QuoteShellArgument(request.Version.Trim())}";
        var restartCommand = request.Restart
            ? $" && {QuoteShellArgument(executablePath)} {string.Join(' ', currentArgs.Select(QuoteShellArgument))}"
            : string.Empty;

        return $"dotnet tool update --global {QuoteShellArgument(request.PackageId)}{versionArg}{restartCommand}";
    }

    private static void StartDetachedUpdater(string updateCommand)
    {
        if (OperatingSystem.IsWindows())
        {
            var command = $"timeout /t 2 /nobreak > nul && {updateCommand}";
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });

            return;
        }

        var posixCommand = $"sleep 2 && {updateCommand}";
        Process.Start(new ProcessStartInfo("/bin/sh", $"-lc {QuoteShellArgument(posixCommand)}")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static string QuoteShellArgument(string value)
    {
        if (OperatingSystem.IsWindows())
        {
            return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }
}

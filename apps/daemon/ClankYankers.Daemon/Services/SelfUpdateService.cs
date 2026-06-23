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

        logger.LogInformation("Scheduling daemon self-update for package {PackageId}", SupportedPackageId);
        StartDetachedUpdater(request.PackageId, request.Version?.Trim(), request.Restart, executablePath, currentArgs);
        lifetime.StopApplication();

        return Task.CompletedTask;
    }

    private static void StartDetachedUpdater(
        string packageId,
        string? version,
        bool restart,
        string executablePath,
        string[] currentArgs)
    {
        // Run in a background thread so the daemon can finish its shutdown sequence.
        // All dynamic values are passed via ArgumentList — never concatenated into a
        // shell command string — to prevent command-line injection.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            // Build the dotnet-tool-update argument list without any shell interpolation.
            var updateArgs = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            updateArgs.ArgumentList.Add("tool");
            updateArgs.ArgumentList.Add("update");
            updateArgs.ArgumentList.Add("--global");
            updateArgs.ArgumentList.Add(packageId);
            if (!string.IsNullOrWhiteSpace(version))
            {
                updateArgs.ArgumentList.Add("--version");
                updateArgs.ArgumentList.Add(version);
            }

            using var updateProcess = Process.Start(updateArgs);
            if (updateProcess is not null)
            {
                await updateProcess.WaitForExitAsync().ConfigureAwait(false);
            }

            if (restart)
            {
                // Restart the daemon by starting a new process directly — no shell needed.
                var restartInfo = new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var arg in currentArgs)
                {
                    restartInfo.ArgumentList.Add(arg);
                }

                Process.Start(restartInfo);
            }
        });
    }
}

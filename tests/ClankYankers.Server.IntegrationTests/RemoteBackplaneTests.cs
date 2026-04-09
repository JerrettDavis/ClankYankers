using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Infrastructure.Backplanes;
using ClankYankers.Server.IntegrationTests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClankYankers.Server.IntegrationTests;

public sealed class RemoteBackplaneTests
{
    [Fact]
    public async Task Remote_backplane_streams_process_output_through_the_daemon()
    {
        await using var harness = await RemoteDaemonProcessHarness.StartAsync(accessToken: "daemon-token");
        var backplane = new RemoteBackplane(NullLogger<RemoteBackplane>.Instance);

        await using var session = await backplane.StartAsync(
            "remote-process",
            CreateRemoteHost(harness) with
            {
                RemoteAccessToken = "daemon-token",
                RemoteExecutorKind = "process"
            },
            CreateShellLaunchSpec("remote-process"),
            CancellationToken.None);

        await session.WriteInputAsync(ProcessEchoCommand("remote-process"), CancellationToken.None);
        await session.WriteInputAsync(ProcessExitCommand(), CancellationToken.None);

        var output = await TerminalTestHelpers.ReadUntilContainsAsync(session.Output, "remote-process", TimeSpan.FromSeconds(20));

        Assert.Contains("remote-process", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Remote_backplane_streams_docker_output_through_the_daemon_when_available()
    {
        if (!TerminalTestHelpers.DockerAvailable() || OperatingSystem.IsWindows())
        {
            return;
        }

        await using var harness = await RemoteDaemonProcessHarness.StartAsync();
        var backplane = new RemoteBackplane(NullLogger<RemoteBackplane>.Instance);

        await using var session = await backplane.StartAsync(
            "remote-docker",
            CreateRemoteHost(harness) with
            {
                RemoteExecutorKind = "docker",
                RemoteDockerEndpoint = OperatingSystem.IsWindows()
                    ? "npipe://./pipe/docker_engine"
                    : "unix:///var/run/docker.sock",
                RemoteDockerImage = "alpine:3.20"
            },
            new LaunchSpec
            {
                SessionId = "remote-docker",
                DisplayCommand = "/bin/sh",
                FileName = "/bin/sh",
                Arguments = [],
                WorkingDirectory = "/workspace",
                Cols = 120,
                Rows = 32
            },
            CancellationToken.None);

        await session.WriteInputAsync("pwd\n", CancellationToken.None);
        await session.WriteInputAsync("echo remote-docker\n", CancellationToken.None);
        await session.WriteInputAsync("exit\n", CancellationToken.None);

        var output = await TerminalTestHelpers.ReadUntilContainsAsync(session.Output, "remote-docker", TimeSpan.FromSeconds(20));

        Assert.Contains("/workspace", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote-docker", output, StringComparison.OrdinalIgnoreCase);
    }

    private static HostConfig CreateRemoteHost(RemoteDaemonProcessHarness harness) =>
        new()
        {
            Id = "remote-host",
            BackplaneId = "remote",
            DisplayName = "Remote",
            ShellExecutable = OperatingSystem.IsWindows() ? "pwsh.exe" : "/bin/sh",
            ShellArguments = OperatingSystem.IsWindows() ? ["-NoLogo"] : [],
            WorkingDirectory = OperatingSystem.IsWindows() ? Environment.CurrentDirectory : "/tmp",
            RemoteDaemonUrl = harness.BaseUri.ToString().TrimEnd('/'),
            RemoteAllowInsecureTls = false
        };

    private static LaunchSpec CreateShellLaunchSpec(string sessionId) =>
        new()
        {
            SessionId = sessionId,
            DisplayCommand = OperatingSystem.IsWindows() ? "pwsh.exe -NoLogo" : "/bin/sh",
            FileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows() ? ["-NoLogo"] : [],
            WorkingDirectory = OperatingSystem.IsWindows() ? Environment.CurrentDirectory : "/tmp",
            Cols = 120,
            Rows = 32
        };

    private static string ProcessEchoCommand(string marker) =>
        OperatingSystem.IsWindows()
            ? $"Write-Output '{marker}'\r\n"
            : $"echo {marker}\n";

    private static string ProcessExitCommand() =>
        OperatingSystem.IsWindows() ? "exit\r\n" : "exit\n";
}

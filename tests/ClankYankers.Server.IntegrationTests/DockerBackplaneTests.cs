using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Infrastructure.Backplanes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClankYankers.Server.IntegrationTests;

public sealed class DockerBackplaneTests
{
    [Fact]
    public async Task Docker_backplane_streams_shell_output_when_available()
    {
        if (!TerminalTestHelpers.DockerAvailable())
        {
            return;
        }

        var backplane = new DockerBackplane(NullLogger<DockerBackplane>.Instance);

        await using var session = await backplane.StartAsync(
            "docker-shell",
            new HostConfig
            {
                Id = "docker-local",
                BackplaneId = "docker",
                DisplayName = "Docker",
                ShellExecutable = "/bin/sh",
                DockerEndpoint = OperatingSystem.IsWindows()
                    ? "npipe://./pipe/docker_engine"
                    : "unix:///var/run/docker.sock",
                DockerImage = "alpine:3.20",
                WorkingDirectory = "/workspace"
            },
            new LaunchSpec
            {
                SessionId = "docker-shell",
                DisplayCommand = "/bin/sh",
                FileName = "/bin/sh",
                Arguments = [],
                Cols = 100,
                Rows = 30
            },
            CancellationToken.None);

        await session.WriteInputAsync("echo integration-docker\n", CancellationToken.None);
        await session.WriteInputAsync("exit\n", CancellationToken.None);

        var output = await TerminalTestHelpers.ReadUntilContainsAsync(
            session.Output,
            "integration-docker",
            TimeSpan.FromSeconds(20));

        Assert.Contains("integration-docker", output, StringComparison.OrdinalIgnoreCase);
    }
}

using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Infrastructure.Backplanes;
using ClankYankers.Server.IntegrationTests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClankYankers.Server.IntegrationTests;

public sealed class SshBackplaneTests
{
    [Fact]
    public async Task Ssh_backplane_streams_shell_output_with_password_auth()
    {
        if (OperatingSystem.IsWindows() || !SshTestContainer.IsAvailable())
        {
            return;
        }

        await using var container = await SshTestContainer.StartAsync();
        var backplane = new SshBackplane(NullLogger<SshBackplane>.Instance);

        await using var session = await backplane.StartAsync(
            "ssh-password",
            CreateHost(container) with
            {
                SshPassword = SshTestContainer.Password,
                SshAllowAnyHostKey = true
            },
            CreateShellLaunchSpec("ssh-password"),
            CancellationToken.None);

        await session.WriteInputAsync("pwd\n", CancellationToken.None);
        await session.WriteInputAsync("echo ssh-password\n", CancellationToken.None);
        await session.WriteInputAsync("exit\n", CancellationToken.None);

        var output = await TerminalTestHelpers.ReadUntilContainsAsync(session.Output, "ssh-password", TimeSpan.FromSeconds(20));

        Assert.Contains("/home/clanky", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ssh-password", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ssh_backplane_supports_private_key_auth_and_resize()
    {
        if (OperatingSystem.IsWindows() || !SshTestContainer.IsAvailable())
        {
            return;
        }

        await using var container = await SshTestContainer.StartAsync();
        var backplane = new SshBackplane(NullLogger<SshBackplane>.Instance);

        await using var session = await backplane.StartAsync(
            "ssh-key",
            CreateHost(container) with
            {
                SshPrivateKeyPath = container.PrivateKeyPath,
                SshHostKeyFingerprint = container.HostKeyFingerprint
            },
            CreateShellLaunchSpec("ssh-key"),
            CancellationToken.None);

        await session.ResizeAsync(140, 40, CancellationToken.None);
        await session.WriteInputAsync("echo ssh-key\n", CancellationToken.None);
        await session.WriteInputAsync("exit\n", CancellationToken.None);

        var output = await TerminalTestHelpers.ReadUntilContainsAsync(session.Output, "ssh-key", TimeSpan.FromSeconds(20));

        Assert.Contains("ssh-key", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ssh_backplane_supports_user_certificate_auth()
    {
        if (OperatingSystem.IsWindows() || !SshTestContainer.IsAvailable())
        {
            return;
        }

        await using var container = await SshTestContainer.StartAsync();
        var backplane = new SshBackplane(NullLogger<SshBackplane>.Instance);

        await using var session = await backplane.StartAsync(
            "ssh-cert",
            CreateHost(container) with
            {
                SshPrivateKeyPath = container.PrivateKeyPath,
                SshCertificatePath = container.CertificatePath,
                SshAllowAnyHostKey = true
            },
            CreateShellLaunchSpec("ssh-cert"),
            CancellationToken.None);

        await session.WriteInputAsync("echo ssh-cert\n", CancellationToken.None);
        await session.WriteInputAsync("exit\n", CancellationToken.None);

        var output = await TerminalTestHelpers.ReadUntilContainsAsync(session.Output, "ssh-cert", TimeSpan.FromSeconds(20));

        Assert.Contains("ssh-cert", output, StringComparison.OrdinalIgnoreCase);
    }

    private static HostConfig CreateHost(SshTestContainer container) =>
        new()
        {
            Id = "ssh-host",
            BackplaneId = "ssh",
            DisplayName = "SSH",
            ShellExecutable = "/bin/bash",
            ShellArguments = [],
            WorkingDirectory = "/home/clanky",
            SshAddress = "127.0.0.1",
            SshPort = container.Port,
            SshUsername = SshTestContainer.Username
        };

    private static LaunchSpec CreateShellLaunchSpec(string sessionId) =>
        new()
        {
            SessionId = sessionId,
            DisplayCommand = "/bin/bash",
            FileName = "/bin/bash",
            Arguments = [],
            WorkingDirectory = "/home/clanky",
            Cols = 120,
            Rows = 32
        };
}

using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Infrastructure.Backplanes;
using ClankYankers.Server.Infrastructure.Pty;

namespace ClankYankers.Server.IntegrationTests;

public sealed class LocalBackplaneTests
{
    [Fact]
    public async Task Local_backplane_streams_shell_output()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var backplane = new LocalBackplane(new WindowsConPtyProcessFactory());

        await using var session = await backplane.StartAsync(
            "local-stream",
            new HostConfig
            {
                Id = "local-host",
                BackplaneId = "local",
                DisplayName = "Local",
                ShellExecutable = "pwsh.exe",
                ShellArguments = ["-NoLogo"]
            },
            new LaunchSpec
            {
                SessionId = "local-stream",
                DisplayCommand = "pwsh.exe -NoLogo",
                FileName = "pwsh.exe",
                Arguments = ["-NoLogo"],
                Cols = 120,
                Rows = 32
            },
            CancellationToken.None);

        await session.WriteInputAsync("Write-Output 'integration-local'\r\n", CancellationToken.None);
        await session.WriteInputAsync("exit\r\n", CancellationToken.None);

        var output = await TerminalTestHelpers.ReadUntilContainsAsync(
            session.Output,
            "integration-local",
            TimeSpan.FromSeconds(10));

        Assert.Contains("integration-local", output, StringComparison.OrdinalIgnoreCase);
        await session.Completion;
    }

    [Fact]
    public async Task Local_backplane_handles_interactive_input()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var backplane = new LocalBackplane(new WindowsConPtyProcessFactory());

        await using var session = await backplane.StartAsync(
            "local-interactive",
            new HostConfig
            {
                Id = "local-host",
                BackplaneId = "local",
                DisplayName = "Local",
                ShellExecutable = "pwsh.exe",
                ShellArguments = ["-NoLogo"]
            },
            new LaunchSpec
            {
                SessionId = "local-interactive",
                DisplayCommand = "pwsh.exe -NoLogo",
                FileName = "pwsh.exe",
                Arguments = ["-NoLogo"],
                Cols = 120,
                Rows = 32
            },
            CancellationToken.None);

        await session.WriteInputAsync("$name = Read-Host 'Name'\r\n", CancellationToken.None);
        var promptOutput = await TerminalTestHelpers.ReadUntilContainsAsync(
            session.Output,
            "Name:",
            TimeSpan.FromSeconds(10));

        Assert.Contains("Name:", promptOutput, StringComparison.OrdinalIgnoreCase);
        await session.WriteInputAsync("Alice\r\n", CancellationToken.None);
        await session.WriteInputAsync("Write-Output \"done:$name\"\r\n", CancellationToken.None);
        await session.WriteInputAsync("exit\r\n", CancellationToken.None);

        var output = await TerminalTestHelpers.ReadUntilContainsAsync(
            session.Output,
            "done:Alice",
            TimeSpan.FromSeconds(10));

        Assert.Contains("done:Alice", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Local_backplane_isolates_multiple_sessions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var backplane = new LocalBackplane(new WindowsConPtyProcessFactory());

        await using var sessionA = await backplane.StartAsync(
            "local-a",
            new HostConfig
            {
                Id = "local-host",
                BackplaneId = "local",
                DisplayName = "Local",
                ShellExecutable = "pwsh.exe",
                ShellArguments = ["-NoLogo"]
            },
            new LaunchSpec
            {
                SessionId = "local-a",
                DisplayCommand = "pwsh.exe -NoLogo",
                FileName = "pwsh.exe",
                Arguments = ["-NoLogo"],
                Cols = 120,
                Rows = 32
            },
            CancellationToken.None);

        await using var sessionB = await backplane.StartAsync(
            "local-b",
            new HostConfig
            {
                Id = "local-host",
                BackplaneId = "local",
                DisplayName = "Local",
                ShellExecutable = "pwsh.exe",
                ShellArguments = ["-NoLogo"]
            },
            new LaunchSpec
            {
                SessionId = "local-b",
                DisplayCommand = "pwsh.exe -NoLogo",
                FileName = "pwsh.exe",
                Arguments = ["-NoLogo"],
                Cols = 120,
                Rows = 32
            },
            CancellationToken.None);

        await sessionA.WriteInputAsync("Write-Output 'session-a'\r\nexit\r\n", CancellationToken.None);
        await sessionB.WriteInputAsync("Write-Output 'session-b'\r\nexit\r\n", CancellationToken.None);

        var outputA = await TerminalTestHelpers.ReadUntilContainsAsync(sessionA.Output, "session-a", TimeSpan.FromSeconds(10));
        var outputB = await TerminalTestHelpers.ReadUntilContainsAsync(sessionB.Output, "session-b", TimeSpan.FromSeconds(10));

        Assert.DoesNotContain("session-b", outputA, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session-a", outputB, StringComparison.OrdinalIgnoreCase);
    }
}

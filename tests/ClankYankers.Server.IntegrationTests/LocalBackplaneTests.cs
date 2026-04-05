using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Features.Sessions;
using ClankYankers.Server.Infrastructure.Backplanes;
using ClankYankers.Server.Infrastructure.Observability;
using ClankYankers.Server.Infrastructure.Pty;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Fact]
    public async Task Local_backplane_stop_completes_wrapped_session_cleanup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var backplane = new LocalBackplane(new WindowsConPtyProcessFactory());

        await using var interactiveSession = await backplane.StartAsync(
            "local-stop",
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
                SessionId = "local-stop",
                DisplayCommand = "pwsh.exe -NoLogo",
                FileName = "pwsh.exe",
                Arguments = ["-NoLogo"],
                Cols = 120,
                Rows = 32
            },
            CancellationToken.None);

        var session = new Session(
            interactiveSession,
            new SessionSummary
            {
                Id = "local-stop",
                BackplaneId = "local",
                HostId = "local-host",
                ConnectorId = "shell",
                DisplayCommand = "pwsh.exe -NoLogo",
                State = SessionState.Running,
                CreatedAt = DateTimeOffset.UtcNow,
                StartedAt = DateTimeOffset.UtcNow
            },
            new InMemoryEventBus(),
            NullLogger<Session>.Instance);

        await session.StopAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && session.Summary.State != SessionState.Stopped)
        {
            await Task.Delay(25);
        }

        Assert.Equal(SessionState.Stopped, session.Summary.State);
    }

    [Fact]
    public async Task Local_backplane_natural_exit_completes_wrapped_session_cleanup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var backplane = new LocalBackplane(new WindowsConPtyProcessFactory());

        await using var interactiveSession = await backplane.StartAsync(
            "local-exit",
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
                SessionId = "local-exit",
                DisplayCommand = "pwsh.exe -NoLogo",
                FileName = "pwsh.exe",
                Arguments = ["-NoLogo"],
                Cols = 120,
                Rows = 32
            },
            CancellationToken.None);

        var session = new Session(
            interactiveSession,
            new SessionSummary
            {
                Id = "local-exit",
                BackplaneId = "local",
                HostId = "local-host",
                ConnectorId = "shell",
                DisplayCommand = "pwsh.exe -NoLogo",
                State = SessionState.Running,
                CreatedAt = DateTimeOffset.UtcNow,
                StartedAt = DateTimeOffset.UtcNow
            },
            new InMemoryEventBus(),
            NullLogger<Session>.Instance);

        await session.WriteInputAsync("exit\r\n", CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && session.Summary.State != SessionState.Stopped)
        {
            await Task.Delay(25);
        }

        Assert.Equal(SessionState.Stopped, session.Summary.State);
    }
}

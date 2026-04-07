using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Features.Sessions;
using ClankYankers.Server.Infrastructure.Connectors;
using ClankYankers.Server.Infrastructure.Observability;
using ClankYankers.Server.Infrastructure.Registry;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace ClankYankers.Server.UnitTests;

public sealed class SessionOrchestratorTests
{
    [Fact]
    public async Task StopAsync_removes_completed_sessions_from_the_live_registry()
    {
        var orchestrator = new SessionOrchestrator(
            new FakeConfigStore(CreateLocalShellConfig()),
            new BackplaneRegistry([new FakeBackplane("local")]),
            new ConnectorRegistry([new ShellConnector()]),
            new SessionRegistry(),
            new InMemoryEventBus(LoggerFactory.Create(_ => { }).CreateLogger<InMemoryEventBus>()),
            LoggerFactory.Create(_ => { }));

        var session = await orchestrator.CreateAsync(
            new CreateSessionRequest
            {
                BackplaneId = "local",
                HostId = "local-host",
                ConnectorId = "shell",
                Cols = 120,
                Rows = 34
            },
            CancellationToken.None);

        Assert.Single(orchestrator.ListSessions());

        await orchestrator.StopAsync(session.Id, CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && orchestrator.ListSessions().Count > 0)
        {
            await Task.Delay(25);
        }

        Assert.Empty(orchestrator.ListSessions());
    }

    [Fact]
    public async Task CreateAsync_rejects_hosts_that_do_not_belong_to_the_selected_backplane()
    {
        var orchestrator = new SessionOrchestrator(
            new FakeConfigStore(CreateLocalDockerShellConfig()),
            new BackplaneRegistry([new FakeBackplane("local"), new FakeBackplane("docker")]),
            new ConnectorRegistry([new ShellConnector()]),
            new SessionRegistry(),
            new InMemoryEventBus(LoggerFactory.Create(_ => { }).CreateLogger<InMemoryEventBus>()),
            LoggerFactory.Create(_ => { }));

        var error = await Assert.ThrowsAsync<SessionRequestResolutionException>(() =>
            orchestrator.CreateAsync(
                new CreateSessionRequest
                {
                    BackplaneId = "docker",
                    HostId = "local-host",
                    ConnectorId = "shell",
                    Cols = 120,
                    Rows = 34
                },
                CancellationToken.None));

        Assert.Contains("does not belong to backplane", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_resolves_connectors_by_kind_instead_of_configured_id()
    {
        var config = AppConfig.CreateDefault() with
        {
            Backplanes =
            [
                new BackplaneDefinition
                {
                    Id = "local",
                    DisplayName = "Local",
                    Kind = "local"
                }
            ],
            Hosts =
            [
                new HostConfig
                {
                    Id = "local-host",
                    BackplaneId = "local",
                    DisplayName = "This machine",
                    ShellExecutable = "pwsh.exe",
                    ShellArguments = ["-NoLogo"]
                }
            ],
            Connectors =
            [
                new ConnectorDefinition
                {
                    Id = "claude-team",
                    DisplayName = "Claude Team",
                    Kind = "claude",
                    LaunchCommand = "claude",
                    DefaultPermissionMode = "acceptEdits"
                }
            ],
            Experiments = []
        };
        var backplane = new FakeBackplane("local");
        var orchestrator = new SessionOrchestrator(
            new FakeConfigStore(config),
            new BackplaneRegistry([backplane]),
            new ConnectorRegistry([new ClaudeConnector()]),
            new SessionRegistry(),
            new InMemoryEventBus(LoggerFactory.Create(_ => { }).CreateLogger<InMemoryEventBus>()),
            LoggerFactory.Create(_ => { }));

        var session = await orchestrator.CreateAsync(
            new CreateSessionRequest
            {
                BackplaneId = "local",
                HostId = "local-host",
                ConnectorId = "claude-team",
                Model = "opus-4.6",
                Cols = 120,
                Rows = 34
            },
            CancellationToken.None);

        Assert.Equal("claude-team", session.ConnectorId);
        Assert.NotNull(backplane.LastLaunchSpec);
        Assert.Equal("claude", backplane.LastLaunchSpec!.FileName);
        Assert.Contains("--model", backplane.LastLaunchSpec.Arguments);
        Assert.Contains("opus-4.6", backplane.LastLaunchSpec.Arguments);
    }

    [Fact]
    public async Task CreateAsync_resolves_backplanes_by_kind_instead_of_configured_id()
    {
        var config = AppConfig.CreateDefault() with
        {
            Backplanes =
            [
                new BackplaneDefinition
                {
                    Id = "local-alt",
                    DisplayName = "Local Alt",
                    Kind = "local"
                }
            ],
            Hosts =
            [
                new HostConfig
                {
                    Id = "local-host",
                    BackplaneId = "local-alt",
                    DisplayName = "Local",
                    ShellExecutable = "pwsh.exe"
                }
            ],
            Connectors =
            [
                new ConnectorDefinition
                {
                    Id = "shell",
                    DisplayName = "Shell",
                    Kind = "shell"
                }
            ],
            Experiments = []
        };
        var backplane = new FakeBackplane("local");
        var orchestrator = new SessionOrchestrator(
            new FakeConfigStore(config),
            new BackplaneRegistry([backplane]),
            new ConnectorRegistry([new ShellConnector()]),
            new SessionRegistry(),
            new InMemoryEventBus(LoggerFactory.Create(_ => { }).CreateLogger<InMemoryEventBus>()),
            LoggerFactory.Create(_ => { }));

        await orchestrator.CreateAsync(
            new CreateSessionRequest
            {
                BackplaneId = "local-alt",
                HostId = "local-host",
                ConnectorId = "shell",
                Cols = 120,
                Rows = 34
            },
            CancellationToken.None);

        Assert.NotNull(backplane.LastLaunchSpec);
        Assert.Equal("pwsh.exe", backplane.LastLaunchSpec!.FileName);
    }

    [Fact]
    public async Task CreateAsync_uses_request_working_directory_override_when_provided()
    {
        var overrideDirectory = Path.Combine(Path.GetTempPath(), $"clanky-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(overrideDirectory);

        var config = CreateLocalShellConfig() with
        {
            Hosts =
            [
                new HostConfig
                {
                    Id = "local-host",
                    BackplaneId = "local",
                    DisplayName = "This machine",
                    ShellExecutable = "pwsh.exe",
                    ShellArguments = ["-NoLogo"],
                    WorkingDirectory = null
                }
            ]
        };
        var backplane = new FakeBackplane("local");
        var orchestrator = new SessionOrchestrator(
            new FakeConfigStore(config),
            new BackplaneRegistry([backplane]),
            new ConnectorRegistry([new ShellConnector()]),
            new SessionRegistry(),
            new InMemoryEventBus(LoggerFactory.Create(_ => { }).CreateLogger<InMemoryEventBus>()),
            LoggerFactory.Create(_ => { }));

        try
        {
            await orchestrator.CreateAsync(
                new CreateSessionRequest
                {
                    BackplaneId = "local",
                    HostId = "local-host",
                    ConnectorId = "shell",
                    WorkingDirectory = overrideDirectory,
                    Cols = 120,
                    Rows = 34
                },
                CancellationToken.None);

            Assert.NotNull(backplane.LastLaunchSpec);
            Assert.Equal(overrideDirectory, backplane.LastLaunchSpec!.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(overrideDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_inherits_the_host_working_directory_when_no_override_is_provided()
    {
        var hostDirectory = Path.Combine(Path.GetTempPath(), $"clanky-host-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(hostDirectory);

        var config = CreateLocalShellConfig() with
        {
            Hosts =
            [
                new HostConfig
                {
                    Id = "local-host",
                    BackplaneId = "local",
                    DisplayName = "This machine",
                    ShellExecutable = "pwsh.exe",
                    ShellArguments = ["-NoLogo"],
                    WorkingDirectory = hostDirectory
                }
            ]
        };
        var backplane = new FakeBackplane("local");
        var orchestrator = new SessionOrchestrator(
            new FakeConfigStore(config),
            new BackplaneRegistry([backplane]),
            new ConnectorRegistry([new ShellConnector()]),
            new SessionRegistry(),
            new InMemoryEventBus(LoggerFactory.Create(_ => { }).CreateLogger<InMemoryEventBus>()),
            LoggerFactory.Create(_ => { }));

        try
        {
            await orchestrator.CreateAsync(
                new CreateSessionRequest
                {
                    BackplaneId = "local",
                    HostId = "local-host",
                    ConnectorId = "shell",
                    Cols = 120,
                    Rows = 34
                },
                CancellationToken.None);

            Assert.NotNull(backplane.LastLaunchSpec);
            Assert.Equal(hostDirectory, backplane.LastLaunchSpec!.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(hostDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_rejects_disabled_connectors()
    {
        var config = AppConfig.CreateDefault() with
        {
            Backplanes =
            [
                new BackplaneDefinition
                {
                    Id = "local",
                    DisplayName = "Local",
                    Kind = "local"
                }
            ],
            Hosts =
            [
                new HostConfig
                {
                    Id = "local-host",
                    BackplaneId = "local",
                    DisplayName = "This machine",
                    ShellExecutable = "pwsh.exe",
                    ShellArguments = ["-NoLogo"]
                }
            ],
            Connectors =
            [
                new ConnectorDefinition
                {
                    Id = "shell",
                    DisplayName = "Shell",
                    Kind = "shell",
                    Enabled = false
                }
            ],
            Experiments = []
        };
        var orchestrator = new SessionOrchestrator(
            new FakeConfigStore(config),
            new BackplaneRegistry([new FakeBackplane("local")]),
            new ConnectorRegistry([new ShellConnector()]),
            new SessionRegistry(),
            new InMemoryEventBus(LoggerFactory.Create(_ => { }).CreateLogger<InMemoryEventBus>()),
            LoggerFactory.Create(_ => { }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.CreateAsync(
                new CreateSessionRequest
                {
                    BackplaneId = "local",
                    HostId = "local-host",
                    ConnectorId = "shell",
                    Cols = 120,
                    Rows = 34
                },
                CancellationToken.None));
    }

    private sealed class FakeConfigStore(AppConfig? config = null) : IConfigStore
    {
        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(config ?? AppConfig.CreateDefault());

        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeBackplane(string id) : IBackplane
    {
        public LaunchSpec? LastLaunchSpec { get; private set; }

        public string Kind => id;

        public Task<IInteractiveSession> StartAsync(
            string sessionId,
            HostConfig host,
            LaunchSpec launchSpec,
            CancellationToken cancellationToken)
        {
            LastLaunchSpec = launchSpec;
            return Task.FromResult<IInteractiveSession>(new FakeInteractiveSession(sessionId));
        }
    }

    private sealed class FakeInteractiveSession(string sessionId) : IInteractiveSession
    {
        private readonly Channel<TerminalOutputChunk> _output = Channel.CreateUnbounded<TerminalOutputChunk>();
        private readonly TaskCompletionSource<int?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string SessionId => sessionId;

        public ChannelReader<TerminalOutputChunk> Output => _output.Reader;

        public Task<int?> Completion => _completion.Task;

        public Task WriteInputAsync(string data, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _output.Writer.TryComplete();
            _completion.TrySetResult(0);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _output.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private static AppConfig CreateLocalShellConfig() =>
        AppConfig.CreateDefault() with
        {
            Backplanes =
            [
                new BackplaneDefinition
                {
                    Id = "local",
                    DisplayName = "Local",
                    Kind = "local"
                }
            ],
            Hosts =
            [
                new HostConfig
                {
                    Id = "local-host",
                    BackplaneId = "local",
                    DisplayName = "This machine",
                    ShellExecutable = "pwsh.exe",
                    ShellArguments = ["-NoLogo"]
                }
            ],
            Connectors =
            [
                new ConnectorDefinition
                {
                    Id = "shell",
                    DisplayName = "Shell",
                    Kind = "shell"
                }
            ],
            Experiments = []
        };

    private static AppConfig CreateLocalDockerShellConfig() =>
        AppConfig.CreateDefault() with
        {
            Connectors =
            [
                new ConnectorDefinition
                {
                    Id = "shell",
                    DisplayName = "Shell",
                    Kind = "shell"
                }
            ],
            Experiments = []
        };
}

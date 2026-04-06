using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Features.Experiments;
using ClankYankers.Server.Features.Sessions;
using ClankYankers.Server.Infrastructure.Connectors;
using ClankYankers.Server.Infrastructure.Observability;
using ClankYankers.Server.Infrastructure.Registry;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace ClankYankers.Server.UnitTests;

public sealed class ExperimentOrchestratorTests
{
    [Fact]
    public async Task RunAsync_creates_a_run_group_and_tags_sessions()
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
                    Kind = "shell"
                }
            ],
            Experiments =
            [
                new ExperimentDefinition
                {
                    Id = "local-shell-smoke",
                    DisplayName = "Local shell smoke",
                    HostIds = ["local-host"],
                    ConnectorIds = ["shell"]
                }
            ]
        };

        var loggerFactory = LoggerFactory.Create(_ => { });
        var sessionOrchestrator = new SessionOrchestrator(
            new FakeConfigStore(config),
            new BackplaneRegistry([new FakeBackplane("local")]),
            new ConnectorRegistry([new ShellConnector()]),
            new SessionRegistry(),
            new InMemoryEventBus(loggerFactory.CreateLogger<InMemoryEventBus>()),
            loggerFactory);
        var experimentOrchestrator = new ExperimentOrchestrator(
            new FakeConfigStore(config),
            sessionOrchestrator,
            new BackplaneRegistry([new FakeBackplane("local")]),
            new ConnectorRegistry([new ShellConnector()]),
            loggerFactory.CreateLogger<ExperimentOrchestrator>());

        var run = await experimentOrchestrator.RunAsync("local-shell-smoke", CancellationToken.None);

        Assert.Equal("local-shell-smoke", run.ExperimentId);
        Assert.Equal(1, run.VariantCount);
        Assert.Equal(1, run.ActiveSessionCount);

        var liveSession = Assert.Single(sessionOrchestrator.ListSessions());
        Assert.Equal("local-shell-smoke", liveSession.ExperimentId);
        Assert.Equal(liveSession.Id, Assert.Single(run.Variants).SessionId);
    }

    private sealed class FakeConfigStore(AppConfig config) : IConfigStore
    {
        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(config);

        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeBackplane(string kind) : IBackplane
    {
        public string Kind => kind;

        public Task<IInteractiveSession> StartAsync(string sessionId, HostConfig host, LaunchSpec launchSpec, CancellationToken cancellationToken) =>
            Task.FromResult<IInteractiveSession>(new FakeInteractiveSession(sessionId));
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
}

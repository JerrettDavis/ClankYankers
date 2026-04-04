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
            new FakeConfigStore(),
            new BackplaneRegistry([new FakeBackplane("local")]),
            new ConnectorRegistry([new ShellConnector()]),
            new SessionRegistry(),
            new InMemoryEventBus(),
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
            new FakeConfigStore(),
            new BackplaneRegistry([new FakeBackplane("local"), new FakeBackplane("docker")]),
            new ConnectorRegistry([new ShellConnector()]),
            new SessionRegistry(),
            new InMemoryEventBus(),
            LoggerFactory.Create(_ => { }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
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

    private sealed class FakeConfigStore : IConfigStore
    {
        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(AppConfig.CreateDefault());

        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeBackplane(string id) : IBackplane
    {
        public string Id => id;

        public Task<IInteractiveSession> StartAsync(
            string sessionId,
            HostConfig host,
            LaunchSpec launchSpec,
            CancellationToken cancellationToken) =>
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

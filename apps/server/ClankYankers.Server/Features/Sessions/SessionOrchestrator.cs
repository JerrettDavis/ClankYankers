using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Events;
using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Infrastructure.Registry;

namespace ClankYankers.Server.Features.Sessions;

public sealed class SessionOrchestrator(
    IConfigStore configStore,
    BackplaneRegistry backplaneRegistry,
    ConnectorRegistry connectorRegistry,
    SessionRegistry sessionRegistry,
    IEventBus eventBus,
    ILoggerFactory loggerFactory)
{
    public IReadOnlyList<SessionSummary> ListSessions() =>
        sessionRegistry.List().Select(session => session.Summary).ToArray();

    public bool TryGet(string sessionId, out Session? session) =>
        sessionRegistry.TryGet(sessionId, out session);

    public async Task<SessionSummary> CreateAsync(CreateSessionRequest request, CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);

        var connectorDefinition = ResolveConnectorDefinition(config, request);
        var backplaneDefinition = ResolveBackplaneDefinition(config, request, connectorDefinition);
        var host = ResolveHost(config, request, backplaneDefinition);

        if (!connectorRegistry.TryGet(connectorDefinition.Id, out var connector))
        {
            throw new InvalidOperationException($"Connector '{connectorDefinition.Id}' is not registered.");
        }

        if (!backplaneRegistry.TryGet(backplaneDefinition.Id, out var backplane))
        {
            throw new InvalidOperationException($"Backplane '{backplaneDefinition.Id}' is not registered.");
        }

        var sessionId = Guid.NewGuid().ToString("n");
        var launchSpec = connector.BuildLaunchSpec(sessionId, request, host, connectorDefinition);
        var createdAt = DateTimeOffset.UtcNow;

        await eventBus.PublishAsync(new CommandExecutionEvent(sessionId, launchSpec.DisplayCommand, createdAt), cancellationToken);
        await eventBus.PublishAsync(new SessionLifecycleEvent(
            sessionId,
            SessionState.Starting.ToString(),
            backplaneDefinition.Id,
            host.Id,
            connectorDefinition.Id,
            createdAt), cancellationToken);

        try
        {
            var runtime = await backplane.StartAsync(sessionId, host, launchSpec, cancellationToken);
            var summary = new SessionSummary
            {
                Id = sessionId,
                BackplaneId = backplaneDefinition.Id,
                HostId = host.Id,
                ConnectorId = connectorDefinition.Id,
                DisplayCommand = launchSpec.DisplayCommand,
                State = SessionState.Running,
                CreatedAt = createdAt,
                StartedAt = DateTimeOffset.UtcNow
            };

            var sessionLogger = loggerFactory.CreateLogger<Session>();
            var session = new Session(runtime, summary, eventBus, sessionLogger);
            sessionRegistry.Add(session);
            _ = ObserveSessionLifetimeAsync(session, loggerFactory.CreateLogger<SessionOrchestrator>());

            await eventBus.PublishAsync(new SessionLifecycleEvent(
                sessionId,
                SessionState.Running.ToString(),
                backplaneDefinition.Id,
                host.Id,
                connectorDefinition.Id,
                DateTimeOffset.UtcNow), cancellationToken);

            return summary;
        }
        catch (Exception exception)
        {
            await eventBus.PublishAsync(new SessionLifecycleEvent(
                sessionId,
                SessionState.Failed.ToString(),
                backplaneDefinition.Id,
                host.Id,
                connectorDefinition.Id,
                DateTimeOffset.UtcNow,
                null,
                exception.Message), cancellationToken);
            throw;
        }
    }

    public async Task StopAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!sessionRegistry.TryGet(sessionId, out var session) || session is null)
        {
            throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
        }

        await session.StopAsync(cancellationToken);
    }

    private async Task ObserveSessionLifetimeAsync(Session session, ILogger<SessionOrchestrator> logger)
    {
        try
        {
            await session.Completion;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Session {SessionId} completed with an error during cleanup.", session.Id);
        }
        finally
        {
            sessionRegistry.Remove(session.Id);

            try
            {
                await session.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Session {SessionId} disposal failed during cleanup.", session.Id);
            }
        }
    }

    private static ConnectorDefinition ResolveConnectorDefinition(AppConfig config, CreateSessionRequest request)
    {
        var connectorId = request.ConnectorId ?? "shell";
        return config.Connectors.FirstOrDefault(connector => connector.Id.Equals(connectorId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown connector '{connectorId}'.");
    }

    private static BackplaneDefinition ResolveBackplaneDefinition(
        AppConfig config,
        CreateSessionRequest request,
        ConnectorDefinition connector)
    {
        var requestedBackplane = request.BackplaneId ?? "local";
        return config.Backplanes.FirstOrDefault(backplane => backplane.Id.Equals(requestedBackplane, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown backplane '{requestedBackplane}' for connector '{connector.Id}'.");
    }

    private static HostConfig ResolveHost(AppConfig config, CreateSessionRequest request, BackplaneDefinition backplane)
    {
        if (!string.IsNullOrWhiteSpace(request.HostId))
        {
            var host = config.Hosts.FirstOrDefault(candidate => candidate.Id.Equals(request.HostId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Unknown host '{request.HostId}'.");

            if (!host.BackplaneId.Equals(backplane.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Host '{host.Id}' does not belong to backplane '{backplane.Id}'.");
            }

            return host;
        }

        return config.Hosts.FirstOrDefault(host => host.BackplaneId.Equals(backplane.Id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No host configured for backplane '{backplane.Id}'.");
    }
}

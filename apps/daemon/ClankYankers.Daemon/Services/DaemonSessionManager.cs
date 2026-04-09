using ClankYankers.Daemon.Contracts;
using ClankYankers.Daemon.Runtime;
using ClankYankers.Remote.Contracts;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ClankYankers.Daemon.Services;

internal sealed class DaemonSessionManager(ILogger<DaemonSessionManager> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, IDaemonInteractiveSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _startLock = new(1, 1);

    public async Task<RemoteSessionStartedResponse> StartAsync(StartRemoteSessionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new InvalidOperationException("Session requests must include a session id.");
        }

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.ContainsKey(request.SessionId))
            {
                throw new InvalidOperationException($"Session '{request.SessionId}' already exists.");
            }

            var session = await CreateSessionAsync(request, cancellationToken);
            if (!_sessions.TryAdd(request.SessionId, session))
            {
                await session.DisposeAsync();
                throw new InvalidOperationException($"Session '{request.SessionId}' already exists.");
            }

            _ = ObserveCompletionAsync(session);
            logger.LogInformation("Started daemon session {SessionId} using executor {ExecutorKind}", request.SessionId, request.ExecutorKind);

            return new RemoteSessionStartedResponse(request.SessionId, $"/ws/session/{request.SessionId}");
        }
        finally
        {
            _startLock.Release();
        }
    }

    public bool TryGet(string sessionId, out IDaemonInteractiveSession? session) =>
        _sessions.TryGetValue(sessionId, out session);

    public async Task StopAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
        }

        await session.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        _startLock.Dispose();
    }

    private static Task<IDaemonInteractiveSession> CreateSessionAsync(
        StartRemoteSessionRequest request,
        CancellationToken cancellationToken) =>
        request.ExecutorKind.Equals("docker", StringComparison.OrdinalIgnoreCase)
            ? DockerInteractiveSession.StartAsync(request, cancellationToken)
            : PtyInteractiveProcessSession.StartAsync(request, cancellationToken);

    private async Task ObserveCompletionAsync(IDaemonInteractiveSession session)
    {
        try
        {
            await session.Completion;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Daemon session {SessionId} completed with an error.", session.SessionId);
        }
        finally
        {
            _sessions.TryRemove(session.SessionId, out _);

            try
            {
                await session.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Daemon session {SessionId} disposal failed.", session.SessionId);
            }
        }
    }
}

using ClankYankers.Daemon.Contracts;
using ClankYankers.Daemon.Runtime;
using ClankYankers.Remote.Contracts;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ClankYankers.Daemon.Services;

internal sealed partial class DaemonSessionManager(ILogger<DaemonSessionManager> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, IDaemonInteractiveSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _startLock = new(1, 1);

    // Session IDs must be alphanumeric with hyphens/underscores only (GUID-style or similar).
    // This prevents log-forging by ensuring SessionId cannot contain newlines or control chars.
    [GeneratedRegex(@"^[\w\-]{1,128}$", RegexOptions.Compiled)]
    private static partial Regex SessionIdPattern();

    public async Task<RemoteSessionStartedResponse> StartAsync(StartRemoteSessionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new InvalidOperationException("Session requests must include a session id.");
        }

        if (!SessionIdPattern().IsMatch(request.SessionId))
        {
            throw new InvalidOperationException("Session ID contains invalid characters.");
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
            logger.LogInformation(
                "Started daemon session {SessionId} using executor {ExecutorKind}",
                Sanitize(request.SessionId),
                Sanitize(request.ExecutorKind));

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
            logger.LogWarning(exception, "Daemon session {SessionId} completed with an error.", Sanitize(session.SessionId));
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
                logger.LogWarning(exception, "Daemon session {SessionId} disposal failed.", Sanitize(session.SessionId));
            }
        }
    }

    /// <summary>
    /// Removes newlines and other control characters from a value before it is written to logs,
    /// preventing log-forging attacks (CWE-117).
    /// </summary>
    private static string Sanitize(string? value) =>
        value is null
            ? string.Empty
            : value
                .Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal);
}

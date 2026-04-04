using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Events;
using ClankYankers.Server.Core.Models;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ClankYankers.Server.Features.Sessions;

public sealed class Session : IAsyncDisposable
{
    private const int MaxHistoryMessages = 256;

    private readonly IInteractiveSession _interactiveSession;
    private readonly IEventBus _eventBus;
    private readonly ILogger<Session> _logger;
    private readonly ConcurrentDictionary<Guid, Channel<TerminalServerMessage>> _subscribers = new();
    private readonly Queue<TerminalServerMessage> _history = new();
    private readonly Lock _gate = new();
    private readonly Task _pumpTask;
    private SessionSummary _summary;

    public Session(
        IInteractiveSession interactiveSession,
        SessionSummary summary,
        IEventBus eventBus,
        ILogger<Session> logger)
    {
        _interactiveSession = interactiveSession;
        _summary = summary;
        _eventBus = eventBus;
        _logger = logger;
        _pumpTask = Task.Run(PumpOutputAsync);
    }

    public string Id => _summary.Id;

    public SessionSummary Summary
    {
        get
        {
            lock (_gate)
            {
                return _summary;
            }
        }
    }

    internal Task Completion => _pumpTask;

    public SessionSubscription Attach()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<TerminalServerMessage>();

        lock (_gate)
        {
            _subscribers[id] = channel;
            channel.Writer.TryWrite(new TerminalServerMessage
            {
                Type = "state",
                State = _summary.State.ToString()
            });

            foreach (var item in _history)
            {
                channel.Writer.TryWrite(item);
            }
        }

        return new SessionSubscription(id, channel.Reader);
    }

    public void Detach(Guid subscriberId)
    {
        if (_subscribers.TryRemove(subscriberId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public Task WriteInputAsync(string data, CancellationToken cancellationToken) =>
        _interactiveSession.WriteInputAsync(data, cancellationToken);

    public Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken) =>
        _interactiveSession.ResizeAsync(cols, rows, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        _interactiveSession.StopAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        foreach (var subscriberId in _subscribers.Keys)
        {
            Detach(subscriberId);
        }

        await _interactiveSession.DisposeAsync();
    }

    private async Task PumpOutputAsync()
    {
        try
        {
            await foreach (var chunk in _interactiveSession.Output.ReadAllAsync())
            {
                Broadcast(new TerminalServerMessage
                {
                    Type = "output",
                    Data = chunk.Data
                });
            }

            var exitCode = await _interactiveSession.Completion;
            await TransitionToAsync(SessionState.Stopped, exitCode, null);
            Broadcast(new TerminalServerMessage
            {
                Type = "exit",
                ExitCode = exitCode,
                State = SessionState.Stopped.ToString()
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Session {SessionId} output pump failed", Id);
            await TransitionToAsync(SessionState.Failed, null, exception.Message);
            await _eventBus.PublishAsync(
                new RuntimeErrorEvent(Id, exception.Message, DateTimeOffset.UtcNow));
            Broadcast(new TerminalServerMessage
            {
                Type = "error",
                Message = exception.Message,
                State = SessionState.Failed.ToString()
            });
        }
        finally
        {
            foreach (var channel in _subscribers.Values)
            {
                channel.Writer.TryComplete();
            }
        }
    }

    private void Broadcast(TerminalServerMessage message)
    {
        lock (_gate)
        {
            _history.Enqueue(message);
            while (_history.Count > MaxHistoryMessages)
            {
                _history.Dequeue();
            }

            foreach (var channel in _subscribers.Values)
            {
                channel.Writer.TryWrite(message);
            }
        }
    }    

    private async Task TransitionToAsync(SessionState state, int? exitCode, string? error)
    {
        SessionSummary updated;
        lock (_gate)
        {
            updated = _summary with
            {
                State = state,
                ExitCode = exitCode,
                Error = error,
                EndedAt = DateTimeOffset.UtcNow
            };

            _summary = updated;
        }

        await _eventBus.PublishAsync(new SessionLifecycleEvent(
            updated.Id,
            updated.State.ToString(),
            updated.BackplaneId,
            updated.HostId,
            updated.ConnectorId,
            DateTimeOffset.UtcNow,
            exitCode,
            error));
    }
}

public readonly record struct SessionSubscription(Guid Id, ChannelReader<TerminalServerMessage> Reader);

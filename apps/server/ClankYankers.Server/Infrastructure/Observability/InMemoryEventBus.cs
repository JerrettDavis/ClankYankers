using ClankYankers.Server.Core.Contracts;
using System.Collections.Concurrent;

namespace ClankYankers.Server.Infrastructure.Observability;

public sealed class InMemoryEventBus(ILogger<InMemoryEventBus> logger) : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Func<object, CancellationToken, Task>>> _handlers = new();

    public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
    {
        var wrapped = new Func<object, CancellationToken, Task>((eventData, cancellationToken) =>
            handler((TEvent)eventData, cancellationToken));

        var handlers = _handlers.GetOrAdd(typeof(TEvent), _ => []);
        lock (handlers)
        {
            handlers.Add(wrapped);
        }
    }

    public async Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
        {
            return;
        }

        List<Func<object, CancellationToken, Task>> snapshot;
        lock (handlers)
        {
            snapshot = [.. handlers];
        }

        foreach (var handler in snapshot)
        {
            try
            {
                await handler(eventData!, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Event handler failed for {EventType}.", typeof(TEvent).Name);
            }
        }
    }
}

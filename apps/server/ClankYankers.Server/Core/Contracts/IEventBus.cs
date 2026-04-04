namespace ClankYankers.Server.Core.Contracts;

public interface IEventBus
{
    void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler);

    Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default);
}

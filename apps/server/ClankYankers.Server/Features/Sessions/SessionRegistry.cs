namespace ClankYankers.Server.Features.Sessions;

public sealed class SessionRegistry
{
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public IReadOnlyList<Session> List()
    {
        lock (_gate)
        {
            return [.. _sessions.Values.OrderByDescending(session => session.Summary.CreatedAt)];
        }
    }

    public bool TryGet(string sessionId, out Session? session)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(sessionId, out session);
        }
    }

    public void Add(Session session)
    {
        lock (_gate)
        {
            _sessions[session.Id] = session;
        }
    }

    public bool Remove(string sessionId)
    {
        lock (_gate)
        {
            return _sessions.Remove(sessionId);
        }
    }
}

using ClankYankers.Server.Core.Contracts;

namespace ClankYankers.Server.Infrastructure.Registry;

public sealed class ConnectorRegistry(IEnumerable<IAgentConnector> connectors)
{
    private readonly IReadOnlyDictionary<string, IAgentConnector> _connectors =
        connectors.ToDictionary(connector => connector.Id, StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string id, out IAgentConnector connector) => _connectors.TryGetValue(id, out connector!);

    public IReadOnlyCollection<string> Ids => _connectors.Keys.ToArray();
}

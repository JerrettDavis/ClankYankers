using ClankYankers.Server.Core.Contracts;

namespace ClankYankers.Server.Infrastructure.Registry;

public sealed class ConnectorRegistry(IEnumerable<IAgentConnector> connectors)
{
    private readonly IReadOnlyDictionary<string, IAgentConnector> _connectors =
        connectors.ToDictionary(connector => connector.Kind, StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string kind, out IAgentConnector connector) => _connectors.TryGetValue(kind, out connector!);

    public IReadOnlyCollection<string> Kinds => _connectors.Keys.ToArray();
}

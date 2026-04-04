using ClankYankers.Server.Core.Contracts;

namespace ClankYankers.Server.Infrastructure.Registry;

public sealed class BackplaneRegistry(IEnumerable<IBackplane> backplanes)
{
    private readonly IReadOnlyDictionary<string, IBackplane> _backplanes =
        backplanes.ToDictionary(backplane => backplane.Id, StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string id, out IBackplane backplane) => _backplanes.TryGetValue(id, out backplane!);

    public IReadOnlyCollection<string> Ids => _backplanes.Keys.ToArray();
}

using ClankYankers.Server.Core.Contracts;

namespace ClankYankers.Server.Infrastructure.Registry;

public sealed class BackplaneRegistry(IEnumerable<IBackplane> backplanes)
{
    private readonly IReadOnlyDictionary<string, IBackplane> _backplanes =
        backplanes.ToDictionary(backplane => backplane.Kind, StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string kind, out IBackplane backplane) => _backplanes.TryGetValue(kind, out backplane!);

    public IReadOnlyCollection<string> Kinds => _backplanes.Keys.ToArray();
}

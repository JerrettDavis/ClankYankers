using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Core.Contracts;

public interface IConfigStore
{
    Task<AppConfig> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppConfig config, CancellationToken cancellationToken);
}

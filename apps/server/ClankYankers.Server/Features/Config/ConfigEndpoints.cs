using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Features.Config;

public static class ConfigEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/config", async (IConfigStore configStore, CancellationToken cancellationToken) =>
        {
            var config = await configStore.LoadAsync(cancellationToken);
            return Results.Ok(config);
        });

        group.MapPut("/config", async (AppConfig config, IConfigStore configStore, CancellationToken cancellationToken) =>
        {
            var errors = ConfigValidator.Validate(config);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            await configStore.SaveAsync(config, cancellationToken);
            return Results.Ok(config);
        });
    }
}

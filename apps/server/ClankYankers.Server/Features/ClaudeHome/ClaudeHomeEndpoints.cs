using ClankYankers.Server.Infrastructure.ClaudeHome;

namespace ClankYankers.Server.Features.ClaudeHome;

public static class ClaudeHomeEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/claude-home/catalog", (ClaudeHomeCatalog claudeHomeCatalog) =>
            Results.Ok(claudeHomeCatalog.LoadCatalog()));
    }
}

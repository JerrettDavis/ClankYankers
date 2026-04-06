using ClankYankers.Server.Features.Sessions;

namespace ClankYankers.Server.Features.Experiments;

public static class ExperimentEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/experiment-runs", (ExperimentOrchestrator orchestrator) =>
            Results.Ok(orchestrator.ListRuns()));

        group.MapPost("/experiments/{experimentId}/runs", async (
            string experimentId,
            ExperimentOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var run = await orchestrator.RunAsync(experimentId, cancellationToken);
                return Results.Ok(run);
            }
            catch (InvalidOperationException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["experiment"] = [exception.Message]
                });
            }
        });
    }
}

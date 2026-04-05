using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Features.Sessions;

public static class SessionEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/app-state", async (
            IConfigStore configStore,
            SessionOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            var config = await configStore.LoadAsync(cancellationToken);
            return Results.Ok(new
            {
                config,
                sessions = orchestrator.ListSessions()
            });
        });

        group.MapGet("/sessions", (SessionOrchestrator orchestrator) =>
            Results.Ok(orchestrator.ListSessions()));

        group.MapPost("/sessions", async (
            CreateSessionRequest request,
            SessionOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            var errors = SessionRequestValidator.Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            try
            {
                var session = await orchestrator.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/sessions/{session.Id}", session);
            }
            catch (InvalidOperationException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["session"] = [exception.Message]
                });
            }
        });

        group.MapPost("/sessions/{sessionId}/stop", async (
            string sessionId,
            SessionOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            await orchestrator.StopAsync(sessionId, cancellationToken);
            return Results.Accepted($"/api/sessions/{sessionId}");
        });
    }
}

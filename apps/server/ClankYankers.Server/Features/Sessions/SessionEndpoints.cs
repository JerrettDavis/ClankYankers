using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Features.Config;
using ClankYankers.Server.Features.Experiments;
using ClankYankers.Server.Infrastructure.ClaudeHome;
using ClankYankers.Server.Infrastructure.Registry;

namespace ClankYankers.Server.Features.Sessions;

public static class SessionEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/app-state", async (
            IConfigStore configStore,
            SessionOrchestrator orchestrator,
            ExperimentOrchestrator experimentOrchestrator,
            ClaudeHomeCatalog claudeHomeCatalog,
            BackplaneRegistry backplaneRegistry,
            ConnectorRegistry connectorRegistry,
            CancellationToken cancellationToken) =>
        {
            var config = await configStore.LoadAsync(cancellationToken);
            ConfigValidator.ThrowIfInvalid(config, backplaneRegistry.Kinds, connectorRegistry.Kinds);
            return Results.Ok(new
            {
                config,
                sessions = orchestrator.ListSessions(),
                experimentRuns = experimentOrchestrator.ListRuns(),
                claudeHome = claudeHomeCatalog.Load()
            });
        });

        group.MapGet("/sessions", (SessionOrchestrator orchestrator) =>
            Results.Ok(orchestrator.ListSessions()));

        group.MapPost("/sessions", async (
            CreateSessionRequest request,
            IConfigStore configStore,
            ClaudeHomeCatalog claudeHomeCatalog,
            SessionOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            var errors = SessionRequestValidator.Validate(request);
            await AppendConnectorSpecificErrorsAsync(errors, request, configStore, claudeHomeCatalog, cancellationToken);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            try
            {
                var session = await orchestrator.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/sessions/{session.Id}", session);
            }
            catch (SessionLaunchValidationException exception)
            {
                return Results.ValidationProblem(exception.Errors);
            }
            catch (SessionRequestResolutionException exception)
            {
                return Results.ValidationProblem(exception.Errors);
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

    private static async Task AppendConnectorSpecificErrorsAsync(
        IDictionary<string, string[]> errors,
        CreateSessionRequest request,
        IConfigStore configStore,
        ClaudeHomeCatalog claudeHomeCatalog,
        CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        var connectorId = string.IsNullOrWhiteSpace(request.ConnectorId) ? "shell" : request.ConnectorId.Trim();
        var connector = config.Connectors.FirstOrDefault(candidate =>
            candidate.Enabled &&
            candidate.Id.Equals(connectorId, StringComparison.OrdinalIgnoreCase));

        if (connector is null)
        {
            return;
        }

        var isClaudeConnector = connector.Kind.Equals("claude", StringComparison.OrdinalIgnoreCase);
        if (!isClaudeConnector)
        {
            AppendUnsupportedSessionSetting(errors, "permissionMode", request.PermissionMode, connector.Kind);
            AppendUnsupportedSessionSetting(errors, "skipPermissions", request.SkipPermissions, connector.Kind);
            AppendUnsupportedSessionSetting(errors, "allowedTools", request.AllowedTools, connector.Kind);
            AppendUnsupportedSessionSetting(errors, "agent", request.Agent, connector.Kind);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Agent))
        {
            return;
        }

        var availableAgents = claudeHomeCatalog.LoadCatalog().Agents
            .Select(agent => agent.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!availableAgents.Contains(request.Agent.Trim()))
        {
            errors.TryAdd("agent", [$"Unknown Claude agent '{request.Agent.Trim()}'."]);
        }
    }

    private static void AppendUnsupportedSessionSetting(
        IDictionary<string, string[]> errors,
        string field,
        object? value,
        string connectorKind)
    {
        var isPresent = value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            System.Collections.IEnumerable sequence => sequence.Cast<object?>().Any(),
            _ => true
        };

        if (isPresent)
        {
            errors.TryAdd(field, [$"'{field}' is only supported for Claude connectors, not '{connectorKind}'."]);
        }
    }
}

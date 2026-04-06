using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Features.Config;
using ClankYankers.Server.Features.Sessions;
using ClankYankers.Server.Infrastructure.Registry;
using System.Collections.Concurrent;

namespace ClankYankers.Server.Features.Experiments;

public sealed class ExperimentOrchestrator(
    IConfigStore configStore,
    SessionOrchestrator sessionOrchestrator,
    BackplaneRegistry backplaneRegistry,
    ConnectorRegistry connectorRegistry,
    ILogger<ExperimentOrchestrator> logger)
{
    private const int MaxStoredRuns = 50;
    private readonly ConcurrentDictionary<string, StoredExperimentRun> _runs = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ExperimentRunSummary> ListRuns()
    {
        var activeSessionIds = sessionOrchestrator.ListSessions()
            .Select(session => session.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _runs.Values
            .OrderByDescending(run => run.CreatedAt)
            .Select(run => run.ToSummary(activeSessionIds))
            .ToArray();
    }

    public async Task<ExperimentRunSummary> RunAsync(string experimentId, CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        ConfigValidator.ThrowIfInvalid(
            config,
            backplaneRegistry.Kinds,
            connectorRegistry.Kinds,
            "Persisted config is invalid for experiment execution.");
        var experiment = config.Experiments.FirstOrDefault(candidate =>
                candidate.Enabled && candidate.Id.Equals(experimentId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown or disabled experiment '{experimentId}'.");

        var hosts = config.Hosts
            .Where(host => host.Enabled && experiment.HostIds.Contains(host.Id, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var connectors = config.Connectors
            .Where(connector => connector.Enabled && experiment.ConnectorIds.Contains(connector.Id, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var models = experiment.Models
            .Select(model => model.Trim())
            .Where(model => model.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string?>()
            .ToArray();

        if (hosts.Length == 0)
        {
            throw new InvalidOperationException($"Experiment '{experiment.DisplayName}' does not have any enabled hosts.");
        }

        if (connectors.Length == 0)
        {
            throw new InvalidOperationException($"Experiment '{experiment.DisplayName}' does not have any enabled connectors.");
        }

        if (models.Length == 0)
        {
            models = [null];
        }

        var variantCount = hosts.Length * connectors.Length * Math.Max(models.Length, 1);
        if (variantCount > ConfigValidator.MaxExperimentVariantCount)
        {
            throw new InvalidOperationException(
                $"Experiment '{experiment.DisplayName}' exceeds the maximum allowed variant count of {ConfigValidator.MaxExperimentVariantCount}.");
        }

        var createdAt = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid().ToString("n");
        var createdSessions = new List<SessionSummary>();
        var variants = new List<ExperimentRunVariant>();
        var attemptedVariant = string.Empty;

        try
        {
            foreach (var host in hosts)
            {
                foreach (var connector in connectors)
                {
                    foreach (var model in models)
                    {
                        attemptedVariant = FormatVariant(host.Id, connector.Id, model);
                        var createdSession = await sessionOrchestrator.CreateAsync(new CreateSessionRequest
                        {
                            ExperimentId = experiment.Id,
                            BackplaneId = host.BackplaneId,
                            HostId = host.Id,
                            ConnectorId = connector.Id,
                            Model = model,
                            Cols = experiment.Cols,
                            Rows = experiment.Rows
                        }, cancellationToken);

                        createdSessions.Add(createdSession);
                        variants.Add(new ExperimentRunVariant
                        {
                            SessionId = createdSession.Id,
                            BackplaneId = createdSession.BackplaneId,
                            HostId = createdSession.HostId,
                            ConnectorId = createdSession.ConnectorId,
                            Model = model
                        });
                    }
                }
            }
        }
        catch (Exception exception)
        {
            foreach (var session in createdSessions)
            {
                try
                {
                    await sessionOrchestrator.StopAsync(session.Id, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    logger.LogWarning(cleanupException, "Cleanup failed for experiment session {SessionId}.", session.Id);
                }
            }

            throw new InvalidOperationException(
                $"Experiment '{experiment.DisplayName}' failed while launching {attemptedVariant}.",
                exception);
        }

        var run = new StoredExperimentRun
        {
            Id = runId,
            ExperimentId = experiment.Id,
            ExperimentDisplayName = experiment.DisplayName,
            ExperimentDescription = experiment.Description,
            CreatedAt = createdAt,
            Variants = variants
        };

        _runs[runId] = run;
        TrimStoredRuns();

        var activeSessionIds = sessionOrchestrator.ListSessions()
            .Select(session => session.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return run.ToSummary(activeSessionIds);
    }

    private static string FormatVariant(string hostId, string connectorId, string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return $"{hostId}/{connectorId}";
        }

        return $"{hostId}/{connectorId} ({model})";
    }

    private void TrimStoredRuns()
    {
        if (_runs.Count <= MaxStoredRuns)
        {
            return;
        }

        foreach (var staleRun in _runs.Values
                     .OrderByDescending(run => run.CreatedAt)
                     .Skip(MaxStoredRuns)
                     .ToArray())
        {
            _runs.TryRemove(staleRun.Id, out _);
        }
    }

    private sealed record StoredExperimentRun
    {
        public required string Id { get; init; }

        public required string ExperimentId { get; init; }

        public required string ExperimentDisplayName { get; init; }

        public string? ExperimentDescription { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public required IReadOnlyList<ExperimentRunVariant> Variants { get; init; }

        public ExperimentRunSummary ToSummary(IReadOnlySet<string> activeSessionIds) =>
            new()
            {
                Id = Id,
                ExperimentId = ExperimentId,
                ExperimentDisplayName = ExperimentDisplayName,
                ExperimentDescription = ExperimentDescription,
                CreatedAt = CreatedAt,
                VariantCount = Variants.Count,
                ActiveSessionCount = Variants.Count(variant => activeSessionIds.Contains(variant.SessionId)),
                Variants = Variants
            };
    }
}

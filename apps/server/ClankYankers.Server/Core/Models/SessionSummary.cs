namespace ClankYankers.Server.Core.Models;

public sealed record SessionSummary
{
    public required string Id { get; init; }

    public string? ExperimentId { get; init; }

    public required string BackplaneId { get; init; }

    public required string HostId { get; init; }

    public required string ConnectorId { get; init; }

    public required string DisplayCommand { get; init; }

    public required SessionState State { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public int? ExitCode { get; init; }

    public string? Error { get; init; }
}

namespace ClankYankers.Server.Core.Models;

public sealed record ExperimentRunSummary
{
    public required string Id { get; init; }

    public required string ExperimentId { get; init; }

    public required string ExperimentDisplayName { get; init; }

    public string? ExperimentDescription { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required int ActiveSessionCount { get; init; }

    public required int VariantCount { get; init; }

    public required IReadOnlyList<ExperimentRunVariant> Variants { get; init; }
}

public sealed record ExperimentRunVariant
{
    public required string SessionId { get; init; }

    public required string BackplaneId { get; init; }

    public required string HostId { get; init; }

    public required string ConnectorId { get; init; }

    public string? Model { get; init; }
}

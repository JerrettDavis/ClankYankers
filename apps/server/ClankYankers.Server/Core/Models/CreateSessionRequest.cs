namespace ClankYankers.Server.Core.Models;

public sealed record CreateSessionRequest
{
    public string? ExperimentId { get; init; }

    public string? BackplaneId { get; init; }

    public string? HostId { get; init; }

    public string? ConnectorId { get; init; }

    public string? Model { get; init; }

    public string? PermissionMode { get; init; }

    public bool? SkipPermissions { get; init; }

    public IReadOnlyList<string>? AllowedTools { get; init; }

    public string? Agent { get; init; }

    public string? WorkingDirectory { get; init; }

    public int Cols { get; init; } = 120;

    public int Rows { get; init; } = 32;
}

namespace ClankYankers.Server.Core.Models;

public sealed record CreateSessionRequest
{
    public string? BackplaneId { get; init; }

    public string? HostId { get; init; }

    public string? ConnectorId { get; init; }

    public string? Model { get; init; }

    public int Cols { get; init; } = 120;

    public int Rows { get; init; } = 32;
}

namespace ClankYankers.Server.Core.Models;

public sealed record TerminalClientMessage
{
    public required string Type { get; init; }

    public string? Data { get; init; }

    public int? Cols { get; init; }

    public int? Rows { get; init; }
}

public sealed record TerminalServerMessage
{
    public required string Type { get; init; }

    public string? Data { get; init; }

    public string? State { get; init; }

    public int? ExitCode { get; init; }

    public string? Message { get; init; }
}

public sealed record TerminalOutputChunk(string Data, bool IsError = false);

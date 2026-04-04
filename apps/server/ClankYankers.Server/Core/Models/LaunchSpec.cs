namespace ClankYankers.Server.Core.Models;

public sealed record LaunchSpec
{
    public required string SessionId { get; init; }

    public required string DisplayCommand { get; init; }

    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string?> Environment { get; init; }
        = new Dictionary<string, string?>();

    public required int Cols { get; init; }

    public required int Rows { get; init; }
}

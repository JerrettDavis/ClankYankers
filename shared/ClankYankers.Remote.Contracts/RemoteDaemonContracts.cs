namespace ClankYankers.Remote.Contracts;

public sealed record StartRemoteSessionRequest
{
    public required string SessionId { get; init; }

    public required string DisplayCommand { get; init; }

    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();

    public required int Cols { get; init; }

    public required int Rows { get; init; }

    public string ExecutorKind { get; init; } = "process";

    public string? DockerEndpoint { get; init; }

    public string? DockerImage { get; init; }
}

public sealed record RemoteSessionStartedResponse(string SessionId, string StreamPath);

public sealed record RemoteSessionInputRequest(string Data);

public sealed record RemoteSessionResizeRequest(int Cols, int Rows);

public sealed record RemoteNodeInfoResponse(
    string Version,
    IReadOnlyList<string> ExecutorKinds,
    bool SupportsSelfUpdate,
    bool SupportsDocker);

public sealed record RemoteSelfUpdateRequest
{
    public string? Version { get; init; }

    public bool Restart { get; init; }

    public string PackageId { get; init; } = "ClankYankers.Daemon";
}

public sealed record RemoteSessionEnvelope
{
    public required string Type { get; init; }

    public string? Data { get; init; }

    public int? ExitCode { get; init; }

    public string? Error { get; init; }
}

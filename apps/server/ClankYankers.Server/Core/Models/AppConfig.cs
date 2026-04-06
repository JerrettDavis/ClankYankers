namespace ClankYankers.Server.Core.Models;

public sealed record AppConfig
{
    public required int Version { get; init; }

    public required IReadOnlyList<BackplaneDefinition> Backplanes { get; init; }

    public required IReadOnlyList<HostConfig> Hosts { get; init; }

    public required IReadOnlyList<ConnectorDefinition> Connectors { get; init; }

    public IReadOnlyList<ExperimentDefinition> Experiments { get; init; } = [];

    public static AppConfig CreateDefault() =>
        new()
        {
            Version = 1,
            Backplanes =
            [
                new BackplaneDefinition
                {
                    Id = "local",
                    DisplayName = "Local",
                    Kind = "local"
                },
                new BackplaneDefinition
                {
                    Id = "docker",
                    DisplayName = "Docker",
                    Kind = "docker"
                }
            ],
            Hosts =
            [
                new HostConfig
                {
                    Id = "local-host",
                    BackplaneId = "local",
                    DisplayName = "This machine",
                    ShellExecutable = "pwsh.exe",
                    ShellArguments = ["-NoLogo"],
                    WorkingDirectory = null
                },
                new HostConfig
                {
                    Id = "docker-local",
                    BackplaneId = "docker",
                    DisplayName = "Local Docker daemon",
                    ShellExecutable = "/bin/sh",
                    ShellArguments = [],
                    DockerEndpoint = "npipe://./pipe/docker_engine",
                    DockerImage = "alpine:3.20",
                    WorkingDirectory = "/workspace"
                }
            ],
            Connectors =
            [
                new ConnectorDefinition
                {
                    Id = "shell",
                    DisplayName = "Shell",
                    Kind = "shell"
                },
                new ConnectorDefinition
                {
                    Id = "ollama",
                    DisplayName = "Ollama qwen3.5:9b",
                    Kind = "ollama",
                    LaunchCommand = "ollama",
                    LaunchArguments = [],
                    DefaultModel = "qwen3.5:9b"
                },
                new ConnectorDefinition
                {
                    Id = "claude",
                    DisplayName = "Claude Code",
                    Kind = "claude",
                    LaunchCommand = "claude",
                    DefaultPermissionMode = "default"
                }
            ],
            Experiments =
            [
                new ExperimentDefinition
                {
                    Id = "local-shell-smoke",
                    DisplayName = "Local shell smoke",
                    Description = "Launch the local shell through the studio so operators can validate the end-to-end runtime path quickly.",
                    HostIds = ["local-host"],
                    ConnectorIds = ["shell"]
                },
                new ExperimentDefinition
                {
                    Id = "connector-sweep",
                    DisplayName = "Connector sweep",
                    Description = "Draft matrix for comparing agent CLIs on the same host once provider availability is confirmed.",
                    HostIds = ["local-host"],
                    ConnectorIds = ["shell", "claude", "ollama"],
                    Enabled = false
                }
            ]
        };
}

public sealed record BackplaneDefinition
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Kind { get; init; }

    public bool Enabled { get; init; } = true;
}

public sealed record HostConfig
{
    public required string Id { get; init; }

    public required string BackplaneId { get; init; }

    public required string DisplayName { get; init; }

    public required string ShellExecutable { get; init; }

    public IReadOnlyList<string> ShellArguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    public string? DockerEndpoint { get; init; }

    public string? DockerImage { get; init; }

    public bool Enabled { get; init; } = true;
}

public sealed record ConnectorDefinition
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Kind { get; init; }

    public string? LaunchCommand { get; init; }

    public IReadOnlyList<string> LaunchArguments { get; init; } = [];

    public string? DefaultModel { get; init; }

    public string? DefaultPermissionMode { get; init; }

    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    public bool SkipPermissions { get; init; }

    public bool Enabled { get; init; } = true;
}

public sealed record ExperimentDefinition
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> HostIds { get; init; } = [];

    public IReadOnlyList<string> ConnectorIds { get; init; } = [];

    public IReadOnlyList<string> Models { get; init; } = [];

    public int Cols { get; init; } = 120;

    public int Rows { get; init; } = 34;

    public bool Enabled { get; init; } = true;
}

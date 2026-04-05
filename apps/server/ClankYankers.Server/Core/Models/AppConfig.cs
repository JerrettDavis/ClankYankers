namespace ClankYankers.Server.Core.Models;

public sealed record AppConfig
{
    public required int Version { get; init; }

    public required IReadOnlyList<BackplaneDefinition> Backplanes { get; init; }

    public required IReadOnlyList<HostConfig> Hosts { get; init; }

    public required IReadOnlyList<ConnectorDefinition> Connectors { get; init; }

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

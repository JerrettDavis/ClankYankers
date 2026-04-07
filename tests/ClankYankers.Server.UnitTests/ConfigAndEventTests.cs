using ClankYankers.Server.Core.Events;
using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Features.Config;
using ClankYankers.Server.Features.Sessions;
using ClankYankers.Server.Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace ClankYankers.Server.UnitTests;

public sealed class ConfigAndEventTests
{
    [Fact]
    public void ConfigValidator_rejects_unknown_backplane_references()
    {
        var config = AppConfig.CreateDefault() with
        {
            Hosts =
            [
                new HostConfig
                {
                    Id = "broken-host",
                    BackplaneId = "missing-backplane",
                    DisplayName = "Broken",
                    ShellExecutable = "pwsh.exe"
                }
            ]
        };

        var errors = ConfigValidator.Validate(config);

        Assert.Contains("hosts.backplaneId", errors.Keys);
    }

    [Fact]
    public void ConfigValidator_rejects_unknown_connector_kinds()
    {
        var config = AppConfig.CreateDefault() with
        {
            Connectors =
            [
                new ConnectorDefinition
                {
                    Id = "mystery",
                    DisplayName = "Mystery",
                    Kind = "mystery"
                }
            ]
        };

        var errors = ConfigValidator.Validate(config);

        Assert.Contains("connectors.kind", errors.Keys);
    }

    [Fact]
    public void ConfigValidator_rejects_unknown_backplane_kinds()
    {
        var config = AppConfig.CreateDefault() with
        {
            Backplanes =
            [
                new BackplaneDefinition
                {
                    Id = "mystery",
                    DisplayName = "Mystery",
                    Kind = "mystery"
                }
            ]
        };

        var errors = ConfigValidator.Validate(config);

        Assert.Contains("backplanes.kind", errors.Keys);
    }

    [Fact]
    public void ConfigValidator_accepts_registry_provided_kinds()
    {
        var config = AppConfig.CreateDefault() with
        {
            Backplanes =
            [
                new BackplaneDefinition
                {
                    Id = "ssh",
                    DisplayName = "SSH",
                    Kind = "ssh"
                }
            ],
            Hosts =
            [
                new HostConfig
                {
                    Id = "ssh-host",
                    BackplaneId = "ssh",
                    DisplayName = "Remote shell",
                    ShellExecutable = "ssh"
                }
            ],
            Connectors =
            [
                new ConnectorDefinition
                {
                    Id = "gemini",
                    DisplayName = "Gemini",
                    Kind = "gemini",
                    LaunchCommand = "gemini"
                }
            ]
        };

        var errors = ConfigValidator.Validate(config, ["ssh"], ["gemini"]);

        Assert.DoesNotContain("backplanes.kind", errors.Keys);
        Assert.DoesNotContain("connectors.kind", errors.Keys);
    }

    [Fact]
    public void SessionRequestValidator_rejects_out_of_range_terminal_dimensions()
    {
        var errors = SessionRequestValidator.Validate(new CreateSessionRequest
        {
            Cols = 10,
            Rows = 500
        });

        Assert.Contains("cols", errors.Keys);
        Assert.Contains("rows", errors.Keys);
    }

    [Fact]
    public void SessionRequestValidator_rejects_relative_local_working_directories_after_resolution()
    {
        var errors = SessionRequestValidator.ValidateResolved(
            new BackplaneDefinition
            {
                Id = "local",
                DisplayName = "Local",
                Kind = "local"
            },
            new LaunchSpec
            {
                SessionId = "session-1",
                DisplayCommand = "pwsh.exe",
                FileName = "pwsh.exe",
                WorkingDirectory = "relative-path",
                Cols = 120,
                Rows = 34
            });

        Assert.Equal(["Local workspace folder must be an absolute path."], errors["workingDirectory"]);
    }

    [Fact]
    public void SessionRequestValidator_rejects_missing_local_working_directories_after_resolution()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), $"clanky-missing-{Guid.NewGuid():N}");
        var errors = SessionRequestValidator.ValidateResolved(
            new BackplaneDefinition
            {
                Id = "local",
                DisplayName = "Local",
                Kind = "local"
            },
            new LaunchSpec
            {
                SessionId = "session-1",
                DisplayCommand = "pwsh.exe",
                FileName = "pwsh.exe",
                WorkingDirectory = missingDirectory,
                Cols = 120,
                Rows = 34
            });

        Assert.Equal([$"Local workspace folder '{missingDirectory}' does not exist."], errors["workingDirectory"]);
    }

    [Fact]
    public void SessionRequestValidator_rejects_relative_docker_working_directories_after_resolution()
    {
        var errors = SessionRequestValidator.ValidateResolved(
            new BackplaneDefinition
            {
                Id = "docker",
                DisplayName = "Docker",
                Kind = "docker"
            },
            new LaunchSpec
            {
                SessionId = "session-1",
                DisplayCommand = "/bin/sh",
                FileName = "/bin/sh",
                WorkingDirectory = "workspace",
                Cols = 120,
                Rows = 34
            });

        Assert.Equal(["Docker workspace folder must be an absolute path such as /workspace."], errors["workingDirectory"]);
    }

    [Fact]
    public void SessionRequestValidator_rejects_windows_style_docker_working_directories_after_resolution()
    {
        var errors = SessionRequestValidator.ValidateResolved(
            new BackplaneDefinition
            {
                Id = "docker",
                DisplayName = "Docker",
                Kind = "docker"
            },
            new LaunchSpec
            {
                SessionId = "session-1",
                DisplayCommand = "/bin/sh",
                FileName = "/bin/sh",
                WorkingDirectory = @"C:\workspace",
                Cols = 120,
                Rows = 34
            });

        Assert.Equal(["Docker workspace folder must be an absolute path such as /workspace."], errors["workingDirectory"]);
    }

    [Fact]
    public void ConfigValidator_requires_an_enabled_launch_path()
    {
        var defaults = AppConfig.CreateDefault();
        var config = defaults with
        {
            Backplanes = defaults.Backplanes.Select(backplane => backplane with { Enabled = false }).ToArray(),
            Hosts = defaults.Hosts.Select(host => host with { Enabled = false }).ToArray(),
            Connectors = defaults.Connectors.Select(connector => connector with { Enabled = false }).ToArray()
        };

        var errors = ConfigValidator.Validate(config);

        Assert.Contains("backplanes.enabled", errors.Keys);
        Assert.Contains("hosts.enabled", errors.Keys);
        Assert.Contains("connectors.enabled", errors.Keys);
    }

    [Fact]
    public void ConfigValidator_rejects_runtime_invalid_enabled_resources()
    {
        var config = AppConfig.CreateDefault() with
        {
            Backplanes =
            [
                new BackplaneDefinition
                {
                    Id = "local",
                    DisplayName = "",
                    Kind = "local",
                    Enabled = true
                },
                new BackplaneDefinition
                {
                    Id = "docker",
                    DisplayName = "Docker",
                    Kind = "docker",
                    Enabled = true
                }
            ],
            Hosts =
            [
                new HostConfig
                {
                    Id = "",
                    BackplaneId = "local",
                    DisplayName = "",
                    ShellExecutable = "",
                    Enabled = true
                },
                new HostConfig
                {
                    Id = "docker-host",
                    BackplaneId = "docker",
                    DisplayName = "Docker host",
                    ShellExecutable = "/bin/sh",
                    DockerEndpoint = "",
                    DockerImage = "",
                    Enabled = true
                }
            ],
            Connectors =
            [
                new ConnectorDefinition
                {
                    Id = "",
                    DisplayName = "",
                    Kind = "claude",
                    LaunchArguments = ["--model=sonnet-4.6", "--agent", "frontend-developer"],
                    Enabled = true
                }
            ]
        };

        var errors = ConfigValidator.Validate(config);

        Assert.Contains("backplanes.displayName", errors.Keys);
        Assert.Contains("hosts.id.required", errors.Keys);
        Assert.Contains("hosts.displayName", errors.Keys);
        Assert.Contains("hosts.shellExecutable", errors.Keys);
        Assert.Contains("hosts.dockerEndpoint", errors.Keys);
        Assert.Contains("hosts.dockerImage", errors.Keys);
        Assert.Contains("connectors.id.required", errors.Keys);
        Assert.Contains("connectors.displayName", errors.Keys);
        Assert.Contains("connectors.launchArguments", errors.Keys);
    }

    [Fact]
    public void ConfigValidator_rejects_invalid_experiment_references()
    {
        var config = AppConfig.CreateDefault() with
        {
            Experiments =
            [
                new ExperimentDefinition
                {
                    Id = "broken",
                    DisplayName = "Broken",
                    HostIds = ["missing-host"],
                    ConnectorIds = ["shell", "missing-connector"],
                    Cols = 10,
                    Rows = 200
                }
            ]
        };

        var errors = ConfigValidator.Validate(config);

        Assert.Contains("experiments.hostIds.unknown", errors.Keys);
        Assert.Contains("experiments.connectorIds.unknown", errors.Keys);
        Assert.Contains("experiments.dimensions", errors.Keys);
    }

    [Fact]
    public void ConfigValidator_rejects_oversized_experiment_matrices()
    {
        var config = AppConfig.CreateDefault() with
        {
            Experiments =
            [
                new ExperimentDefinition
                {
                    Id = "too-big",
                    DisplayName = "Too big",
                    HostIds = Enumerable.Range(1, 5).Select(index => $"host-{index}").ToArray(),
                    ConnectorIds = Enumerable.Range(1, 5).Select(index => $"connector-{index}").ToArray(),
                    Models = ["a", "b"]
                }
            ],
            Hosts = Enumerable.Range(1, 5).Select(index => new HostConfig
            {
                Id = $"host-{index}",
                BackplaneId = "local",
                DisplayName = $"Host {index}",
                ShellExecutable = "pwsh.exe"
            }).ToArray(),
            Connectors = Enumerable.Range(1, 5).Select(index => new ConnectorDefinition
            {
                Id = $"connector-{index}",
                DisplayName = $"Connector {index}",
                Kind = "shell"
            }).ToArray()
        };

        var errors = ConfigValidator.Validate(config);

        Assert.Contains("experiments.variantCount", errors.Keys);
    }

    [Fact]
    public async Task InMemoryEventBus_delivers_events_to_subscribers()
    {
        var eventBus = new InMemoryEventBus(LoggerFactory.Create(_ => { }).CreateLogger<InMemoryEventBus>());
        RuntimeErrorEvent? captured = null;

        eventBus.Subscribe<RuntimeErrorEvent>((eventData, _) =>
        {
            captured = eventData;
            return Task.CompletedTask;
        });

        var expected = new RuntimeErrorEvent("session-1", "boom", DateTimeOffset.UtcNow);
        await eventBus.PublishAsync(expected);

        Assert.Equal(expected, captured);
    }

    [Fact]
    public async Task InMemoryEventBus_isolates_subscriber_failures()
    {
        var eventBus = new InMemoryEventBus(LoggerFactory.Create(_ => { }).CreateLogger<InMemoryEventBus>());
        RuntimeErrorEvent? captured = null;

        eventBus.Subscribe<RuntimeErrorEvent>((_, _) => throw new InvalidOperationException("boom"));
        eventBus.Subscribe<RuntimeErrorEvent>((eventData, _) =>
        {
            captured = eventData;
            return Task.CompletedTask;
        });

        var expected = new RuntimeErrorEvent("session-1", "boom", DateTimeOffset.UtcNow);
        await eventBus.PublishAsync(expected);

        Assert.Equal(expected, captured);
    }
}

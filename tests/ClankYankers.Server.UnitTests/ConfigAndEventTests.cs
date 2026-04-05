using ClankYankers.Server.Core.Events;
using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Features.Config;
using ClankYankers.Server.Features.Sessions;
using ClankYankers.Server.Infrastructure.Observability;

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
                    LaunchArguments = ["--model", "sonnet-4.6"],
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
    public async Task InMemoryEventBus_delivers_events_to_subscribers()
    {
        var eventBus = new InMemoryEventBus();
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
}

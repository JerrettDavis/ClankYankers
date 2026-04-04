using ClankYankers.Server.Core.Events;
using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Features.Config;
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

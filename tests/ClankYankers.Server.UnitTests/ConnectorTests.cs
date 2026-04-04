using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Infrastructure.Connectors;

namespace ClankYankers.Server.UnitTests;

public sealed class ConnectorTests
{
    [Fact]
    public void ShellConnector_uses_host_shell_configuration()
    {
        var connector = new ShellConnector();
        var host = new HostConfig
        {
            Id = "local-host",
            BackplaneId = "local",
            DisplayName = "Local",
            ShellExecutable = "pwsh.exe",
            ShellArguments = ["-NoLogo"],
            WorkingDirectory = "C:\\git\\ClankYankers"
        };

        var launchSpec = connector.BuildLaunchSpec(
            "session-1",
            new CreateSessionRequest { Cols = 120, Rows = 40 },
            host,
            new ConnectorDefinition
            {
                Id = "shell",
                DisplayName = "Shell",
                Kind = "shell"
            });

        Assert.Equal("pwsh.exe", launchSpec.FileName);
        Assert.Equal(["-NoLogo"], launchSpec.Arguments);
        Assert.Equal("pwsh.exe -NoLogo", launchSpec.DisplayCommand);
        Assert.Equal("C:\\git\\ClankYankers", launchSpec.WorkingDirectory);
    }

    [Fact]
    public void OllamaConnector_builds_default_model_command()
    {
        var connector = new OllamaConnector();
        var launchSpec = connector.BuildLaunchSpec(
            "session-ollama",
            new CreateSessionRequest { Cols = 100, Rows = 28 },
            new HostConfig
            {
                Id = "local-host",
                BackplaneId = "local",
                DisplayName = "Local",
                ShellExecutable = "pwsh.exe"
            },
            new ConnectorDefinition
            {
                Id = "ollama",
                DisplayName = "Ollama",
                Kind = "ollama",
                DefaultModel = "qwen3.5:9b"
            });

        Assert.Equal("ollama", launchSpec.FileName);
        Assert.Equal(["run", "qwen3.5:9b"], launchSpec.Arguments);
        Assert.Equal("ollama run qwen3.5:9b", launchSpec.DisplayCommand);
    }
}

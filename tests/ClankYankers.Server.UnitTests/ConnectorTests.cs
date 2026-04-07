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
                LaunchCommand = "ollama",
                LaunchArguments = [],
                DefaultModel = "qwen3.5:9b"
            });

        Assert.Equal("ollama", launchSpec.FileName);
        Assert.Equal(["run", "qwen3.5:9b"], launchSpec.Arguments);
        Assert.Equal("ollama run qwen3.5:9b", launchSpec.DisplayCommand);
    }

    [Fact]
    public void OllamaConnector_preserves_required_run_subcommand_when_extra_arguments_are_configured()
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
                LaunchCommand = "ollama",
                LaunchArguments = ["--verbose"],
                DefaultModel = "qwen3.5:9b"
            });

        Assert.Equal(["run", "--verbose", "qwen3.5:9b"], launchSpec.Arguments);
        Assert.Equal("ollama run --verbose qwen3.5:9b", launchSpec.DisplayCommand);
    }

    [Fact]
    public void ClaudeConnector_builds_launch_spec_from_connector_policy_and_model_override()
    {
        var connector = new ClaudeConnector();

        var launchSpec = connector.BuildLaunchSpec(
            "session-claude",
            new CreateSessionRequest
            {
                Cols = 120,
                Rows = 40,
                Model = "sonnet-4.6"
            },
            new HostConfig
            {
                Id = "local-host",
                BackplaneId = "local",
                DisplayName = "Local",
                ShellExecutable = "pwsh.exe",
                WorkingDirectory = "C:\\git\\ClankYankers"
            },
            new ConnectorDefinition
            {
                Id = "claude",
                DisplayName = "Claude Code",
                Kind = "claude",
                LaunchCommand = "claude",
                LaunchArguments = ["--verbose"],
                DefaultPermissionMode = "plan",
                AllowedTools = ["Read", "Bash(ls *)"],
                SkipPermissions = true
            });

        Assert.Equal("claude", launchSpec.FileName);
        Assert.Equal(
            ["--verbose", "--model", "sonnet-4.6", "--dangerously-skip-permissions", "--allowedTools", "Read,Bash(ls *)"],
            launchSpec.Arguments);
        Assert.Equal(
            "claude --verbose --model sonnet-4.6 --dangerously-skip-permissions --allowedTools Read,Bash(ls *)",
            launchSpec.DisplayCommand);
        Assert.Equal("C:\\git\\ClankYankers", launchSpec.WorkingDirectory);
    }

    [Fact]
    public void ClaudeConnector_strips_reserved_arguments_from_base_configuration()
    {
        var connector = new ClaudeConnector();

        var launchSpec = connector.BuildLaunchSpec(
            "session-claude",
            new CreateSessionRequest
            {
                Cols = 120,
                Rows = 40,
                Model = "sonnet-4.6"
            },
            new HostConfig
            {
                Id = "local-host",
                BackplaneId = "local",
                DisplayName = "Local",
                ShellExecutable = "pwsh.exe"
            },
            new ConnectorDefinition
            {
                Id = "claude",
                DisplayName = "Claude Code",
                Kind = "claude",
                LaunchCommand = "claude",
                LaunchArguments = ["--verbose", "--model=haiku", "--dangerously-skip-permissions", "--permission-mode", "acceptEdits", "--allowedTools=Read", "--agent=frontend-developer"],
                DefaultPermissionMode = "plan"
            });

        Assert.Equal(["--verbose", "--model", "sonnet-4.6", "--permission-mode", "plan"], launchSpec.Arguments);
        Assert.Equal("claude --verbose --model sonnet-4.6 --permission-mode plan", launchSpec.DisplayCommand);
    }

    [Fact]
    public void ClaudeConnector_applies_session_level_permission_tool_and_agent_overrides()
    {
        var connector = new ClaudeConnector();

        var launchSpec = connector.BuildLaunchSpec(
            "session-claude",
            new CreateSessionRequest
            {
                Cols = 120,
                Rows = 40,
                Model = "opus-4.6",
                PermissionMode = "acceptEdits",
                SkipPermissions = false,
                AllowedTools = ["Read", "Bash(git status)", "Read"],
                Agent = "frontend-developer"
            },
            new HostConfig
            {
                Id = "local-host",
                BackplaneId = "local",
                DisplayName = "Local",
                ShellExecutable = "pwsh.exe"
            },
            new ConnectorDefinition
            {
                Id = "claude",
                DisplayName = "Claude Code",
                Kind = "claude",
                LaunchCommand = "claude",
                LaunchArguments = ["--verbose"],
                DefaultPermissionMode = "plan",
                AllowedTools = ["Edit"],
                SkipPermissions = true
            });

        Assert.Equal(
            ["--verbose", "--model", "opus-4.6", "--agent", "frontend-developer", "--permission-mode", "acceptEdits", "--allowedTools", "Read,Bash(git status)"],
            launchSpec.Arguments);
        Assert.Equal(
            "claude --verbose --model opus-4.6 --agent frontend-developer --permission-mode acceptEdits --allowedTools Read,Bash(git status)",
            launchSpec.DisplayCommand);
    }
}

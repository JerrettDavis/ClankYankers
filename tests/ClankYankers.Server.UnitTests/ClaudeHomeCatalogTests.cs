using ClankYankers.Server.Infrastructure.ClaudeHome;

namespace ClankYankers.Server.UnitTests;

public sealed class ClaudeHomeCatalogTests
{
    [Fact]
    public void Load_returns_empty_summary_when_claude_home_is_missing()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"clankyankers-claude-home-{Guid.NewGuid():N}");
        var catalog = new ClaudeHomeCatalog(rootPath);

        var summary = catalog.Load();

        Assert.False(summary.Exists);
        Assert.Equal("~/.claude", summary.RootDisplayPath);
        Assert.Equal(0, summary.AgentCount);
        Assert.Equal(0, summary.SkillCount);
        Assert.Equal(0, summary.CommandCount);
        Assert.Equal(0, summary.McpArtifactCount);
        Assert.Null(summary.Settings);
    }

    [Fact]
    public void Load_discovers_agents_skills_commands_and_sanitized_settings()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"clankyankers-claude-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);

        try
        {
            Directory.CreateDirectory(Path.Combine(rootPath, "agents"));
            Directory.CreateDirectory(Path.Combine(rootPath, "skills", "brainstorming", "commands"));
            Directory.CreateDirectory(Path.Combine(rootPath, "commands"));

            File.WriteAllText(
                Path.Combine(rootPath, "agents", "frontend-developer.md"),
                """
                ---
                name: Frontend Developer
                description: Expert frontend developer
                color: cyan
                emoji: 🖥️
                vibe: Builds responsive interfaces.
                ---

                # Frontend Developer
                """);

            File.WriteAllText(
                Path.Combine(rootPath, "skills", "brainstorming", "SKILL.md"),
                """
                ---
                name: brainstorming
                description: Explore intent before implementing
                ---

                # Brainstorming
                """);

            File.WriteAllText(
                Path.Combine(rootPath, "skills", "brainstorming", "commands", "brainstorm.md"),
                "# Brainstorm");

            File.WriteAllText(
                Path.Combine(rootPath, "commands", "daily.md"),
                "# Daily");

            File.WriteAllText(
                Path.Combine(rootPath, "mcp-needs-auth-cache.json"),
                "{}");

            File.WriteAllText(
                Path.Combine(rootPath, "settings.json"),
                """
                {
                  "statusLine": {
                    "type": "command",
                    "command": "python statusline.py"
                  },
                  "enabledPlugins": {
                    "vercel": true,
                    "figma": false
                  },
                  "voiceEnabled": true,
                  "skipDangerousModePermissionPrompt": true,
                  "env": {
                    "OPENAI_API_KEY": "secret"
                  }
                }
                """);

            File.WriteAllText(
                Path.Combine(rootPath, "settings.local.json"),
                """
                {
                  "enabledPlugins": {
                    "figma": true
                  },
                  "voiceEnabled": false
                }
                """);

            var catalog = new ClaudeHomeCatalog(rootPath);

            var summary = catalog.Load();
            var detail = catalog.LoadCatalog();

            Assert.True(summary.Exists);
            Assert.Equal(1, summary.AgentCount);
            Assert.Equal(1, summary.SkillCount);
            Assert.Equal(1, summary.CommandCount);
            Assert.Equal(1, summary.McpArtifactCount);

            var agent = Assert.Single(detail.Agents);
            Assert.Equal("frontend-developer", agent.Name);
            Assert.Equal(0, agent.CommandCount);

            var skill = Assert.Single(detail.Skills);
            Assert.Equal("brainstorming", skill.Name);
            Assert.Equal(1, skill.CommandCount);

            Assert.NotNull(summary.Settings);
            Assert.True(summary.Settings!.HasLocalOverrides);
            Assert.Equal("command", summary.Settings!.StatusLineType);
            Assert.True(summary.Settings.HasStatusLineCommand);
            Assert.False(summary.Settings.VoiceEnabled);
            Assert.True(summary.Settings.SkipDangerousModePermissionPrompt);
            Assert.Equal(2, summary.Settings.EnabledPluginCount);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_tolerates_invalid_settings_json_and_returns_partial_summary()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"clankyankers-claude-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(rootPath, "agents"));

        try
        {
            File.WriteAllText(Path.Combine(rootPath, "agents", "ops.md"), "# Ops");
            File.WriteAllText(Path.Combine(rootPath, "settings.json"), "{ invalid");

            var catalog = new ClaudeHomeCatalog(rootPath);

            var summary = catalog.Load();
            var detail = catalog.LoadCatalog();

            Assert.True(summary.Exists);
            Assert.Equal(1, summary.AgentCount);
            Assert.Null(summary.Settings);
            Assert.Single(detail.Agents);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}

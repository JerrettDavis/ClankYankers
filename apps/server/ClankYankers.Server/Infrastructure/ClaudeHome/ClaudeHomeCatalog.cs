using System.Text.Json;
using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Infrastructure.ClaudeHome;

public sealed class ClaudeHomeCatalog(string? rootPath = null)
{
    private readonly string _rootPath = rootPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude");

    public ClaudeHomeSummary Load()
    {
        if (!Directory.Exists(_rootPath))
        {
            return CreateEmpty();
        }

        var catalog = LoadCatalog();
        var commandCount = SafeLoad(LoadCommandCount, 0);
        var mcpArtifactCount = SafeLoad(LoadMcpArtifactCount, 0);
        var settings = SafeLoad(LoadSettings, fallback: null);

        return new ClaudeHomeSummary
        {
            RootDisplayPath = "~/.claude",
            Exists = true,
            AgentCount = catalog.Agents.Count,
            SkillCount = catalog.Skills.Count,
            CommandCount = commandCount,
            McpArtifactCount = mcpArtifactCount,
            Settings = settings
        };
    }

    public ClaudeHomeCatalogResponse LoadCatalog()
    {
        if (!Directory.Exists(_rootPath))
        {
            return new ClaudeHomeCatalogResponse();
        }

        return new ClaudeHomeCatalogResponse
        {
            Agents = SafeLoad(LoadAgents, Array.Empty<ClaudeCatalogEntry>()),
            Skills = SafeLoad(LoadSkills, Array.Empty<ClaudeCatalogEntry>())
        };
    }

    private ClaudeHomeSummary CreateEmpty() =>
        new()
        {
            RootDisplayPath = "~/.claude",
            Exists = false
        };

    private IReadOnlyList<ClaudeCatalogEntry> LoadAgents()
    {
        var agentsPath = Path.Combine(_rootPath, "agents");
        if (!Directory.Exists(agentsPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(agentsPath, "*.md", SearchOption.TopDirectoryOnly)
            .Select(path => new ClaudeCatalogEntry
            {
                Name = Path.GetFileNameWithoutExtension(path),
                CommandCount = 0
            })
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<ClaudeCatalogEntry> LoadSkills()
    {
        var skillsPath = ResolveSkillsPath();
        if (!Directory.Exists(skillsPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(skillsPath, "SKILL.md", SearchOption.AllDirectories)
            .Select(path => new ClaudeCatalogEntry
            {
                Name = Path.GetFileName(Path.GetDirectoryName(path)) ?? "skill",
                CommandCount = CountCommands(Path.GetDirectoryName(path))
            })
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private int LoadCommandCount()
    {
        var commandsPath = Path.Combine(_rootPath, "commands");
        if (!Directory.Exists(commandsPath))
        {
            return 0;
        }

        return Directory.EnumerateFiles(commandsPath, "*.md", SearchOption.AllDirectories).Count();
    }

    private int LoadMcpArtifactCount() =>
        Directory.EnumerateFileSystemEntries(_rootPath, "*mcp*", SearchOption.TopDirectoryOnly).Count();

    private ClaudeSettingsSummary? LoadSettings()
    {
        var settingsPath = Path.Combine(_rootPath, "settings.json");
        var localSettingsPath = Path.Combine(_rootPath, "settings.local.json");
        var hasBaseSettings = File.Exists(settingsPath);
        var hasLocalSettings = File.Exists(localSettingsPath);

        if (!hasBaseSettings && !hasLocalSettings)
        {
            return null;
        }

        var safeSettings = new SafeSettingsSnapshot();
        if (hasBaseSettings)
        {
            safeSettings = MergeSettings(safeSettings, ReadSettingsSnapshot(settingsPath));
        }

        if (hasLocalSettings)
        {
            safeSettings = MergeSettings(safeSettings, ReadSettingsSnapshot(localSettingsPath));
        }

        var enabledPluginNames = safeSettings.EnabledPlugins
            .Where(pair => pair.Value)
            .Select(pair => pair.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ClaudeSettingsSummary
        {
            HasLocalOverrides = hasLocalSettings,
            StatusLineType = safeSettings.StatusLineType,
            HasStatusLineCommand = !string.IsNullOrWhiteSpace(safeSettings.StatusLineCommand),
            VoiceEnabled = safeSettings.VoiceEnabled,
            SkipDangerousModePermissionPrompt = safeSettings.SkipDangerousModePermissionPrompt,
            EnabledPluginCount = enabledPluginNames.Length
        };
    }

    private static SafeSettingsSnapshot ReadSettingsSnapshot(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var enabledPlugins = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("enabledPlugins", out var enabledPluginsElement) &&
            enabledPluginsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in enabledPluginsElement.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    enabledPlugins[property.Name] = property.Value.GetBoolean();
                }
            }
        }

        string? statusLineType = null;
        string? statusLineCommand = null;
        if (root.TryGetProperty("statusLine", out var statusLineElement) &&
            statusLineElement.ValueKind == JsonValueKind.Object)
        {
            statusLineType = statusLineElement.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;
            statusLineCommand = statusLineElement.TryGetProperty("command", out var commandElement) && commandElement.ValueKind == JsonValueKind.String
                ? commandElement.GetString()
                : null;
        }

        return new SafeSettingsSnapshot
        {
            StatusLineType = statusLineType,
            StatusLineCommand = statusLineCommand,
            VoiceEnabled = TryGetBoolean(root, "voiceEnabled"),
            SkipDangerousModePermissionPrompt = TryGetBoolean(root, "skipDangerousModePermissionPrompt"),
            EnabledPlugins = enabledPlugins
        };
    }

    private static SafeSettingsSnapshot MergeSettings(SafeSettingsSnapshot current, SafeSettingsSnapshot next)
    {
        var enabledPlugins = new Dictionary<string, bool>(current.EnabledPlugins, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in next.EnabledPlugins)
        {
            enabledPlugins[pair.Key] = pair.Value;
        }

        return new SafeSettingsSnapshot
        {
            StatusLineType = next.StatusLineType ?? current.StatusLineType,
            StatusLineCommand = next.StatusLineCommand ?? current.StatusLineCommand,
            VoiceEnabled = next.VoiceEnabled ?? current.VoiceEnabled,
            SkipDangerousModePermissionPrompt = next.SkipDangerousModePermissionPrompt ?? current.SkipDangerousModePermissionPrompt,
            EnabledPlugins = enabledPlugins
        };
    }

    private string ResolveSkillsPath()
    {
        var lowerCasePath = Path.Combine(_rootPath, "skills");
        if (Directory.Exists(lowerCasePath))
        {
            return lowerCasePath;
        }

        return Path.Combine(_rootPath, "Skills");
    }

    private int CountCommands(string? commandRootPath)
    {
        if (string.IsNullOrWhiteSpace(commandRootPath) || !Directory.Exists(commandRootPath))
        {
            return 0;
        }

        return Directory
            .EnumerateFiles(commandRootPath, "*.md", SearchOption.AllDirectories)
            .Count(path => path.Contains(
                $"{Path.DirectorySeparatorChar}commands{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool? TryGetBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static T SafeLoad<T>(Func<T> loader, T fallback)
    {
        try
        {
            return loader();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return fallback;
        }
    }

    private sealed record SafeSettingsSnapshot
    {
        public string? StatusLineType { get; init; }

        public string? StatusLineCommand { get; init; }

        public bool? VoiceEnabled { get; init; }

        public bool? SkipDangerousModePermissionPrompt { get; init; }

        public IReadOnlyDictionary<string, bool> EnabledPlugins { get; init; } =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }
}

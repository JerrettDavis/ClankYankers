namespace ClankYankers.Server.Core.Models;

public sealed record ClaudeHomeSummary
{
    public required string RootDisplayPath { get; init; }

    public bool Exists { get; init; }

    public int AgentCount { get; init; }

    public int SkillCount { get; init; }

    public int CommandCount { get; init; }

    public int McpArtifactCount { get; init; }

    public ClaudeSettingsSummary? Settings { get; init; }
}

public sealed record ClaudeHomeCatalogResponse
{
    public IReadOnlyList<ClaudeCatalogEntry> Agents { get; init; } = [];

    public IReadOnlyList<ClaudeCatalogEntry> Skills { get; init; } = [];
}

public sealed record ClaudeCatalogEntry
{
    public required string Name { get; init; }

    public int CommandCount { get; init; }
}

public sealed record ClaudeSettingsSummary
{
    public bool HasLocalOverrides { get; init; }

    public string? StatusLineType { get; init; }

    public bool HasStatusLineCommand { get; init; }

    public bool? VoiceEnabled { get; init; }

    public bool? SkipDangerousModePermissionPrompt { get; init; }

    public int EnabledPluginCount { get; init; }
}

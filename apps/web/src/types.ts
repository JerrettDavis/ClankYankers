export interface BackplaneDefinition {
  id: string
  displayName: string
  kind: string
  enabled: boolean
}

export interface HostConfig {
  id: string
  backplaneId: string
  displayName: string
  shellExecutable: string
  shellArguments: string[]
  workingDirectory: string | null
  dockerEndpoint: string | null
  dockerImage: string | null
  enabled: boolean
}

export interface ConnectorDefinition {
  id: string
  displayName: string
  kind: string
  launchCommand: string | null
  launchArguments: string[]
  defaultModel: string | null
  defaultPermissionMode: string | null
  allowedTools: string[]
  skipPermissions: boolean
  enabled: boolean
}

export interface AppConfig {
  version: number
  backplanes: BackplaneDefinition[]
  hosts: HostConfig[]
  connectors: ConnectorDefinition[]
  experiments: ExperimentDefinition[]
}

export interface ExperimentDefinition {
  id: string
  displayName: string
  description: string | null
  hostIds: string[]
  connectorIds: string[]
  models: string[]
  cols: number
  rows: number
  enabled: boolean
}

export interface ExperimentRunVariant {
  sessionId: string
  backplaneId: string
  hostId: string
  connectorId: string
  model: string | null
}

export interface ExperimentRunSummary {
  id: string
  experimentId: string
  experimentDisplayName: string
  experimentDescription: string | null
  createdAt: string
  activeSessionCount: number
  variantCount: number
  variants: ExperimentRunVariant[]
}

export type SessionState = 'Starting' | 'Running' | 'Stopped' | 'Failed'

export interface SessionSummary {
  id: string
  experimentId: string | null
  backplaneId: string
  hostId: string
  connectorId: string
  displayCommand: string
  state: SessionState
  createdAt: string
  startedAt: string | null
  endedAt: string | null
  exitCode: number | null
  error: string | null
}

export interface AppStateResponse {
  config: AppConfig
  sessions: SessionSummary[]
  experimentRuns: ExperimentRunSummary[]
  claudeHome: ClaudeHomeSummary | null
}

export interface ClaudeCatalogEntry {
  name: string
  commandCount: number
}

export interface ClaudeSettingsSummary {
  hasLocalOverrides: boolean
  statusLineType: string | null
  hasStatusLineCommand: boolean
  voiceEnabled: boolean | null
  skipDangerousModePermissionPrompt: boolean | null
  enabledPluginCount: number
}

export interface ClaudeHomeSummary {
  rootDisplayPath: string
  exists: boolean
  agentCount: number
  skillCount: number
  commandCount: number
  mcpArtifactCount: number
  settings: ClaudeSettingsSummary | null
}

export interface ClaudeHomeCatalogResponse {
  agents: ClaudeCatalogEntry[]
  skills: ClaudeCatalogEntry[]
}

export interface CreateSessionRequest {
  backplaneId: string
  hostId: string
  connectorId: string
  model: string | null
  permissionMode: string | null
  skipPermissions: boolean | null
  allowedTools: string[] | null
  agent: string | null
  workingDirectory: string | null
  cols: number
  rows: number
}

export interface TerminalClientMessage {
  type: 'input' | 'resize'
  data?: string
  cols?: number
  rows?: number
}

export interface TerminalServerMessage {
  type: 'output' | 'state' | 'exit' | 'error'
  data?: string | null
  state?: string | null
  exitCode?: number | null
  message?: string | null
}

export interface ApiValidationProblem {
  title?: string
  errors?: Record<string, string[]>
}

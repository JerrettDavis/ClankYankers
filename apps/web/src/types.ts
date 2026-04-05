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
}

export type SessionState = 'Starting' | 'Running' | 'Stopped' | 'Failed'

export interface SessionSummary {
  id: string
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
}

export interface CreateSessionRequest {
  backplaneId: string
  hostId: string
  connectorId: string
  model: string | null
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

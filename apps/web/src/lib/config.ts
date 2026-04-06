import type {
  AppConfig,
  BackplaneDefinition,
  ConnectorDefinition,
  ExperimentDefinition,
  HostConfig,
  SessionState,
  SessionSummary,
} from '../types'

export interface LaunchDraft {
  backplaneId: string
  hostId: string
  connectorId: string
  model: string | null
  cols: number
  rows: number
}

const sessionStates: SessionState[] = ['Starting', 'Running', 'Stopped', 'Failed']

export function normalizeSessionState(value: number | string | null | undefined): SessionState {
  if (typeof value === 'string' && sessionStates.includes(value as SessionState)) {
    return value as SessionState
  }

  if (typeof value === 'number' && value >= 0 && value < sessionStates.length) {
    return sessionStates[value]
  }

  return 'Running'
}

export function normalizeSession(summary: Omit<SessionSummary, 'state'> & { state: number | string }): SessionSummary {
  return {
    ...summary,
    state: normalizeSessionState(summary.state),
  }
}

export function getEnabledBackplanes(config: AppConfig): BackplaneDefinition[] {
  return config.backplanes.filter((backplane) => backplane.enabled)
}

export function getEnabledHosts(config: AppConfig, backplaneId: string): HostConfig[] {
  return config.hosts.filter((host) => host.enabled && sameId(host.backplaneId, backplaneId))
}

export function getEnabledConnectors(config: AppConfig): ConnectorDefinition[] {
  return config.connectors.filter((connector) => connector.enabled)
}

export function getConnector(config: AppConfig, connectorId: string): ConnectorDefinition | undefined {
  return config.connectors.find((connector) => sameId(connector.id, connectorId))
}

export function coerceLaunchDraft(config: AppConfig, current?: Partial<LaunchDraft>): LaunchDraft {
  const backplane = pickValue(
    getEnabledBackplanes(config).map((item) => item.id),
    current?.backplaneId,
  )

  const host = pickValue(
    getEnabledHosts(config, backplane).map((item) => item.id),
    current?.hostId,
  )

  const connector = pickValue(
    getEnabledConnectors(config).map((item) => item.id),
    current?.connectorId,
  )
  const connectorDefinition = getConnector(config, connector)

  return {
    backplaneId: backplane,
    hostId: host,
    connectorId: connector,
    model: pickText(current?.model, connectorDefinition?.defaultModel),
    cols: clampDimension(current?.cols, 120),
    rows: clampDimension(current?.rows, 34),
  }
}

export function parseArgumentList(value: string): string[] {
  return value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean)
}

export function formatArgumentList(values: string[]): string {
  return values.join(', ')
}

export function createBackplaneDefinition(): BackplaneDefinition {
  const id = uniqueId('backplane')
  return {
    id,
    displayName: `New backplane ${id.slice(-4)}`,
    kind: 'local',
    enabled: true,
  }
}

export function createHostConfig(backplaneId: string): HostConfig {
  const id = uniqueId('host')
  return {
    id,
    backplaneId,
    displayName: `New host ${id.slice(-4)}`,
    shellExecutable: 'pwsh.exe',
    shellArguments: ['-NoLogo'],
    workingDirectory: null,
    dockerEndpoint: null,
    dockerImage: null,
    enabled: true,
  }
}

export function createConnectorDefinition(): ConnectorDefinition {
  const id = uniqueId('connector')
  return {
    id,
    displayName: `New connector ${id.slice(-4)}`,
    kind: 'shell',
    launchCommand: null,
    launchArguments: [],
    defaultModel: null,
    defaultPermissionMode: null,
    allowedTools: [],
    skipPermissions: false,
    enabled: true,
  }
}

export function createExperimentDefinition(config: AppConfig): ExperimentDefinition {
  const id = uniqueId('experiment')
  const defaultHost = config.hosts.find((host) => host.enabled)?.id
  const defaultConnector = config.connectors.find((connector) => connector.enabled)?.id

  return {
    id,
    displayName: `New experiment ${id.slice(-4)}`,
    description: null,
    hostIds: defaultHost ? [defaultHost] : [],
    connectorIds: defaultConnector ? [defaultConnector] : [],
    models: [],
    cols: 120,
    rows: 34,
    enabled: Boolean(defaultHost && defaultConnector),
  }
}

function clampDimension(value: number | undefined, fallback: number): number {
  if (!value || Number.isNaN(value)) {
    return fallback
  }

  return Math.min(240, Math.max(24, Math.round(value)))
}

function pickValue(values: string[], current: string | undefined): string {
  if (current) {
    const matched = values.find((value) => sameId(value, current))
    if (matched) {
      return matched
    }
  }

  return values[0] ?? ''
}

function pickText(current: string | null | undefined, fallback: string | null | undefined): string | null {
  if (typeof current === 'string') {
    const trimmed = current.trim()
    return trimmed.length > 0 ? trimmed : null
  }

  if (typeof fallback === 'string') {
    const trimmed = fallback.trim()
    return trimmed.length > 0 ? trimmed : null
  }

  return null
}

function uniqueId(prefix: string): string {
  const suffix =
    typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
      ? crypto.randomUUID().slice(0, 8)
      : Math.random().toString(36).slice(2, 10)

  return `${prefix}-${suffix}`
}

function sameId(left: string, right: string): boolean {
  return left.localeCompare(right, undefined, { sensitivity: 'accent' }) === 0
}

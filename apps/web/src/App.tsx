import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'

import { TerminalPane } from './components/TerminalPane'
import { ApiError, createSession, loadAppState, loadClaudeHomeCatalog, loadSessions, runExperiment, saveConfig, stopSession } from './lib/api'
import {
  coerceLaunchDraft,
  createExperimentDefinition,
  createBackplaneDefinition,
  createConnectorDefinition,
  createHostConfig,
  formatArgumentList,
  getConnector,
  getEnabledBackplanes,
  getEnabledConnectors,
  getEnabledHosts,
  normalizeSessionState,
  parseArgumentList,
  type LaunchDraft,
} from './lib/config'
import {
  closeWorkspaceTab,
  createInitialWorkspace,
  ensureSecondarySession,
  focusWorkspaceTab,
  getSessionTabId,
  isSessionTab,
  openSessionTab,
  orchestrationTabId,
  setWorkspaceLayout,
  syncWorkspaceState,
  type WorkspaceLayout,
  type WorkspaceTab,
} from './lib/workspace'
import type {
  AppConfig,
  BackplaneDefinition,
  ClaudeHomeCatalogResponse,
  ClaudeHomeSummary,
  ConnectorDefinition,
  ExperimentDefinition,
  ExperimentRunSummary,
  HostConfig,
  SessionSummary,
  TerminalServerMessage,
} from './types'

type ThemeMode = 'light' | 'dark'
type StudioSection =
  | 'overview'
  | 'workspace'
  | 'sessions'
  | 'backplanes'
  | 'hosts'
  | 'connectors'
  | 'lab'
  | 'agents'
  | 'skills'
  | 'mcp'
  | 'settings'

interface StudioSectionMeta {
  description: string
  eyebrow: string
  label: string
}

interface MetricItem {
  detail?: string
  label: string
  value: number | string
}

interface BlueprintCard {
  badge: string
  description: string
  eyebrow: string
  title: string
}

const compactWorkspaceQuery = '(max-width: 720px), (max-height: 760px)'
const defaultStudioSection: StudioSection = 'overview'
const studioNavigation: Array<{ items: StudioSection[]; title: string }> = [
  {
    title: 'Operate',
    items: ['overview', 'workspace', 'sessions', 'lab'],
  },
  {
    title: 'Runtime',
    items: ['backplanes', 'hosts', 'connectors', 'mcp'],
  },
  {
    title: 'Definitions',
    items: ['agents', 'skills', 'settings'],
  },
]
const studioSectionMeta: Record<StudioSection, StudioSectionMeta> = {
  overview: {
    eyebrow: 'Studio overview',
    label: 'Overview',
    description: 'Runtime health, inventory, and the next operational moves across the local studio.',
  },
  workspace: {
    eyebrow: 'Terminal workspace',
    label: 'Workspace',
    description: 'Launch sessions, compare panes, and orchestrate long-lived terminals without losing fidelity.',
  },
  sessions: {
    eyebrow: 'Session fleet',
    label: 'Sessions',
    description: 'Review live and recent sessions, reopen context quickly, and route work back into the workspace.',
  },
  backplanes: {
    eyebrow: 'Execution fabrics',
    label: 'Backplanes',
    description: 'Define where commands execute and how each host fabric is exposed to the studio.',
  },
  hosts: {
    eyebrow: 'Host inventory',
    label: 'Hosts',
    description: 'Curate local and container host targets, shell defaults, and working directory policies.',
  },
  connectors: {
    eyebrow: 'Connector catalog',
    label: 'Connectors',
    description: 'Shape how shells and agent CLIs launch, what defaults they inherit, and what tools they can use.',
  },
  lab: {
    eyebrow: 'Experiment lab',
    label: 'Lab',
    description: 'Scaffold repeatable experiments, side-by-side evaluations, and runtime verification recipes.',
  },
  agents: {
    eyebrow: 'Agent definitions',
    label: 'Agents',
    description: 'Design reusable agent roles, guardrails, prompts, and execution surfaces for future orchestration.',
  },
  skills: {
    eyebrow: 'Skill definitions',
    label: 'Skills',
    description: 'Organize reusable capabilities, tool bundles, and operator guidance around connector workflows.',
  },
  mcp: {
    eyebrow: 'MCP server registry',
    label: 'MCP servers',
    description: 'Map transport endpoints, scopes, and operational readiness for local and remote MCP surfaces.',
  },
  settings: {
    eyebrow: 'Control surfaces',
    label: 'Settings',
    description: 'Review persistence, theme behavior, polling cadence, and other studio-wide operating defaults.',
  },
}
const labBlueprints: BlueprintCard[] = [
  {
    eyebrow: 'Runtime parity',
    title: 'Connector sweep',
    badge: 'Scaffolded',
    description: 'Compare shell, Claude, and Ollama launch envelopes across identical host targets before a release.',
  },
  {
    eyebrow: 'Workspace rehearsal',
    title: 'Multi-pane walkthrough',
    badge: 'Ready',
    description: 'Pin a primary session, open a compare pane, and record the exact state transitions needed for E2E.',
  },
  {
    eyebrow: 'Capability gate',
    title: 'Docker availability drill',
    badge: 'Conditional',
    description: 'Track which experiments require Docker or model presence so the lab can explain skipped coverage.',
  },
]
const agentBlueprints: BlueprintCard[] = [
  {
    eyebrow: 'Operator role',
    title: 'Runtime shepherd',
    badge: 'Draft',
    description: 'Own launch policy, backplane choice, and recovery steps for live session operations.',
  },
  {
    eyebrow: 'Evaluation role',
    title: 'Experiment reviewer',
    badge: 'Draft',
    description: 'Inspect transcripts, compare outputs, and mark experiments ready for publication into the lab.',
  },
  {
    eyebrow: 'Safety role',
    title: 'Policy sentinel',
    badge: 'Draft',
    description: 'Check connector permissions, allowed tools, and unsafe launch flags before runtime changes ship.',
  },
]
const skillBlueprints: BlueprintCard[] = [
  {
    eyebrow: 'Tooling bundle',
    title: 'Session recovery kit',
    badge: 'Concept',
    description: 'Wrap transcript replay, reconnect guidance, and session manifest inspection into a reusable operator skill.',
  },
  {
    eyebrow: 'Connector bundle',
    title: 'Model tuning starter',
    badge: 'Concept',
    description: 'Preload preferred launch flags, model overrides, and comparison steps for local model experiments.',
  },
  {
    eyebrow: 'Governance bundle',
    title: 'Compliance snapshot',
    badge: 'Concept',
    description: 'Collect WCAG, theme, and runtime policy checks into a repeatable review skill for release candidates.',
  },
]
const mcpBlueprints: BlueprintCard[] = [
  {
    eyebrow: 'Filesystem',
    title: 'Workspace context server',
    badge: 'Planned',
    description: 'Expose project snapshots, living docs, and checkpoint artifacts through a scoped local transport.',
  },
  {
    eyebrow: 'Source control',
    title: 'Git automation server',
    badge: 'Planned',
    description: 'Surface repo status, commit policies, and change review helpers without leaving the studio shell.',
  },
  {
    eyebrow: 'Browser',
    title: 'Review evidence server',
    badge: 'Planned',
    description: 'Publish screenshots, BDD evidence, and verification traces as first-class operational assets.',
  },
]

function App() {
  const themeMode = useSystemTheme()
  const compactWorkspace = useCompactWorkspace()
  const activeSection = useStudioSection()
  const [savedConfig, setSavedConfig] = useState<AppConfig | null>(null)
  const [configDraft, setConfigDraft] = useState<AppConfig | null>(null)
  const [claudeHome, setClaudeHome] = useState<ClaudeHomeSummary | null>(null)
  const [claudeCatalog, setClaudeCatalog] = useState<ClaudeHomeCatalogResponse | null>(null)
  const [sessions, setSessions] = useState<SessionSummary[]>([])
  const [experimentRuns, setExperimentRuns] = useState<ExperimentRunSummary[]>([])
  const [launchDraft, setLaunchDraft] = useState<LaunchDraft>({
    backplaneId: '',
    hostId: '',
    connectorId: '',
    model: null,
    permissionMode: null,
    skipPermissions: null,
    allowedTools: null,
    agent: null,
    workingDirectory: null,
    cols: 120,
    rows: 34,
  })
  const [workspace, setWorkspace] = useState(() => createInitialWorkspace([]))
  const [isWorkspaceBladeOpen, setIsWorkspaceBladeOpen] = useState(false)
  const [isWorkspaceStudioNavOpen, setIsWorkspaceStudioNavOpen] = useState(() => shouldStartWorkspaceStudioNavOpen())
  const [isBooting, setIsBooting] = useState(true)
  const [isCreatingSession, setIsCreatingSession] = useState(false)
  const [isSavingConfig, setIsSavingConfig] = useState(false)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [isRunningExperiment, setIsRunningExperiment] = useState(false)
  const [isStoppingSession, setIsStoppingSession] = useState(false)
  const [statusMessage, setStatusMessage] = useState<string>('Control deck ready.')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [configDirty, setConfigDirty] = useState(false)

  const refreshSessions = useCallback(async () => {
    try {
      setIsRefreshing(true)
      const nextSessions = await loadSessions()
      setSessions(nextSessions)
    } catch (error) {
      setErrorMessage(asMessage(error))
    } finally {
      setIsRefreshing(false)
    }
  }, [])

  const loadWorkspace = useCallback(async () => {
    try {
      setIsBooting(true)
      setErrorMessage(null)

      const state = await loadAppState()
      setSavedConfig(state.config)
      setConfigDraft(state.config)
      setClaudeHome(state.claudeHome)
      setClaudeCatalog(null)
      setSessions(state.sessions)
      setExperimentRuns(state.experimentRuns)
      setWorkspace(createInitialWorkspace(state.sessions))
      setLaunchDraft(coerceLaunchDraft(state.config))
      setConfigDirty(false)
      setStatusMessage('Control deck synchronized.')
    } catch (error) {
      setErrorMessage(asMessage(error))
    } finally {
      setIsBooting(false)
    }
  }, [])

  const refreshStudioData = useCallback(async () => {
    try {
      setIsRefreshing(true)
      setErrorMessage(null)

      const nextState = await loadAppState()
      setSessions(nextState.sessions)
      setExperimentRuns(nextState.experimentRuns)
      setClaudeHome(nextState.claudeHome)

      const nextWorkspaceConnector = getConnector(nextState.config, launchDraft.connectorId) ?? null
      if (shouldLoadClaudeCatalogForSection(activeSection, nextState.claudeHome?.exists, nextWorkspaceConnector)) {
        setClaudeCatalog(nextState.claudeHome?.exists ? await loadClaudeHomeCatalog() : null)
      }
    } catch (error) {
      setErrorMessage(asMessage(error))
    } finally {
      setIsRefreshing(false)
    }
  }, [activeSection, launchDraft.connectorId])

  useEffect(() => {
    void loadWorkspace()
  }, [loadWorkspace])

  useEffect(() => {
    const workspaceConnector = savedConfig ? getConnector(savedConfig, launchDraft.connectorId) ?? null : null
    if (!shouldLoadClaudeCatalogForSection(activeSection, claudeHome?.exists, workspaceConnector) || claudeCatalog) {
      return
    }

    let cancelled = false

    void (async () => {
      try {
        const nextCatalog = await loadClaudeHomeCatalog()
        if (!cancelled) {
          setClaudeCatalog(nextCatalog)
        }
      } catch (error) {
        if (!cancelled) {
          setErrorMessage(asMessage(error))
        }
      }
    })()

    return () => {
      cancelled = true
    }
  }, [activeSection, claudeCatalog, claudeHome?.exists, launchDraft.connectorId, savedConfig])

  useEffect(() => {
    if (!savedConfig) {
      return
    }

    setLaunchDraft((current) => coerceLaunchDraft(savedConfig, current))
  }, [savedConfig])

  useEffect(() => {
    setWorkspace((current) => syncWorkspaceState(current, sessions))
  }, [sessions])

  useEffect(() => {
    if (!compactWorkspace) {
      return
    }

    setWorkspace((current) => (current.layout === 'single' ? current : setWorkspaceLayout(current, 'single')))
  }, [compactWorkspace])

  useEffect(() => {
    if (activeSection !== 'workspace') {
      setIsWorkspaceStudioNavOpen(true)
      return
    }

    setIsWorkspaceStudioNavOpen(false)
    setIsWorkspaceBladeOpen(false)
  }, [activeSection])

  useEffect(() => {
    const intervalId = window.setInterval(() => {
      void refreshSessions()
    }, 5000)

    return () => {
      window.clearInterval(intervalId)
    }
  }, [refreshSessions])

  const enabledBackplanes = useMemo(
    () => (savedConfig ? getEnabledBackplanes(savedConfig) : []),
    [savedConfig],
  )
  const availableHosts = useMemo(
    () => (savedConfig ? getEnabledHosts(savedConfig, launchDraft.backplaneId) : []),
    [launchDraft.backplaneId, savedConfig],
  )
  const enabledConnectors = useMemo(
    () => (savedConfig ? getEnabledConnectors(savedConfig) : []),
    [savedConfig],
  )
  const selectedConnector = useMemo(
    () => (savedConfig ? getConnector(savedConfig, launchDraft.connectorId) ?? null : null),
    [launchDraft.connectorId, savedConfig],
  )
  const sessionMap = useMemo(() => new Map(sessions.map((session) => [session.id, session])), [sessions])
  const activeTab = workspace.tabs.find((tab) => tab.id === workspace.activeTabId) ?? workspace.tabs[0] ?? null
  const secondaryTab =
    workspace.secondaryTabId !== null
      ? workspace.tabs.find((tab) => tab.id === workspace.secondaryTabId) ?? null
      : null
  const activeSession = isSessionTab(activeTab) ? (sessionMap.get(activeTab.sessionId) ?? null) : null
  const secondarySession = isSessionTab(secondaryTab) ? (sessionMap.get(secondaryTab.sessionId) ?? null) : null
  const workspaceTabOptions = useMemo(
    () => [
      { label: 'Orchestration', tabId: orchestrationTabId },
      ...sessions.map((session) => ({ label: session.id, tabId: getSessionTabId(session.id) })),
    ],
    [sessions],
  )
  const canSplitWorkspace = !compactWorkspace && (sessions.length >= 2 || workspace.secondaryTabId !== null)

  const updateLaunchDraft = <K extends keyof LaunchDraft>(key: K, value: LaunchDraft[K]) => {
    if (!savedConfig) {
      return
    }

    setLaunchDraft((current) => {
      if (key === 'connectorId') {
        const connector = getConnector(savedConfig, String(value))
        return coerceLaunchDraft(savedConfig, {
          ...current,
          connectorId: String(value),
          ...getConnectorLaunchDefaults(connector),
        })
      }

      if (key === 'backplaneId') {
        const nextBackplaneId = String(value)
        const nextHostId = getEnabledHosts(savedConfig, nextBackplaneId)[0]?.id
        return coerceLaunchDraft(savedConfig, {
          ...current,
          backplaneId: nextBackplaneId,
          hostId: nextHostId,
          workingDirectory: undefined,
        })
      }

      if (key === 'hostId') {
        return coerceLaunchDraft(savedConfig, {
          ...current,
          hostId: String(value),
          workingDirectory: undefined,
        })
      }

      return coerceLaunchDraft(savedConfig, {
        ...current,
        [key]: value,
      })
    })
  }

  const updateConfigDraft = (updater: (current: AppConfig) => AppConfig) => {
    setConfigDraft((current) => {
      if (!current) {
        return current
      }

      setConfigDirty(true)
      return updater(current)
    })
  }

  const handleLaunchSession = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!savedConfig) {
      return
    }

    try {
      setIsCreatingSession(true)
      setErrorMessage(null)
      const createdSession = await createSession(launchDraft)
      setSessions((current) => [createdSession, ...current])
      setWorkspace((current) => openSessionTab(current, createdSession))
      setIsWorkspaceBladeOpen(false)
      setStatusMessage(`Session ${formatWorkspaceLabel(createdSession.id)} is live.`)
    } catch (error) {
      setErrorMessage(asMessage(error))
    } finally {
      setIsCreatingSession(false)
    }
  }

  const handleStopSession = async (sessionId: string) => {
    const session = sessionMap.get(sessionId)
    if (!session) {
      return
    }

    try {
      setIsStoppingSession(true)
      setErrorMessage(null)
      await stopSession(session.id)
      await refreshSessions()
      setStatusMessage(`Stop requested for ${session.id}.`)
    } catch (error) {
      setErrorMessage(asMessage(error))
    } finally {
      setIsStoppingSession(false)
    }
  }

  const handleSaveConfig = async () => {
    if (!configDraft) {
      return
    }

    try {
      setIsSavingConfig(true)
      setErrorMessage(null)
      const persistedConfig = await saveConfig(configDraft)
      setSavedConfig(persistedConfig)
      setConfigDraft(persistedConfig)
      setConfigDirty(false)
      setLaunchDraft((current) => coerceLaunchDraft(persistedConfig, current))
      setStatusMessage('Configuration saved.')
    } catch (error) {
      setErrorMessage(asMessage(error))
    } finally {
      setIsSavingConfig(false)
    }
  }

  const handleRunExperiment = async (experimentId: string) => {
    try {
      setIsRunningExperiment(true)
      setErrorMessage(null)
      const run = await runExperiment(experimentId)
      setExperimentRuns((current) => [run, ...current.filter((item) => item.id !== run.id)])
      await refreshSessions()
      setStatusMessage(`Experiment ${formatWorkspaceLabel(run.experimentDisplayName)} launched ${run.variantCount} run${run.variantCount === 1 ? '' : 's'}.`)
    } catch (error) {
      setErrorMessage(asMessage(error))
    } finally {
      setIsRunningExperiment(false)
    }
  }

  const handleResetDraft = () => {
    if (!savedConfig) {
      return
    }

    setConfigDraft(savedConfig)
    setConfigDirty(false)
    setStatusMessage('Configuration edits discarded.')
  }

  const handleSessionMessage = useCallback(
    (sessionId: string, message: TerminalServerMessage) => {
      if (message.type === 'state' && message.state) {
        setSessions((current) =>
          current.map((session) =>
            session.id === sessionId
              ? {
                  ...session,
                  state: normalizeSessionState(message.state),
                }
              : session,
          ),
        )
        return
      }

      if (message.type === 'exit') {
        setSessions((current) =>
          current.map((session) =>
            session.id === sessionId
              ? {
                  ...session,
                  state: normalizeSessionState(message.state),
                  exitCode: message.exitCode ?? session.exitCode,
                  endedAt: new Date().toISOString(),
                }
              : session,
          ),
        )
        return
      }

      if (message.type === 'error') {
        setSessions((current) =>
          current.map((session) =>
            session.id === sessionId
              ? {
                  ...session,
                  state: normalizeSessionState(message.state),
                  error: message.message ?? session.error,
                  endedAt: new Date().toISOString(),
                }
              : session,
          ),
        )
        setErrorMessage(message.message ?? 'Terminal session reported an error.')
      }
    },
    [],
  )

  const handleOpenSession = (session: SessionSummary, target: 'primary' | 'secondary' = 'primary') => {
    const resolvedTarget = compactWorkspace ? 'primary' : target
    setWorkspace((current) => openSessionTab(current, session, { target: resolvedTarget }))
    setStatusMessage(
      resolvedTarget === 'secondary'
        ? `Comparing ${session.id} in a split pane.`
        : `Focused ${session.id} in the workspace.`,
    )
  }

  const handleOpenOrchestration = () => {
    setWorkspace((current) => focusWorkspaceTab(current, orchestrationTabId))
    setStatusMessage('Orchestration board is active.')
  }

  const handleCloseWorkspaceTab = (tabId: string) => {
    setWorkspace((current) => closeWorkspaceTab(current, tabId))
  }

  const handleSetWorkspaceLayout = (layout: WorkspaceLayout) => {
    setWorkspace((current) => {
      if (layout === 'single') {
        return setWorkspaceLayout(current, layout)
      }

      return setWorkspaceLayout(ensureSecondarySession(current, sessions), layout)
    })
  }

  const handleSelectPaneTab = (target: 'primary' | 'secondary', tabId: string) => {
    if (tabId === orchestrationTabId) {
      setWorkspace((current) => {
        if (target === 'primary') {
          return focusWorkspaceTab(current, orchestrationTabId)
        }

        if (current.activeTabId === orchestrationTabId) {
          return current
        }

        return {
          ...current,
          layout: current.layout === 'single' ? 'split-vertical' : current.layout,
          secondaryTabId: orchestrationTabId,
        }
      })
      return
    }

    const session = sessionMap.get(tabId.replace(/^session:/, ''))
    if (!session) {
      return
    }

    handleOpenSession(session, target)
  }

  if (isBooting) {
    return (
      <div className="boot-screen">
        <p className="eyebrow">ClankYankers</p>
        <h1>Waking the control deck</h1>
        <p className="boot-copy">
          Pulling local config, inventorying backplanes, and syncing live sessions.
        </p>
      </div>
    )
  }

  if (!savedConfig || !configDraft) {
    return (
      <div className="boot-screen">
        <p className="eyebrow">ClankYankers</p>
        <h1>Control deck offline</h1>
        <p className="boot-copy">{errorMessage ?? 'The local server did not respond.'}</p>
        <button className="button button--solid" onClick={() => void loadWorkspace()}>
          Retry bootstrap
        </button>
      </div>
    )
  }

  const activeSectionMeta = studioSectionMeta[activeSection]
  const claudeSettings = claudeHome?.exists ? claudeHome.settings : null
  const claudeAgentCount = claudeHome?.exists ? claudeHome.agentCount : 0
  const claudeSkillCount = claudeHome?.exists ? claudeHome.skillCount : 0
  const claudeCommandCount = claudeHome?.exists ? claudeHome.commandCount : 0
  const claudeMcpArtifactCount = claudeHome?.exists ? claudeHome.mcpArtifactCount : 0
  const catalogAgents = claudeCatalog?.agents ?? []
  const catalogSkills = claudeCatalog?.skills ?? []
  const agentCards: BlueprintCard[] =
    catalogAgents.length > 0
      ? catalogAgents.slice(0, 12).map((agent) => ({
          eyebrow: 'Local agent',
          title: agent.name,
          badge: 'Catalog',
          description: 'Discovered from the local Claude home using a sanitized catalog entry.',
        }))
      : agentBlueprints
  const skillCards: BlueprintCard[] =
    catalogSkills.length > 0
      ? catalogSkills.slice(0, 12).map((skill) => ({
          eyebrow: 'Local skill',
          title: skill.name,
          badge: skill.commandCount > 0 ? `${skill.commandCount} commands` : 'Skill',
          description: 'Discovered from the local Claude home using a sanitized catalog entry.',
        }))
      : skillBlueprints
  const mcpCards: BlueprintCard[] =
    claudeMcpArtifactCount > 0 || (claudeSettings?.enabledPluginCount ?? 0) > 0
      ? [
          {
            eyebrow: 'Local signal',
            title: 'Claude MCP artifacts',
            badge: `${claudeMcpArtifactCount}`,
            description: 'Top-level MCP-related artifacts were discovered in the local Claude home.',
          },
          {
            eyebrow: 'Plugin posture',
            title: 'Enabled Claude plugins',
            badge: `${claudeSettings?.enabledPluginCount ?? 0}`,
            description: 'Only counts are exposed here so local plugin state stays visible without leaking names.',
          },
        ]
      : mcpBlueprints
  const runningSessionCount = sessions.filter((session) => session.state === 'Running').length
  const attentionSessionCount = sessions.filter((session) => session.state === 'Failed').length
  const experimentSessionCount = sessions.filter((session) => session.experimentId).length
  const enabledExperimentCount = savedConfig.experiments.filter((experiment) => experiment.enabled).length
  const activeExperimentRunCount = experimentRuns.filter((run) => run.activeSessionCount > 0).length
  const launchedVariantCount = experimentRuns.reduce((total, run) => total + run.variantCount, 0)
  const sessionMetrics: MetricItem[] = [
    {
      label: 'Live sessions',
      value: sessions.length,
      detail: sessions.length > 0 ? 'Tracked and reconnectable from the workspace.' : 'Ready for the first launch.',
    },
    {
      label: 'Running now',
      value: runningSessionCount,
      detail: runningSessionCount > 0 ? 'Streaming terminals are active.' : 'No active runtime processes.',
    },
    {
      label: 'Needs attention',
      value: attentionSessionCount,
      detail: attentionSessionCount > 0 ? 'Failed sessions are waiting for review.' : 'No failed sessions detected.',
    },
  ]
  const inventoryMetrics: MetricItem[] = [
    {
      label: 'Backplanes',
      value: savedConfig.backplanes.length,
      detail: `${enabledBackplanes.length} enabled execution fabrics`,
    },
    {
      label: 'Hosts',
      value: savedConfig.hosts.length,
      detail: `${savedConfig.hosts.filter((host) => host.enabled).length} enabled launch targets`,
    },
    {
      label: 'Connectors',
      value: savedConfig.connectors.length,
      detail: `${enabledConnectors.length} ready for launch`,
    },
    {
      label: 'Claude assets',
      value: claudeAgentCount + claudeSkillCount,
      detail: claudeHome?.exists ? `${claudeAgentCount} agents and ${claudeSkillCount} skills discovered` : 'No ~/.claude catalog detected',
    },
  ]
  const labMetrics: MetricItem[] = [
    {
      label: 'Experiments',
      value: savedConfig.experiments.length,
      detail: `${enabledExperimentCount} enabled definitions ready for launch`,
    },
    {
      label: 'Run groups',
      value: experimentRuns.length,
      detail: experimentRuns.length > 0 ? `${activeExperimentRunCount} still have active sessions` : 'No experiment batches launched yet.',
    },
    {
      label: 'Variants',
      value: launchedVariantCount,
      detail: launchedVariantCount > 0 ? 'Launched from saved experiment matrices.' : 'Waiting for the first structured run.',
    },
    {
      label: 'Experiment sessions',
      value: experimentSessionCount,
      detail: experimentSessionCount > 0 ? 'Live sessions tied to an experiment definition.' : 'Manual launches still dominate the runtime ledger.',
    },
  ]
  const sectionBadges: Record<StudioSection, string | number> = {
    overview: `${runningSessionCount}/${sessions.length}`,
    workspace: formatWorkspaceLayout(workspace.layout),
    sessions: sessions.length,
    backplanes: savedConfig.backplanes.length,
    hosts: savedConfig.hosts.length,
    connectors: savedConfig.connectors.length,
    lab: `${enabledExperimentCount}/${savedConfig.experiments.length}`,
    agents: claudeAgentCount || agentBlueprints.length,
    skills: claudeSkillCount || skillBlueprints.length,
    mcp: claudeMcpArtifactCount || claudeSettings?.enabledPluginCount || mcpBlueprints.length,
    settings: configDirty ? 'draft' : 'stable',
  }
  const isWorkspaceSection = activeSection === 'workspace'
  const isWorkspaceFocusMode = isWorkspaceSection && !isWorkspaceStudioNavOpen
  const handleToggleWorkspaceBlade = (open: boolean) => {
    setIsWorkspaceBladeOpen(open)
    if (open) {
      setIsWorkspaceStudioNavOpen(false)
    }
  }
  const handleToggleWorkspaceStudioNav = (open: boolean) => {
    setIsWorkspaceStudioNavOpen(open)
    if (open) {
      setIsWorkspaceBladeOpen(false)
    }
  }
  const openStudioSection = (section: StudioSection) => {
    setStudioSectionHash(section)
  }
  const showConfigActions = ['workspace', 'backplanes', 'hosts', 'connectors', 'lab'].includes(activeSection)
  const refreshActionLabel =
    activeSection === 'lab'
      ? 'Refresh lab'
      : showConfigActions
        ? 'Refresh sessions'
        : 'Refresh'
  const handleRefreshAction = activeSection === 'lab' ? refreshStudioData : showConfigActions ? refreshSessions : refreshStudioData
  const handleOpenWorkspaceSession = (session: SessionSummary, target: 'primary' | 'secondary' = 'primary') => {
    setStudioSectionHash('workspace')
    handleOpenSession(session, target)
  }

  let currentPage: ReactNode
  switch (activeSection) {
    case 'workspace':
      currentPage = (
        <WorkspacePage
          activeSession={activeSession}
          activeTab={activeTab}
          availableHosts={availableHosts}
          canSplitWorkspace={canSplitWorkspace}
          claudeCatalog={claudeCatalog}
          claudeHome={claudeHome}
          compactWorkspace={compactWorkspace}
          configDraft={configDraft}
          enabledBackplanes={enabledBackplanes}
          enabledConnectors={enabledConnectors}
          isCreatingSession={isCreatingSession}
          isLaunchBladeOpen={isWorkspaceBladeOpen}
          isSavingConfig={isSavingConfig}
          isStoppingSession={isStoppingSession}
          launchDraft={launchDraft}
          onCloseWorkspaceTab={handleCloseWorkspaceTab}
          onLaunchSession={handleLaunchSession}
          onOpenOrchestration={handleOpenOrchestration}
          onOpenSession={handleOpenSession}
          onResetDraft={handleResetDraft}
          onSaveConfig={handleSaveConfig}
          onSelectPaneTab={handleSelectPaneTab}
          onSessionMessage={handleSessionMessage}
          onSetWorkspaceLayout={handleSetWorkspaceLayout}
          onStopSession={handleStopSession}
          onToggleLaunchBlade={handleToggleWorkspaceBlade}
          onUpdateConfigDraft={updateConfigDraft}
          onUpdateLaunchDraft={updateLaunchDraft}
          secondarySession={secondarySession}
          secondaryTab={secondaryTab}
          selectedConnector={selectedConnector}
          sessionMetrics={sessionMetrics}
          sessions={sessions}
          themeMode={themeMode}
          workspace={workspace}
          workspaceTabOptions={workspaceTabOptions}
        />
      )
      break
    case 'sessions':
      currentPage = (
        <SessionsPage
          compactWorkspace={compactWorkspace}
          sessions={sessions}
          sessionMetrics={sessionMetrics}
          onOpenSection={openStudioSection}
          onOpenSession={handleOpenWorkspaceSession}
        />
      )
      break
    case 'backplanes':
      currentPage = (
        <InventoryPage
          eyebrow="Execution fabrics"
          title="Backplane registry"
          description="Define the fabrics your studio can launch into, then flow them directly into workspace launches."
          metrics={[
            inventoryMetrics[0],
            { label: 'Enabled', value: enabledBackplanes.length, detail: 'Backplanes currently available for launch.' },
            { label: 'Kinds', value: new Set(savedConfig.backplanes.map((backplane) => backplane.kind)).size, detail: 'Unique execution fabric types in the registry.' },
          ]}
          aside={
            <ProductNote
              title="How this page fits"
              body="Backplanes are the highest-level execution surfaces. Hosts attach concrete launch targets, and sessions inherit them at run time."
            />
          }
        >
          <ConfigBlock
            title="Backplanes"
            description="Execution fabrics available to new sessions."
            actionLabel="Add backplane"
            onAdd={() =>
              updateConfigDraft((current) => ({
                ...current,
                backplanes: [...current.backplanes, createBackplaneDefinition()],
              }))
            }
          >
            {configDraft.backplanes.map((backplane, index) => (
              <BackplaneEditor
                key={backplane.id}
                backplane={backplane}
                onChange={(nextValue) =>
                  updateConfigDraft((current) => ({
                    ...current,
                    backplanes: updateItem(current.backplanes, index, nextValue),
                  }))
                }
                onRemove={() =>
                  updateConfigDraft((current) => ({
                    ...current,
                    backplanes: current.backplanes.filter((_, itemIndex) => itemIndex !== index),
                  }))
                }
              />
            ))}
          </ConfigBlock>
        </InventoryPage>
      )
      break
    case 'hosts':
      currentPage = (
        <InventoryPage
          eyebrow="Host inventory"
          title="Launch hosts"
          description="Map each backplane to concrete local or container targets with the right shell and working-directory defaults."
          metrics={[
            inventoryMetrics[1],
            {
              label: 'Docker-ready hosts',
              value: savedConfig.hosts.filter((host) => Boolean(host.dockerImage)).length,
              detail: 'Hosts that already declare a container image.',
            },
            {
              label: 'Working dirs',
              value: savedConfig.hosts.filter((host) => Boolean(host.workingDirectory)).length,
              detail: 'Hosts pinned to a preferred execution directory.',
            },
          ]}
          aside={
            <ProductNote
              title="Launch guidance"
              body="Hosts are the most concrete runtime targets in the product. They should stay obvious, auditable, and connector-agnostic."
            />
          }
        >
          <ConfigBlock
            title="Hosts"
            description="Concrete local or container targets exposed by each backplane."
            actionLabel="Add host"
            onAdd={() =>
              updateConfigDraft((current) => ({
                ...current,
                hosts: [
                  ...current.hosts,
                  createHostConfig(current.backplanes[0]?.id ?? current.hosts[0]?.backplaneId ?? 'local'),
                ],
              }))
            }
          >
            {configDraft.hosts.map((host, index) => (
              <HostEditor
                key={host.id}
                host={host}
                backplanes={configDraft.backplanes}
                onChange={(nextValue) =>
                  updateConfigDraft((current) => ({
                    ...current,
                    hosts: updateItem(current.hosts, index, nextValue),
                  }))
                }
                onRemove={() =>
                  updateConfigDraft((current) => ({
                    ...current,
                    hosts: current.hosts.filter((_, itemIndex) => itemIndex !== index),
                  }))
                }
              />
            ))}
          </ConfigBlock>
        </InventoryPage>
      )
      break
    case 'connectors':
      currentPage = (
        <InventoryPage
          eyebrow="Connector catalog"
          title="Connector definitions"
          description="Configure shell and agent launch defaults, model hints, and permission envelopes for each runtime connector."
          metrics={[
            inventoryMetrics[2],
            {
              label: 'Model-aware',
              value: savedConfig.connectors.filter((connector) => connector.defaultModel).length,
              detail: 'Connectors with a model default ready for launch.',
            },
            {
              label: 'Policy-aware',
              value: savedConfig.connectors.filter((connector) => connector.defaultPermissionMode || connector.skipPermissions).length,
              detail: 'Connectors with explicit permission posture.',
            },
          ]}
          aside={
            <ProductNote
              title="Connector policy"
              body="Connectors direct operators into the right CLI immediately. Defaults here should make launch intent obvious before any session starts."
            />
          }
        >
          <ConfigBlock
            title="Connectors"
            description="CLI adapters mapped into each session."
            actionLabel="Add connector"
            onAdd={() =>
              updateConfigDraft((current) => ({
                ...current,
                connectors: [...current.connectors, createConnectorDefinition()],
              }))
            }
          >
            {configDraft.connectors.map((connector, index) => (
              <ConnectorEditor
                key={connector.id}
                connector={connector}
                onChange={(nextValue) =>
                  updateConfigDraft((current) => ({
                    ...current,
                    connectors: updateItem(current.connectors, index, nextValue),
                  }))
                }
                onRemove={() =>
                  updateConfigDraft((current) => ({
                    ...current,
                    connectors: current.connectors.filter((_, itemIndex) => itemIndex !== index),
                  }))
                }
              />
            ))}
          </ConfigBlock>
        </InventoryPage>
      )
      break
    case 'lab':
      currentPage = (
        <LabPage
          configDirty={configDirty}
          experiments={configDraft.experiments}
          experimentRuns={experimentRuns}
          isRunningExperiment={isRunningExperiment}
          metrics={labMetrics}
          onAddExperiment={() =>
            updateConfigDraft((current) => ({
              ...current,
              experiments: [...current.experiments, createExperimentDefinition(current)],
            }))
          }
          onExperimentChange={(index, nextValue) =>
            updateConfigDraft((current) => ({
              ...current,
              experiments: updateItem(current.experiments, index, nextValue),
            }))
          }
          onExperimentRemove={(index) =>
            updateConfigDraft((current) => ({
              ...current,
              experiments: current.experiments.filter((_, itemIndex) => itemIndex !== index),
            }))
          }
          onOpenSection={openStudioSection}
          onOpenSession={handleOpenWorkspaceSession}
          onRunExperiment={handleRunExperiment}
          sessions={sessions}
          templates={labBlueprints}
        />
      )
      break
    case 'agents':
      currentPage = (
        <BlueprintPage
          eyebrow="Agent definitions"
          title="Local Claude agent catalog"
          description={
            claudeAgentCount > 0
              ? 'These agent slugs are loaded on demand from the local Claude home so the studio reflects the real operator catalog without shipping raw file contents.'
              : 'No local Claude agents were discovered, so the page falls back to suggested starter roles for the studio.'
          }
          cards={agentCards}
          metrics={[
            {
              label: 'Discovered agents',
              value: claudeAgentCount || agentBlueprints.length,
              detail: claudeAgentCount > 0 ? 'Loaded from ~/.claude via a sanitized catalog endpoint.' : 'Suggested starter roles for the studio.',
            },
            { label: 'Connectors', value: enabledConnectors.length, detail: 'Potential agent entrypoints already in runtime config.' },
            { label: 'Policy drafts', value: savedConfig.connectors.filter((connector) => connector.skipPermissions).length, detail: 'Connectors currently bypassing standard permission prompts.' },
          ]}
          quickLinks={[
            { href: createStudioSectionHref('skills'), label: 'Review skills' },
            { href: createStudioSectionHref('settings'), label: 'Open settings' },
          ]}
        />
      )
      break
    case 'skills':
      currentPage = (
        <BlueprintPage
          eyebrow="Skill definitions"
          title="Local Claude skill catalog"
          description={
            claudeSkillCount > 0
              ? 'Reusable skill slugs are discovered from the local Claude home, including bundled command counts, without exposing file paths or frontmatter copy.'
              : 'No local Claude skills were discovered, so the page shows product-oriented starter concepts instead.'
          }
          cards={skillCards}
          metrics={[
            {
              label: 'Discovered skills',
              value: claudeSkillCount || skillBlueprints.length,
              detail: claudeSkillCount > 0 ? `${claudeCommandCount} custom commands found under ~/.claude.` : 'Starter bundles for recovery, tuning, and governance.',
            },
            { label: 'Tool-aware connectors', value: savedConfig.connectors.filter((connector) => connector.allowedTools.length > 0).length, detail: 'Connectors that already encode tool policy.' },
            { label: 'Save state', value: configDirty ? 'Unsaved edits' : 'In sync', detail: 'Definition scaffolds should track config discipline too.' },
          ]}
          quickLinks={[
            { href: createStudioSectionHref('agents'), label: 'Open agents' },
            { href: createStudioSectionHref('workspace'), label: 'Open workspace' },
          ]}
        />
      )
      break
    case 'mcp':
      currentPage = (
        <BlueprintPage
          eyebrow="MCP registry"
          title="Claude MCP and plugin surfaces"
          description={
            claudeMcpArtifactCount > 0 || (claudeSettings?.enabledPluginCount ?? 0) > 0
              ? 'This view surfaces count-based MCP and plugin signals from the local Claude home without exposing private names or command strings.'
              : 'No MCP-specific local artifacts were discovered yet, so the page keeps the broader rollout blueprint visible.'
          }
          cards={mcpCards}
          metrics={[
            {
              label: 'Artifacts',
              value: claudeMcpArtifactCount || mcpBlueprints.length,
              detail: claudeMcpArtifactCount > 0 ? 'Top-level MCP-related artifacts discovered in ~/.claude.' : 'Starter surfaces for the first MCP rollout.',
            },
            {
              label: 'Enabled plugins',
              value: claudeSettings?.enabledPluginCount ?? 0,
              detail: 'Enabled plugin state is summarized from local Claude settings.',
            },
            { label: 'Operational anchors', value: sessions.length + enabledConnectors.length, detail: 'Existing studio surfaces MCP can amplify.' },
            { label: 'Status', value: claudeHome?.exists ? 'Connected' : 'Scaffold', detail: 'This page is ready for persistence and live health checks next.' },
          ]}
          quickLinks={[
            { href: createStudioSectionHref('overview'), label: 'Back to overview' },
            { href: createStudioSectionHref('settings'), label: 'Review settings' },
          ]}
        />
      )
      break
    case 'settings':
      currentPage = (
        <SettingsPage
          claudeHome={claudeHome}
          compactWorkspace={compactWorkspace}
          config={savedConfig}
          configDirty={configDirty}
          inventoryMetrics={inventoryMetrics}
          sessionMetrics={sessionMetrics}
          themeMode={themeMode}
        />
      )
      break
    case 'overview':
    default:
      currentPage = (
        <OverviewPage
          claudeHome={claudeHome}
          compactWorkspace={compactWorkspace}
          config={savedConfig}
          inventoryMetrics={inventoryMetrics}
          onOpenSection={openStudioSection}
          onOpenSession={handleOpenWorkspaceSession}
          sessionMetrics={sessionMetrics}
          sessions={sessions}
          statusMessage={statusMessage}
          workspace={workspace}
        />
      )
      break
  }

  return (
    <>
      <a href="#main-content" className="skip-link">
        Skip to main content
      </a>
      <div
        className={`studio-shell${isWorkspaceSection ? ' studio-shell--workspace' : ''}${isWorkspaceFocusMode ? ' studio-shell--workspace-focus' : ''}${isWorkspaceSection && isWorkspaceStudioNavOpen ? ' studio-shell--workspace-nav-open' : ''}`}
      >
        <aside
          aria-hidden={isWorkspaceSection ? !isWorkspaceStudioNavOpen : undefined}
          className="studio-sidebar"
          data-testid="studio-sidebar"
          inert={isWorkspaceSection ? !isWorkspaceStudioNavOpen : undefined}
        >
          <div className="studio-sidebar__brand">
            <p className="eyebrow">Local orchestration studio</p>
            <h1>ClankYankers</h1>
            <p>
              A product shell for sessions, runtimes, definitions, and experiments built around a real terminal workspace.
            </p>
          </div>

          <div className="studio-sidebar__metrics">
            <MetricStrip items={sessionMetrics.slice(0, 2)} />
          </div>

          <nav aria-label="Product sections" className="studio-nav">
            {studioNavigation.map((group) => (
              <div className="studio-nav__group" key={group.title}>
                <p className="studio-nav__label">{group.title}</p>
                <div className="studio-nav__items">
                  {group.items.map((section) => (
                    <a
                      key={section}
                      className={`studio-nav__item${section === activeSection ? ' is-active' : ''}`}
                      data-testid={`nav-section-${section}`}
                      href={createStudioSectionHref(section)}
                      aria-current={section === activeSection ? 'page' : undefined}
                    >
                      <span>{studioSectionMeta[section].label}</span>
                      <strong>{sectionBadges[section]}</strong>
                    </a>
                  ))}
                </div>
              </div>
            ))}
          </nav>
        </aside>

        {isWorkspaceSection ? (
          <button
            aria-hidden={!isWorkspaceStudioNavOpen}
            aria-label="Close studio nav"
            className={`studio-shell__scrim${isWorkspaceStudioNavOpen ? ' is-visible' : ''}`}
            onClick={() => handleToggleWorkspaceStudioNav(false)}
            tabIndex={isWorkspaceStudioNavOpen ? 0 : -1}
            type="button"
          />
        ) : null}

        <div className={`studio-main${isWorkspaceSection ? ' studio-main--workspace' : ''}`}>
          <header className={`masthead masthead--studio${isWorkspaceFocusMode ? ' masthead--workspace-focus' : ''}`}>
            <div className="masthead__brand">
              <p className="eyebrow">{activeSectionMeta.eyebrow}</p>
              <div className="masthead__title">
                <h2>{activeSectionMeta.label}</h2>
                <p className="masthead__lede">{activeSectionMeta.description}</p>
              </div>
            </div>
            <div className="masthead__meta">
              <div className="masthead__status">
                <div className="status-chip">
                  <span className="status-chip__label">Deck status</span>
                  <strong title={statusMessage}>{summarizeStatusMessage(statusMessage)}</strong>
                </div>
                <div className="status-chip">
                  <span className="status-chip__label">Live sessions</span>
                  <strong>{sessions.length}</strong>
                </div>
                <div className="status-chip">
                  <span className="status-chip__label">Workspace</span>
                  <strong>{formatWorkspaceLayout(workspace.layout)}</strong>
                </div>
              </div>
              <div className="header-actions">
                {isWorkspaceSection ? (
                  <button
                    className="button button--ghost"
                    data-testid="workspace-studio-nav-toggle"
                    onClick={() => handleToggleWorkspaceStudioNav(!isWorkspaceStudioNavOpen)}
                    type="button"
                  >
                    {isWorkspaceStudioNavOpen ? 'Hide studio nav' : 'Show studio nav'}
                  </button>
                ) : null}
                <button
                  className="button button--ghost"
                  data-testid="refresh-sessions"
                  onClick={() => void handleRefreshAction()}
                  type="button"
                >
                  {isRefreshing ? 'Refreshing…' : refreshActionLabel}
                </button>
                {showConfigActions ? (
                  <>
                    <button
                      className="button button--ghost"
                      data-testid="discard-config"
                      onClick={handleResetDraft}
                      disabled={!configDirty}
                      type="button"
                    >
                      Discard edits
                    </button>
                    <button
                      className="button button--solid"
                      data-testid="save-config"
                      onClick={handleSaveConfig}
                      disabled={isSavingConfig || !configDirty}
                      type="button"
                    >
                      {isSavingConfig ? 'Saving…' : 'Save config'}
                    </button>
                  </>
                ) : null}
              </div>
            </div>
          </header>

          {errorMessage ? (
            <div className="alert" role="alert">
              <strong>Heads up:</strong> {errorMessage}
            </div>
          ) : null}

          <main
            className={`product-main${isWorkspaceSection ? ' product-main--workspace' : ''}${isWorkspaceFocusMode ? ' product-main--workspace-focus' : ''}`}
            id="main-content"
          >
            {currentPage}
          </main>
        </div>
      </div>
      <datalist id="permission-mode-options">
        {permissionModes.map((mode) => (
          <option key={mode} value={mode} />
        ))}
      </datalist>
    </>
  )
}

function MetricStrip({ items }: { items: MetricItem[] }) {
  return (
    <div className="metric-strip">
      {items.map((item) => (
        <article className="metric-tile" key={item.label}>
          <span className="metric-tile__label">{item.label}</span>
          <strong>{item.value}</strong>
          {item.detail ? <p>{item.detail}</p> : null}
        </article>
      ))}
    </div>
  )
}

function ProductNote({ body, title }: { body: string; title: string }) {
  return (
    <section className="panel product-note">
      <div className="panel__header">
        <div>
          <p className="eyebrow">Operator note</p>
          <h2>{title}</h2>
        </div>
      </div>
      <p>{body}</p>
    </section>
  )
}

interface OverviewPageProps {
  claudeHome: ClaudeHomeSummary | null
  compactWorkspace: boolean
  config: AppConfig
  inventoryMetrics: MetricItem[]
  onOpenSection: (section: StudioSection) => void
  onOpenSession: (session: SessionSummary, target?: 'primary' | 'secondary') => void
  sessionMetrics: MetricItem[]
  sessions: SessionSummary[]
  statusMessage: string
  workspace: { layout: WorkspaceLayout }
}

function OverviewPage({
  claudeHome,
  compactWorkspace,
  config,
  inventoryMetrics,
  onOpenSection,
  onOpenSession,
  sessionMetrics,
  sessions,
  statusMessage,
  workspace,
}: OverviewPageProps) {
  const latestSessions = sessions.slice(0, 4)

  return (
    <div className="product-page product-page--overview" data-testid="product-page-overview">
      <section className="page-stack">
        <div className="page-intro">
          <div>
            <p className="eyebrow">Studio snapshot</p>
            <h2>One shell, multiple operating surfaces</h2>
            <p>
              The terminal is now one part of a broader product: runtime inventory, configuration, experiments,
              and future definitions all live beside it.
            </p>
          </div>
          <div className="inline-actions">
            <button className="button button--solid" onClick={() => onOpenSection('workspace')} type="button">
              Open workspace
            </button>
            <button className="button button--ghost" onClick={() => onOpenSection('sessions')} type="button">
              Review sessions
            </button>
          </div>
        </div>

        <MetricStrip items={[...sessionMetrics, ...inventoryMetrics]} />

        <div className="overview-grid">
          <section className="panel panel--overview-pulse">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Operational pulse</p>
                <h2>What matters right now</h2>
              </div>
            </div>
            <div className="summary-list">
              <article>
                <span>Deck status</span>
                <strong title={statusMessage}>{statusMessage}</strong>
              </article>
              <article>
                <span>Workspace shape</span>
                <strong>{formatWorkspaceLayout(workspace.layout)}</strong>
              </article>
              <article>
                <span>Runtime policy</span>
                <strong>{config.connectors.some((connector) => connector.skipPermissions) ? 'Needs review' : 'Standardized'}</strong>
              </article>
              <article>
                <span>Claude home</span>
                <strong>
                  {claudeHome?.exists
                    ? `${claudeHome.agentCount} agents / ${claudeHome.skillCount} skills`
                    : 'Not detected'}
                </strong>
              </article>
            </div>
          </section>

          <section className="panel panel--overview-surfaces">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Product surfaces</p>
                <h2>Jump directly into the right tool</h2>
              </div>
            </div>
            <div className="quick-links">
              {(['backplanes', 'hosts', 'connectors', 'lab', 'agents', 'skills', 'mcp', 'settings'] as StudioSection[]).map(
                (section) => (
                  <button
                    key={section}
                    className="quick-link"
                    onClick={() => onOpenSection(section)}
                    type="button"
                  >
                    <span className="quick-link__eyebrow">{studioSectionMeta[section].eyebrow}</span>
                    <strong>{studioSectionMeta[section].label}</strong>
                    <p>{studioSectionMeta[section].description}</p>
                  </button>
                ),
              )}
            </div>
          </section>

          <section className="panel overview-grid__wide">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Recent session fleet</p>
                <h2>Recover context fast</h2>
              </div>
            </div>
            {latestSessions.length > 0 ? (
              <div className="overview-session-list">
                {latestSessions.map((session) => (
                  <article className="overview-session-item" key={session.id}>
                    <div>
                      <span className={`session-state session-state--${session.state.toLowerCase()}`}>{session.state}</span>
                      <strong title={session.id}>{formatWorkspaceLabel(session.id)}</strong>
                      <p>{session.displayCommand}</p>
                    </div>
                    <div className="inline-actions">
                      <button className="button button--ghost" onClick={() => onOpenSession(session)} type="button">
                        Open
                      </button>
                      <button
                        className="button button--ghost"
                        onClick={() => onOpenSession(session, 'secondary')}
                        type="button"
                        disabled={compactWorkspace}
                        title={compactWorkspace ? 'Compare is unavailable on compact viewports.' : undefined}
                      >
                        Compare
                      </button>
                    </div>
                  </article>
                ))}
              </div>
            ) : (
              <div className="empty-callout">
                <p className="eyebrow">No live sessions yet</p>
                <p>Start in Workspace to launch the first terminal, then this overview becomes your operating pulse.</p>
              </div>
            )}
          </section>
        </div>
      </section>
    </div>
  )
}

interface WorkspacePageProps {
  activeSession: SessionSummary | null
  activeTab: WorkspaceTab | null
  availableHosts: HostConfig[]
  canSplitWorkspace: boolean
  claudeCatalog: ClaudeHomeCatalogResponse | null
  claudeHome: ClaudeHomeSummary | null
  compactWorkspace: boolean
  configDraft: AppConfig
  enabledBackplanes: BackplaneDefinition[]
  enabledConnectors: ConnectorDefinition[]
  isCreatingSession: boolean
  isLaunchBladeOpen: boolean
  isSavingConfig: boolean
  isStoppingSession: boolean
  launchDraft: LaunchDraft
  onCloseWorkspaceTab: (tabId: string) => void
  onLaunchSession: (event: FormEvent<HTMLFormElement>) => void
  onOpenOrchestration: () => void
  onOpenSession: (session: SessionSummary, target?: 'primary' | 'secondary') => void
  onResetDraft: () => void
  onSaveConfig: () => void
  onSelectPaneTab: (target: 'primary' | 'secondary', tabId: string) => void
  onSessionMessage: (sessionId: string, message: TerminalServerMessage) => void
  onSetWorkspaceLayout: (layout: WorkspaceLayout) => void
  onStopSession: (sessionId: string) => void
  onToggleLaunchBlade: (open: boolean) => void
  onUpdateConfigDraft: (updater: (current: AppConfig) => AppConfig) => void
  onUpdateLaunchDraft: <K extends keyof LaunchDraft>(key: K, value: LaunchDraft[K]) => void
  secondarySession: SessionSummary | null
  secondaryTab: WorkspaceTab | null
  selectedConnector: ConnectorDefinition | null
  sessionMetrics: MetricItem[]
  sessions: SessionSummary[]
  themeMode: ThemeMode
  workspace: {
    activeTabId: string
    layout: WorkspaceLayout
    tabs: WorkspaceTab[]
  }
  workspaceTabOptions: Array<{ label: string; tabId: string }>
}

function WorkspacePage({
  activeSession,
  activeTab,
  availableHosts,
  canSplitWorkspace,
  claudeCatalog,
  claudeHome,
  compactWorkspace,
  configDraft,
  enabledBackplanes,
  enabledConnectors,
  isCreatingSession,
  isLaunchBladeOpen,
  isSavingConfig,
  isStoppingSession,
  launchDraft,
  onCloseWorkspaceTab,
  onLaunchSession,
  onOpenOrchestration,
  onOpenSession,
  onResetDraft,
  onSaveConfig,
  onSelectPaneTab,
  onSessionMessage,
  onSetWorkspaceLayout,
  onStopSession,
  onToggleLaunchBlade,
  onUpdateConfigDraft,
  onUpdateLaunchDraft,
  secondarySession,
  secondaryTab,
  selectedConnector,
  sessionMetrics,
  sessions,
  themeMode,
  workspace,
  workspaceTabOptions,
}: WorkspacePageProps) {
  return (
    <div className="product-page product-page--workspace" data-testid="product-page-workspace">
      <section className="page-stack page-stack--workspace">
        <div className="page-intro page-intro--workspace">
          <div>
            <p className="eyebrow">Command studio</p>
            <h2>Terminal work stays first-class</h2>
            <p>
              Launch, compare, and monitor terminals here while the rest of the product grows around it.
            </p>
          </div>
          <MetricStrip items={sessionMetrics} />
        </div>

        <div
          className={`workspace workspace--product${activeSession ? ' workspace--active' : ''}${isLaunchBladeOpen ? ' workspace--blade-open' : ''}`}
        >
          <section className={`stage${activeSession ? ' stage--active' : ' stage--idle'}`}>
            <div className="stage__header">
              <div>
                <p className="eyebrow">Workspace canvas</p>
                <h2>Session stage</h2>
                <p className="stage__lede">
                  Tabs manage long-lived sessions, while split panes handle compare and monitor flows without breaking terminal fidelity.
                </p>
                <p className="stage__context">
                  Active: {formatWorkspaceLabel(activeTab?.title ?? 'Orchestration')}
                  {secondaryTab ? ` · Compare: ${formatWorkspaceLabel(secondaryTab.title)}` : ''}
                </p>
              </div>

              <div className="stage__actions">
                <button
                  className="button button--ghost"
                  data-testid="workspace-launch-blade-toggle"
                  onClick={() => onToggleLaunchBlade(!isLaunchBladeOpen)}
                  type="button"
                >
                  {isLaunchBladeOpen ? 'Hide launch blade' : 'New session'}
                </button>
                <button className="button button--ghost" onClick={onOpenOrchestration} type="button">
                  Orchestration
                </button>
                <button
                  className="button button--ghost"
                  data-testid="split-vertical"
                  onClick={() => onSetWorkspaceLayout('split-vertical')}
                  disabled={!canSplitWorkspace}
                  type="button"
                  title={compactWorkspace ? 'Compare panes are available on larger viewports.' : undefined}
                >
                  Split vertical
                </button>
                <button
                  className="button button--ghost"
                  data-testid="split-horizontal"
                  onClick={() => onSetWorkspaceLayout('split-horizontal')}
                  disabled={!canSplitWorkspace}
                  type="button"
                  title={compactWorkspace ? 'Compare panes are available on larger viewports.' : undefined}
                >
                  Split horizontal
                </button>
                <button
                  className="button button--ghost"
                  data-testid="single-pane"
                  onClick={() => onSetWorkspaceLayout('single')}
                  disabled={workspace.layout === 'single'}
                  type="button"
                >
                  Single pane
                </button>
              </div>
            </div>

            <div className="stage__body stage__body--workspace">
              <div className="workspace-tabs" role="tablist" aria-label="Workspace tabs">
                {workspace.tabs.map((tab) => (
                  <div
                    key={tab.id}
                    className={`workspace-tab${tab.id === workspace.activeTabId ? ' workspace-tab--active' : ''}`}
                  >
                    <button
                      className="workspace-tab__trigger"
                      data-testid={`workspace-tab-${tab.id}`}
                      onClick={() => onSelectPaneTab('primary', tab.id)}
                      role="tab"
                      aria-selected={tab.id === workspace.activeTabId}
                      type="button"
                    >
                      <span className="workspace-tab__title" title={tab.title}>
                        {formatWorkspaceLabel(tab.title)}
                      </span>
                      <span className="workspace-tab__kind">{tab.kind === 'orchestration' ? 'board' : 'session'}</span>
                    </button>
                    {tab.closable ? (
                      <button
                        className="workspace-tab__close"
                        data-testid={`workspace-tab-close-${tab.id}`}
                        onClick={() => onCloseWorkspaceTab(tab.id)}
                        aria-label={`Close ${tab.title}`}
                        type="button"
                      >
                        ×
                      </button>
                    ) : null}
                  </div>
                ))}
              </div>

              <div className={`workspace-canvas workspace-canvas--${workspace.layout}`}>
                <WorkspacePaneView
                  label="Primary"
                  session={activeSession}
                  sessions={sessions}
                  tab={activeTab}
                  tabOptions={workspaceTabOptions}
                  target="primary"
                  themeMode={themeMode}
                  isStoppingSession={isStoppingSession}
                  onOpenSession={onOpenSession}
                  onSelectTab={onSelectPaneTab}
                  onSessionMessage={onSessionMessage}
                  onStopSession={onStopSession}
                  allowCompare={!compactWorkspace}
                />

                {workspace.layout !== 'single' && secondaryTab ? (
                  <WorkspacePaneView
                    label="Compare"
                    session={secondarySession}
                    sessions={sessions}
                    tab={secondaryTab}
                    tabOptions={workspaceTabOptions}
                    target="secondary"
                    themeMode={themeMode}
                    isStoppingSession={isStoppingSession}
                    onOpenSession={onOpenSession}
                    onSelectTab={onSelectPaneTab}
                    onSessionMessage={onSessionMessage}
                    onStopSession={onStopSession}
                    allowCompare={!compactWorkspace}
                  />
                ) : null}
              </div>
            </div>
          </section>

          <button
            className={`workspace__scrim${isLaunchBladeOpen ? ' is-visible' : ''}`}
            aria-label="Close launch blade"
            aria-hidden={!isLaunchBladeOpen}
            onClick={() => onToggleLaunchBlade(false)}
            tabIndex={isLaunchBladeOpen ? 0 : -1}
            type="button"
          />

          <aside
            className={`rail-blade${isLaunchBladeOpen ? ' rail-blade--open' : ''}`}
            aria-hidden={!isLaunchBladeOpen}
            data-testid="workspace-launch-blade"
          >
            <div className="rail">
              <LaunchPanel
                availableHosts={availableHosts}
                claudeCatalog={claudeCatalog}
                claudeHome={claudeHome}
                enabledBackplanes={enabledBackplanes}
                enabledConnectors={enabledConnectors}
                isCreatingSession={isCreatingSession}
                key={selectedConnector?.id ?? 'launch-panel'}
                launchDraft={launchDraft}
                onClose={() => onToggleLaunchBlade(false)}
                onLaunchSession={onLaunchSession}
                onUpdateLaunchDraft={onUpdateLaunchDraft}
                selectedConnector={selectedConnector}
              />

              <QuickConfigPanel
                configDraft={configDraft}
                isSavingConfig={isSavingConfig}
                onAddBackplane={() =>
                  onUpdateConfigDraft((current) => ({
                    ...current,
                    backplanes: [...current.backplanes, createBackplaneDefinition()],
                  }))
                }
                onAddConnector={() =>
                  onUpdateConfigDraft((current) => ({
                    ...current,
                    connectors: [...current.connectors, createConnectorDefinition()],
                  }))
                }
                onAddHost={() =>
                  onUpdateConfigDraft((current) => ({
                    ...current,
                    hosts: [
                      ...current.hosts,
                      createHostConfig(current.backplanes[0]?.id ?? current.hosts[0]?.backplaneId ?? 'local'),
                    ],
                  }))
                }
                onBackplaneChange={(index, nextValue) =>
                  onUpdateConfigDraft((current) => ({
                    ...current,
                    backplanes: updateItem(current.backplanes, index, nextValue),
                  }))
                }
                onBackplaneRemove={(index) =>
                  onUpdateConfigDraft((current) => ({
                    ...current,
                    backplanes: current.backplanes.filter((_, itemIndex) => itemIndex !== index),
                  }))
                }
                onConnectorChange={(index, nextValue) =>
                  onUpdateConfigDraft((current) => ({
                    ...current,
                    connectors: updateItem(current.connectors, index, nextValue),
                  }))
                }
                onConnectorRemove={(index) =>
                  onUpdateConfigDraft((current) => ({
                    ...current,
                    connectors: current.connectors.filter((_, itemIndex) => itemIndex !== index),
                  }))
                }
                onHostChange={(index, nextValue) =>
                  onUpdateConfigDraft((current) => ({
                    ...current,
                    hosts: updateItem(current.hosts, index, nextValue),
                  }))
                }
                onHostRemove={(index) =>
                  onUpdateConfigDraft((current) => ({
                    ...current,
                    hosts: current.hosts.filter((_, itemIndex) => itemIndex !== index),
                  }))
                }
                onResetDraft={onResetDraft}
                onSaveConfig={onSaveConfig}
              />

              <SessionManifestPanel
                activeSessionId={activeSession?.id ?? null}
                onOpenSession={onOpenSession}
                sessions={sessions}
              />
            </div>
          </aside>
        </div>
      </section>
    </div>
  )
}

interface LaunchPanelProps {
  availableHosts: HostConfig[]
  claudeCatalog: ClaudeHomeCatalogResponse | null
  claudeHome: ClaudeHomeSummary | null
  enabledBackplanes: BackplaneDefinition[]
  enabledConnectors: ConnectorDefinition[]
  isCreatingSession: boolean
  launchDraft: LaunchDraft
  onClose: () => void
  onLaunchSession: (event: FormEvent<HTMLFormElement>) => void
  onUpdateLaunchDraft: <K extends keyof LaunchDraft>(key: K, value: LaunchDraft[K]) => void
  selectedConnector: ConnectorDefinition | null
}

function LaunchPanel({
  availableHosts,
  claudeCatalog,
  claudeHome,
  enabledBackplanes,
  enabledConnectors,
  isCreatingSession,
  launchDraft,
  onClose,
  onLaunchSession,
  onUpdateLaunchDraft,
  selectedConnector,
}: LaunchPanelProps) {
  const [isAllowedToolsPickerOpen, setIsAllowedToolsPickerOpen] = useState(false)
  const [customAllowedToolDraft, setCustomAllowedToolDraft] = useState('')
  const selectedHost = availableHosts.find((host) => host.id === launchDraft.hostId) ?? null
  const launchCapabilities = getConnectorLaunchCapabilities(selectedConnector)
  const effectiveModel = launchDraft.model ?? selectedConnector?.defaultModel ?? ''
  const effectivePermissionMode = launchDraft.permissionMode ?? selectedConnector?.defaultPermissionMode ?? ''
  const effectiveSkipPermissions = launchDraft.skipPermissions ?? selectedConnector?.skipPermissions ?? false
  const effectiveAllowedTools = launchDraft.allowedTools ?? selectedConnector?.allowedTools ?? []
  const knownAllowedToolOptions = getKnownAllowedToolOptions(selectedConnector)
  const customAllowedTools = effectiveAllowedTools.filter(
    (tool) => !knownAllowedToolOptions.some((option) => sameLaunchValue(option, tool)),
  )
  const claudeAgents = [...(claudeCatalog?.agents ?? [])].sort((left, right) => left.name.localeCompare(right.name))
  const claudeAgentCount = claudeHome?.agentCount ?? 0
  const visibleWorkingDirectory = launchDraft.workingDirectory ?? selectedHost?.workingDirectory ?? ''
  const workspaceFolderHint = selectedHost?.workingDirectory
    ? `Host default: ${selectedHost.workingDirectory}. Clear edits to restore it.`
    : 'Uses the selected host default when available, otherwise the runtime process default.'
  const permissionModeHint = effectiveSkipPermissions
    ? 'Dangerously skip permissions is enabled, so Claude will bypass permission mode for this launch.'
    : launchDraft.permissionMode === null
      ? `Using connector default: ${selectedConnector?.defaultPermissionMode ?? 'Claude default'}.`
      : `Session override applied: ${formatPermissionMode(effectivePermissionMode)}.`
  const skipPermissionsHint =
    launchDraft.skipPermissions === null
      ? `Using connector default: ${selectedConnector?.skipPermissions ? 'enabled' : 'disabled'}.`
      : 'Session override applied for dangerous skip permissions.'
  const allowedToolsHint =
    launchDraft.allowedTools === null
      ? selectedConnector?.allowedTools.length
        ? `Using connector default: ${formatAllowedToolSummary(selectedConnector.allowedTools)}.`
        : 'No connector allowlist configured. Select tools here only when this session needs pre-approved actions.'
      : effectiveAllowedTools.length > 0
        ? `${effectiveAllowedTools.length} allow rule${effectiveAllowedTools.length === 1 ? '' : 's'} selected for this launch.`
        : 'This launch will prompt for tools unless permissions are bypassed.'
  const agentHint = !claudeHome?.exists
    ? 'No Claude home directory detected for agent discovery.'
    : claudeAgentCount === 0
      ? 'No Claude agents were discovered in ~/.claude/agents.'
      : claudeCatalog
        ? 'Apply a Claude agent at session start for specialized prompts and tool rules.'
        : 'Loading discovered Claude agents…'
  const hasExplicitAllowedTools = launchDraft.allowedTools !== null
  const allowedToolsToggleLabel = hasExplicitAllowedTools
    ? effectiveAllowedTools.length > 0
      ? `${effectiveAllowedTools.length} tool rule${effectiveAllowedTools.length === 1 ? '' : 's'} selected`
      : 'Choose allowed tools'
    : effectiveAllowedTools.length > 0
      ? 'Customize connector default tools'
      : 'Choose allowed tools'

  const handleAllowedToolToggle = (tool: string, checked: boolean) => {
    const remainder = effectiveAllowedTools.filter((candidate) => !sameLaunchValue(candidate, tool))
    const nextAllowedTools = checked ? dedupeLaunchValues([...remainder, tool]) : remainder
    onUpdateLaunchDraft('allowedTools', nextAllowedTools)
  }

  const handleCustomAllowedToolsAdd = () => {
    const nextCustomTools = parseArgumentList(customAllowedToolDraft)
    if (nextCustomTools.length === 0) {
      return
    }

    const nextAllowedTools = dedupeLaunchValues([...effectiveAllowedTools, ...nextCustomTools])
    onUpdateLaunchDraft('allowedTools', nextAllowedTools)
    setCustomAllowedToolDraft('')
    setIsAllowedToolsPickerOpen(true)
  }

  const handleAllowedToolRemove = (tool: string) => {
    const nextAllowedTools = effectiveAllowedTools.filter((candidate) => !sameLaunchValue(candidate, tool))
    onUpdateLaunchDraft('allowedTools', nextAllowedTools)
  }

  return (
    <section className="panel panel--launch">
      <div className="panel__header">
        <div>
          <p className="eyebrow">Session launch</p>
          <h2>New session</h2>
        </div>
        <div className="panel__header-actions">
          <button
            className="button button--solid button--compact"
            data-testid="launch-session"
            disabled={isCreatingSession}
            form="launch-form"
            type="submit"
          >
            {isCreatingSession ? 'Starting…' : 'Start session'}
          </button>
          <button className="button button--ghost button--compact" onClick={onClose} type="button">
            Hide
          </button>
        </div>
      </div>

      <form className="launch-form" data-testid="launch-form" id="launch-form" onSubmit={onLaunchSession}>
        <label className="field">
          <span>Backplane</span>
          <select
            data-testid="launch-backplane"
            value={launchDraft.backplaneId}
            onChange={(event) => onUpdateLaunchDraft('backplaneId', event.target.value)}
          >
            {enabledBackplanes.map((backplane) => (
              <option key={backplane.id} value={backplane.id}>
                {backplane.displayName}
              </option>
            ))}
          </select>
        </label>

        <label className="field">
          <span>Host</span>
          <select
            data-testid="launch-host"
            value={launchDraft.hostId}
            onChange={(event) => onUpdateLaunchDraft('hostId', event.target.value)}
          >
            {availableHosts.map((host) => (
              <option key={host.id} value={host.id}>
                {host.displayName}
              </option>
            ))}
          </select>
        </label>

        <label className="field">
          <span>Connector</span>
          <select
            data-testid="launch-connector"
            value={launchDraft.connectorId}
            onChange={(event) => onUpdateLaunchDraft('connectorId', event.target.value)}
          >
            {enabledConnectors.map((connector) => (
              <option key={connector.id} value={connector.id}>
                {connector.displayName}
              </option>
            ))}
          </select>
        </label>

        {selectedConnector ? (
          <>
            <div className="field field--hint">
              <span>Launch target</span>
              <p data-testid="launch-connector-command">{formatConnectorLaunchTarget(selectedConnector)}</p>
            </div>

            {launchCapabilities.supportsModel ||
            launchCapabilities.supportsPermissionMode ||
            launchCapabilities.supportsAllowedTools ||
            launchCapabilities.supportsSkipPermissions ||
            launchCapabilities.supportsAgent ? (
              <div className="launch-overrides" data-testid="launch-overrides">
                {launchCapabilities.supportsModel ? (
                  <label className="field">
                    <span>Model override</span>
                    <input
                      data-testid="launch-model"
                      placeholder={selectedConnector.defaultModel ?? 'Use connector default'}
                      value={launchDraft.model ?? ''}
                      onChange={(event) => onUpdateLaunchDraft('model', event.target.value.trim() || null)}
                    />
                    <p className="field-note">
                      {launchDraft.model === null
                        ? `Using connector default: ${selectedConnector.defaultModel ?? 'provider default'}.`
                        : `Session override applied: ${effectiveModel}.`}
                    </p>
                  </label>
                ) : null}

                {launchCapabilities.supportsPermissionMode ? (
                  <label className="field">
                    <span>Permission mode</span>
                    <select
                      data-testid="launch-permission-mode"
                      value={launchDraft.permissionMode ?? ''}
                      disabled={effectiveSkipPermissions}
                      onChange={(event) => onUpdateLaunchDraft('permissionMode', event.target.value || null)}
                    >
                      <option value="">Use connector default</option>
                      {permissionModes.map((mode) => (
                        <option key={mode} value={mode}>
                          {formatPermissionMode(mode)}
                        </option>
                      ))}
                    </select>
                    <p className="field-note">{permissionModeHint}</p>
                  </label>
                ) : null}

                {launchCapabilities.supportsAgent ? (
                  <label className="field">
                    <span>Agent</span>
                    <select
                      data-testid="launch-agent"
                      value={launchDraft.agent ?? ''}
                      disabled={Boolean(claudeHome?.exists) && claudeAgentCount > 0 && !claudeCatalog}
                      onChange={(event) => onUpdateLaunchDraft('agent', event.target.value || null)}
                    >
                      <option value="">No agent</option>
                      {claudeAgents.map((agent) => (
                        <option key={agent.name} value={agent.name}>
                          {agent.name}
                        </option>
                      ))}
                    </select>
                    <p className="field-note">{agentHint}</p>
                  </label>
                ) : null}

                {launchCapabilities.supportsSkipPermissions ? (
                  <div className="field field--toggle-card">
                    <div className="field__header">
                      <span>Dangerously skip permissions</span>
                      <button
                        className="button button--ghost button--inline"
                        disabled={launchDraft.skipPermissions === null}
                        onClick={() => onUpdateLaunchDraft('skipPermissions', null)}
                        type="button"
                      >
                        Use connector default
                      </button>
                    </div>
                    <label className="toggle toggle--card">
                      <input
                        data-testid="launch-skip-permissions"
                        type="checkbox"
                        checked={effectiveSkipPermissions}
                        onChange={(event) => onUpdateLaunchDraft('skipPermissions', event.target.checked)}
                      />
                      <span>Let Claude bypass permission prompts for this session.</span>
                    </label>
                    <p className="field-note">{skipPermissionsHint}</p>
                  </div>
                ) : null}

                {launchCapabilities.supportsAllowedTools ? (
                  <div className="field field--tool-palette">
                    <div className="field__header">
                      <span>Allowed tools</span>
                      <button
                        className="button button--ghost button--inline"
                        disabled={launchDraft.allowedTools === null}
                        onClick={() => onUpdateLaunchDraft('allowedTools', null)}
                        type="button"
                        >
                          Use connector default
                        </button>
                      </div>
                    <div className="tool-picker">
                      <button
                        aria-controls="launch-allowed-tools-menu"
                        aria-expanded={isAllowedToolsPickerOpen}
                        className={`tool-picker__toggle${isAllowedToolsPickerOpen ? ' is-open' : ''}`}
                        data-testid="launch-allowed-tools-toggle"
                        onClick={() => setIsAllowedToolsPickerOpen((current) => !current)}
                        type="button"
                      >
                        <span>{allowedToolsToggleLabel}</span>
                        <strong aria-hidden="true">{isAllowedToolsPickerOpen ? '−' : '+'}</strong>
                      </button>
                      <div className="tool-picker__chips" data-testid="launch-allowed-tools-selected">
                        {effectiveAllowedTools.length > 0 ? (
                          effectiveAllowedTools.map((tool) => (
                            <span
                              className={`selection-chip${hasExplicitAllowedTools ? '' : ' selection-chip--readonly'}`}
                              key={tool}
                            >
                              <span>{tool}</span>
                              {hasExplicitAllowedTools ? (
                                <button
                                  aria-label={`Remove ${tool}`}
                                  className="selection-chip__remove"
                                  onClick={() => handleAllowedToolRemove(tool)}
                                  type="button"
                                >
                                  ×
                                </button>
                              ) : null}
                            </span>
                          ))
                        ) : (
                          <p className="tool-picker__empty">No tool rules selected for this session yet.</p>
                        )}
                      </div>
                      {isAllowedToolsPickerOpen ? (
                        <div className="tool-picker__menu" data-testid="launch-allowed-tools" id="launch-allowed-tools-menu">
                          <div className="tool-picker__options">
                            {knownAllowedToolOptions.map((tool) => {
                              const isSelected = effectiveAllowedTools.some((candidate) => sameLaunchValue(candidate, tool))
                              return (
                                <label className={`tool-option${isSelected ? ' is-selected' : ''}`} key={tool}>
                                  <input
                                    checked={isSelected}
                                    onChange={(event) => handleAllowedToolToggle(tool, event.target.checked)}
                                    type="checkbox"
                                  />
                                  <span>{tool}</span>
                                </label>
                              )
                            })}
                          </div>
                          <label className="field field--nested">
                            <span>Add custom allow rule</span>
                            <div className="tool-picker__custom-row">
                              <input
                                data-testid="launch-custom-tools"
                                placeholder="Bash(git diff *), Read"
                                value={customAllowedToolDraft}
                                onChange={(event) => setCustomAllowedToolDraft(event.target.value)}
                                onKeyDown={(event) => {
                                  if (event.key === 'Enter') {
                                    event.preventDefault()
                                    handleCustomAllowedToolsAdd()
                                  }
                                }}
                              />
                              <button
                                className="button button--ghost button--compact"
                                disabled={!customAllowedToolDraft.trim()}
                                onClick={handleCustomAllowedToolsAdd}
                                type="button"
                              >
                                Add
                              </button>
                            </div>
                          </label>
                          {customAllowedTools.length > 0 ? (
                            <p className="field-note">Custom rules are shown as chips and launch with this session override.</p>
                          ) : null}
                        </div>
                      ) : null}
                    </div>
                    <p className="field-note">{allowedToolsHint}</p>
                  </div>
                ) : null}
              </div>
            ) : (
              <p className="field-note">Uses the selected host shell.</p>
            )}
          </>
        ) : null}

        <label className="field">
          <span>Workspace folder</span>
          <input
            data-testid="launch-working-directory"
            placeholder="Use host default"
            value={visibleWorkingDirectory}
            onChange={(event) => onUpdateLaunchDraft('workingDirectory', event.target.value || null)}
          />
          <p className="field-note">{workspaceFolderHint}</p>
        </label>

        <div className="dimension-grid">
          <label className="field">
            <span>Columns</span>
            <input
              data-testid="launch-cols"
              type="number"
              min={24}
              max={240}
              value={launchDraft.cols}
              onChange={(event) => onUpdateLaunchDraft('cols', Number(event.target.value))}
            />
          </label>

          <label className="field">
            <span>Rows</span>
            <input
              data-testid="launch-rows"
              type="number"
              min={24}
              max={240}
              value={launchDraft.rows}
              onChange={(event) => onUpdateLaunchDraft('rows', Number(event.target.value))}
            />
          </label>
        </div>
        <p className="field-note launch-form__footnote">This session opens directly into the workspace stage once the runtime starts.</p>
      </form>
    </section>
  )
}

interface QuickConfigPanelProps {
  configDraft: AppConfig
  isSavingConfig: boolean
  onAddBackplane: () => void
  onAddConnector: () => void
  onAddHost: () => void
  onBackplaneChange: (index: number, nextValue: BackplaneDefinition) => void
  onBackplaneRemove: (index: number) => void
  onConnectorChange: (index: number, nextValue: ConnectorDefinition) => void
  onConnectorRemove: (index: number) => void
  onHostChange: (index: number, nextValue: HostConfig) => void
  onHostRemove: (index: number) => void
  onResetDraft: () => void
  onSaveConfig: () => void
}

function QuickConfigPanel({
  configDraft,
  isSavingConfig,
  onAddBackplane,
  onAddConnector,
  onAddHost,
  onBackplaneChange,
  onBackplaneRemove,
  onConnectorChange,
  onConnectorRemove,
  onHostChange,
  onHostRemove,
  onResetDraft,
  onSaveConfig,
}: QuickConfigPanelProps) {
  return (
    <details className="panel panel--settings" data-testid="config-panel">
      <summary data-testid="config-panel-toggle">
        <div>
          <p className="eyebrow">Configuration manifest</p>
          <h2>Runtime config</h2>
        </div>
      </summary>
      <div className="panel--settings__body">
        <div className="inline-actions inline-actions--compact">
          <button className="button button--ghost" onClick={onResetDraft} type="button">
            Reset draft
          </button>
          <button className="button button--ghost" onClick={onSaveConfig} type="button">
            {isSavingConfig ? 'Saving…' : 'Save from workspace'}
          </button>
        </div>
        <ConfigBlock
          title="Backplanes"
          description="Execution fabrics available to new sessions."
          actionLabel="Add backplane"
          onAdd={onAddBackplane}
        >
          {configDraft.backplanes.map((backplane, index) => (
            <BackplaneEditor
              key={backplane.id}
              backplane={backplane}
              onChange={(nextValue) => onBackplaneChange(index, nextValue)}
              onRemove={() => onBackplaneRemove(index)}
            />
          ))}
        </ConfigBlock>

        <ConfigBlock
          title="Hosts"
          description="Concrete local or container targets exposed by each backplane."
          actionLabel="Add host"
          onAdd={onAddHost}
        >
          {configDraft.hosts.map((host, index) => (
            <HostEditor
              key={host.id}
              host={host}
              backplanes={configDraft.backplanes}
              onChange={(nextValue) => onHostChange(index, nextValue)}
              onRemove={() => onHostRemove(index)}
            />
          ))}
        </ConfigBlock>

        <ConfigBlock
          title="Connectors"
          description="CLI adapters mapped into each session."
          actionLabel="Add connector"
          onAdd={onAddConnector}
        >
          {configDraft.connectors.map((connector, index) => (
            <ConnectorEditor
              key={connector.id}
              connector={connector}
              onChange={(nextValue) => onConnectorChange(index, nextValue)}
              onRemove={() => onConnectorRemove(index)}
            />
          ))}
        </ConfigBlock>
      </div>
    </details>
  )
}

function SessionManifestPanel({
  activeSessionId,
  onOpenSession,
  sessions,
}: {
  activeSessionId: string | null
  onOpenSession: (session: SessionSummary, target?: 'primary' | 'secondary') => void
  sessions: SessionSummary[]
}) {
  return (
    <section className="panel panel--manifest">
      <div className="panel__header">
        <div>
          <p className="eyebrow">Session manifest</p>
          <h2>Sessions</h2>
        </div>
      </div>

      {sessions.length > 0 ? (
        <div className="session-list" role="tablist" aria-label="Sessions">
          {sessions.map((session) => (
            <button
              key={session.id}
              data-testid={`session-card-${session.id}`}
              className={`session-card${activeSessionId === session.id ? ' is-active' : ''}`}
              onClick={() => onOpenSession(session)}
              role="tab"
              aria-selected={activeSessionId === session.id}
              type="button"
            >
              <span className={`session-state session-state--${session.state.toLowerCase()}`}>{session.state}</span>
              <strong title={session.id}>{formatWorkspaceLabel(session.id)}</strong>
              <span>{session.displayCommand}</span>
            </button>
          ))}
        </div>
      ) : (
        <div className="empty-callout">
          <p className="eyebrow">No live sessions yet</p>
          <p>
            Launch a shell or Ollama run, then switch tabs freely. Sessions survive WebSocket disconnects and reattach when you come back.
          </p>
        </div>
      )}
    </section>
  )
}

function SessionsPage({
  compactWorkspace,
  onOpenSection,
  onOpenSession,
  sessionMetrics,
  sessions,
}: {
  compactWorkspace: boolean
  onOpenSection: (section: StudioSection) => void
  onOpenSession: (session: SessionSummary, target?: 'primary' | 'secondary') => void
  sessionMetrics: MetricItem[]
  sessions: SessionSummary[]
}) {
  return (
    <div className="product-page product-page--sessions" data-testid="product-page-sessions">
      <section className="page-stack">
        <div className="page-intro">
          <div>
            <p className="eyebrow">Session fleet</p>
            <h2>Browse the active runtime manifest</h2>
            <p>Use this page as the operational ledger, then jump into Workspace only when you need the live terminal.</p>
          </div>
          <div className="inline-actions">
            <button className="button button--solid" onClick={() => onOpenSection('workspace')} type="button">
              Go to workspace
            </button>
          </div>
        </div>
        <MetricStrip items={sessionMetrics} />
        {sessions.length > 0 ? (
          <div className="catalog-grid">
            {sessions.map((session) => (
              <article className="catalog-card" key={session.id}>
                <span className={`session-state session-state--${session.state.toLowerCase()}`}>{session.state}</span>
                <strong title={session.id}>{formatWorkspaceLabel(session.id)}</strong>
                <p>{session.displayCommand}</p>
                <div className="summary-list summary-list--compact">
                  <article>
                    <span>Backplane</span>
                    <strong>{session.backplaneId}</strong>
                  </article>
                  <article>
                    <span>Host</span>
                    <strong>{session.hostId}</strong>
                  </article>
                  <article>
                    <span>Connector</span>
                    <strong>{session.connectorId}</strong>
                  </article>
                </div>
                <div className="inline-actions">
                  <button className="button button--ghost" onClick={() => onOpenSession(session)} type="button">
                    Open
                  </button>
                  <button
                    className="button button--ghost"
                    onClick={() => onOpenSession(session, 'secondary')}
                    type="button"
                    disabled={compactWorkspace}
                    title={compactWorkspace ? 'Compare is unavailable on compact viewports.' : undefined}
                  >
                    Compare
                  </button>
                </div>
              </article>
            ))}
          </div>
        ) : (
          <div className="empty-stage">
            <p className="eyebrow">No session fleet yet</p>
            <h3>Launch the first runtime from Workspace.</h3>
            <p>This page becomes your searchable manifest once live sessions exist.</p>
          </div>
        )}
      </section>
    </div>
  )
}

function LabPage({
  configDirty,
  experiments,
  experimentRuns,
  isRunningExperiment,
  metrics,
  onAddExperiment,
  onExperimentChange,
  onExperimentRemove,
  onOpenSection,
  onOpenSession,
  onRunExperiment,
  sessions,
  templates,
}: {
  configDirty: boolean
  experiments: ExperimentDefinition[]
  experimentRuns: ExperimentRunSummary[]
  isRunningExperiment: boolean
  metrics: MetricItem[]
  onAddExperiment: () => void
  onExperimentChange: (index: number, value: ExperimentDefinition) => void
  onExperimentRemove: (index: number) => void
  onOpenSection: (section: StudioSection) => void
  onOpenSession: (session: SessionSummary, target?: 'primary' | 'secondary') => void
  onRunExperiment: (experimentId: string) => void
  sessions: SessionSummary[]
  templates: BlueprintCard[]
}) {
  const sessionMap = new Map(sessions.map((session) => [session.id, session]))

  return (
    <InventoryPage
      eyebrow="Experiment lab"
      title="Experiment matrix and recent runs"
      description="Define reusable execution matrices, launch them as a batch, and route the resulting sessions back into the workspace with traceable context."
      metrics={metrics}
      aside={
        <>
          <section className="panel">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Recent run groups</p>
                <h2>Execution ledger</h2>
              </div>
            </div>
            {experimentRuns.length > 0 ? (
              <div className="summary-list">
                {experimentRuns.slice(0, 6).map((run) => (
                  <article key={run.id}>
                    <span>{run.experimentDisplayName}</span>
                    <strong>{run.activeSessionCount}/{run.variantCount} active</strong>
                    <small>{new Date(run.createdAt).toLocaleString()}</small>
                    <div className="inline-actions inline-actions--compact">
                      {run.variants.slice(0, 2).map((variant) => {
                        const session = sessionMap.get(variant.sessionId)
                        if (!session) {
                          return null
                        }

                        return (
                          <button
                            className="button button--ghost"
                            key={variant.sessionId}
                            onClick={() => onOpenSession(session)}
                            type="button"
                          >
                            Open {formatWorkspaceLabel(session.id)}
                          </button>
                        )
                      })}
                    </div>
                  </article>
                ))}
              </div>
            ) : (
              <div className="empty-callout">
                <p className="eyebrow">No run groups yet</p>
                <p>Launch a saved experiment to create the first structured execution batch.</p>
              </div>
            )}
          </section>

          <section className="panel">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Starter patterns</p>
                <h2>What to model next</h2>
              </div>
            </div>
            <div className="summary-list">
              {templates.slice(0, 3).map((template) => (
                <article key={template.title}>
                  <span>{template.eyebrow}</span>
                  <strong>{template.title}</strong>
                  <p>{template.description}</p>
                </article>
              ))}
            </div>
            <div className="inline-actions">
              <button className="button button--ghost" onClick={() => onOpenSection('workspace')} type="button">
                Open workspace
              </button>
              <button className="button button--ghost" onClick={() => onOpenSection('connectors')} type="button">
                Tune connectors
              </button>
            </div>
          </section>
        </>
      }
    >
      <ConfigBlock
        title="Experiment definitions"
        description="Saved execution matrices that can be launched repeatedly across hosts, connectors, and model variants."
        actionLabel="Add experiment"
        onAdd={onAddExperiment}
      >
        {experiments.map((experiment, index) => (
          <ExperimentEditor
            configDirty={configDirty}
            experiment={experiment}
            isRunningExperiment={isRunningExperiment}
            key={experiment.id}
            onChange={(nextValue) => onExperimentChange(index, nextValue)}
            onRemove={() => onExperimentRemove(index)}
            onRun={() => onRunExperiment(experiment.id)}
          />
        ))}
      </ConfigBlock>
    </InventoryPage>
  )
}

interface InventoryPageProps {
  aside?: ReactNode
  children: ReactNode
  description: string
  eyebrow: string
  metrics: MetricItem[]
  title: string
}

function InventoryPage({ aside, children, description, eyebrow, metrics, title }: InventoryPageProps) {
  return (
    <div className="product-page product-page--inventory">
      <section className="page-stack">
        <div className="page-intro">
          <div>
            <p className="eyebrow">{eyebrow}</p>
            <h2>{title}</h2>
            <p>{description}</p>
          </div>
        </div>
        <MetricStrip items={metrics} />
        <div className="inventory-grid">
          <section className="inventory-grid__main">{children}</section>
          <aside className="inventory-grid__aside">{aside}</aside>
        </div>
      </section>
    </div>
  )
}

function BlueprintPage({
  cards,
  description,
  eyebrow,
  metrics,
  quickLinks,
  title,
}: {
  cards: BlueprintCard[]
  description: string
  eyebrow: string
  metrics: MetricItem[]
  quickLinks: Array<{ href: string; label: string }>
  title: string
}) {
  return (
    <div className="product-page product-page--blueprint">
      <section className="page-stack">
        <div className="page-intro">
          <div>
            <p className="eyebrow">{eyebrow}</p>
            <h2>{title}</h2>
            <p>{description}</p>
          </div>
          <div className="inline-actions">
            {quickLinks.map((link) => (
              <a className="button button--ghost" href={link.href} key={link.href}>
                {link.label}
              </a>
            ))}
          </div>
        </div>
        <MetricStrip items={metrics} />
        <div className="catalog-grid">
          {cards.map((card) => (
            <article className="catalog-card" key={card.title}>
              <span className="catalog-card__eyebrow">{card.eyebrow}</span>
              <strong>{card.title}</strong>
              <p>{card.description}</p>
              <span className="catalog-card__badge">{card.badge}</span>
            </article>
          ))}
        </div>
      </section>
    </div>
  )
}

function SettingsPage({
  claudeHome,
  compactWorkspace,
  config,
  configDirty,
  inventoryMetrics,
  sessionMetrics,
  themeMode,
}: {
  claudeHome: ClaudeHomeSummary | null
  compactWorkspace: boolean
  config: AppConfig
  configDirty: boolean
  inventoryMetrics: MetricItem[]
  sessionMetrics: MetricItem[]
  themeMode: ThemeMode
}) {
  return (
    <div className="product-page product-page--settings" data-testid="product-page-settings">
      <section className="page-stack">
        <div className="page-intro">
          <div>
            <p className="eyebrow">Control surfaces</p>
            <h2>Studio settings and operating posture</h2>
            <p>Use this page to understand how the local product behaves before deeper persistence arrives for every new surface.</p>
          </div>
        </div>
        <MetricStrip
          items={[
            ...sessionMetrics.slice(0, 2),
            ...inventoryMetrics.slice(0, 2),
            {
              label: 'Claude home',
              value: claudeHome?.exists ? 'Connected' : 'Missing',
              detail: claudeHome?.exists ? `${claudeHome.rootDisplayPath} is being summarized safely.` : 'No local Claude home was discovered.',
            },
            { label: 'Theme mode', value: themeMode, detail: 'Synchronized with system theme preference.' },
          ]}
        />
        <div className="overview-grid">
          <section className="panel">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Visual behavior</p>
                <h2>Viewport and theme</h2>
              </div>
            </div>
            <div className="summary-list">
              <article>
                <span>Theme source</span>
                <strong>System preference</strong>
              </article>
              <article>
                <span>Current mode</span>
                <strong>{themeMode}</strong>
              </article>
              <article>
                <span>Compact workspace</span>
                <strong>{compactWorkspace ? 'Enabled' : 'Disabled'}</strong>
              </article>
            </div>
          </section>

          <section className="panel">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Persistence</p>
                <h2>Configuration state</h2>
              </div>
            </div>
            <div className="summary-list">
              <article>
                <span>Config version</span>
                <strong>{config.version}</strong>
              </article>
              <article>
                <span>Draft state</span>
                <strong>{configDirty ? 'Unsaved edits' : 'In sync'}</strong>
              </article>
              <article>
                <span>Polling cadence</span>
                <strong>5 seconds</strong>
              </article>
            </div>
          </section>

          <section className="panel">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Claude home</p>
                <h2>Local integration status</h2>
              </div>
            </div>
            <div className="summary-list">
              <article>
                <span>Catalog root</span>
                <strong>{claudeHome?.rootDisplayPath ?? '~/.claude'}</strong>
              </article>
              <article>
                <span>Agents / skills</span>
                <strong>{claudeHome?.exists ? `${claudeHome.agentCount} / ${claudeHome.skillCount}` : 'Unavailable'}</strong>
              </article>
              <article>
                <span>Enabled plugins</span>
                <strong>{claudeHome?.settings?.enabledPluginCount ?? 0}</strong>
              </article>
              <article>
                <span>Local overrides</span>
                <strong>{claudeHome?.settings?.hasLocalOverrides ? 'Present' : 'None'}</strong>
              </article>
              <article>
                <span>Permissions prompt</span>
                <strong>
                  {claudeHome?.settings?.skipDangerousModePermissionPrompt === true
                    ? 'Skipped'
                    : claudeHome?.settings?.skipDangerousModePermissionPrompt === false
                      ? 'Enforced'
                      : 'Unknown'}
                </strong>
              </article>
            </div>
          </section>

          <ProductNote
            title="What comes next"
            body="The studio now reflects the local Claude home without exposing sensitive env values. This page is ready to expand into deeper user preferences, experiment defaults, audit retention, and global runtime policies next."
          />
        </div>
      </section>
    </div>
  )
}

interface WorkspacePaneViewProps {
  allowCompare: boolean
  isStoppingSession: boolean
  label: string
  onOpenSession: (session: SessionSummary, target?: 'primary' | 'secondary') => void
  onSelectTab: (target: 'primary' | 'secondary', tabId: string) => void
  onSessionMessage: (sessionId: string, message: TerminalServerMessage) => void
  onStopSession: (sessionId: string) => void
  session: SessionSummary | null
  sessions: SessionSummary[]
  tab: WorkspaceTab | null
  tabOptions: Array<{ label: string; tabId: string }>
  target: 'primary' | 'secondary'
  themeMode: ThemeMode
}

function WorkspacePaneView({
  allowCompare,
  isStoppingSession,
  label,
  onOpenSession,
  onSelectTab,
  onSessionMessage,
  onStopSession,
  session,
  sessions,
  tab,
  tabOptions,
  target,
    themeMode,
}: WorkspacePaneViewProps) {
  return (
    <section className={`workspace-pane workspace-pane--${target}`} data-testid={`workspace-pane-${target}`}>
      <header className="workspace-pane__header">
        <div className="workspace-pane__meta">
          <span className="workspace-pane__eyebrow">{label}</span>
          <strong title={tab?.title ?? 'Orchestration'}>{formatWorkspaceLabel(tab?.title ?? 'Orchestration')}</strong>
        </div>
        <div className="workspace-pane__controls">
          <label className="workspace-pane__picker">
            <span>View</span>
            <select
              aria-label={`${label} view`}
              data-testid={`workspace-pane-picker-${target}`}
              value={tab?.id ?? orchestrationTabId}
              onChange={(event) => onSelectTab(target, event.target.value)}
            >
              {tabOptions.map((option) => (
                <option key={option.tabId} value={option.tabId}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
          {session ? (
            <>
              <span className={`session-state session-state--${session.state.toLowerCase()}`}>{session.state}</span>
              <button
                className="button button--ghost"
                data-testid={`stop-session-${session.id}`}
                onClick={() => onStopSession(session.id)}
                disabled={isStoppingSession || session.state !== 'Running'}
                type="button"
              >
                {isStoppingSession ? 'Stopping…' : 'Stop'}
              </button>
            </>
          ) : null}
        </div>
      </header>

      {isSessionTab(tab) && session ? (
        <div className="workspace-pane__body">
          <div className="workspace-pane__summary">
            <span>{session.backplaneId}</span>
            <span>{session.hostId}</span>
            <span>{session.connectorId}</span>
            <code title={session.displayCommand}>{session.displayCommand}</code>
          </div>

          <div className="stage__dock workspace-pane__dock">
            <TerminalPane
              label={formatWorkspaceLabel(session.id)}
              sessionId={session.id}
              onSessionMessage={onSessionMessage}
              themeMode={themeMode}
            />
          </div>
        </div>
      ) : (
        <OrchestrationBoard allowCompare={allowCompare} onOpenSession={onOpenSession} sessions={sessions} target={target} />
      )}
    </section>
  )
}

interface OrchestrationBoardProps {
  allowCompare: boolean
  onOpenSession: (session: SessionSummary, target?: 'primary' | 'secondary') => void
  sessions: SessionSummary[]
  target: 'primary' | 'secondary'
}

function OrchestrationBoard({ allowCompare, onOpenSession, sessions, target }: OrchestrationBoardProps) {
  const runningCount = sessions.filter((session) => session.state === 'Running').length
  const attentionCount = sessions.filter((session) => session.state === 'Failed').length

  return (
    <div className="workspace-board">
      <div className="workspace-board__stats">
        <article className="workspace-stat">
          <span className="workspace-stat__label">Live sessions</span>
          <strong>{sessions.length}</strong>
        </article>
        <article className="workspace-stat">
          <span className="workspace-stat__label">Running now</span>
          <strong>{runningCount}</strong>
        </article>
        <article className="workspace-stat">
          <span className="workspace-stat__label">Needs attention</span>
          <strong>{attentionCount}</strong>
        </article>
      </div>

      {sessions.length > 0 ? (
        <div className="orchestration-list">
          {sessions.map((session) => (
            <article key={session.id} className="orchestration-item" data-testid={`orchestration-item-${session.id}`}>
              <div className="orchestration-item__meta">
                <span className={`session-state session-state--${session.state.toLowerCase()}`}>{session.state}</span>
                <strong title={session.id}>{formatWorkspaceLabel(session.id)}</strong>
                <p>{session.displayCommand}</p>
              </div>
              <div className="orchestration-item__actions">
                <button
                  aria-label={`Open ${session.id}`}
                  className="button button--ghost"
                  data-testid={`open-session-${session.id}`}
                  onClick={() => onOpenSession(session, target)}
                  type="button"
                >
                  Open
                </button>
                <button
                  aria-label={`Compare ${session.id}`}
                  className="button button--ghost"
                  data-testid={`compare-session-${session.id}`}
                  onClick={() => onOpenSession(session, 'secondary')}
                  disabled={!allowCompare}
                  title={!allowCompare ? 'Compare mode is unavailable on compact viewports.' : undefined}
                  type="button"
                >
                  Compare
                </button>
              </div>
            </article>
          ))}
        </div>
      ) : (
        <div className="empty-stage">
          <p className="eyebrow">No live sessions yet</p>
          <h3>Launch a session and build a workspace.</h3>
          <p>
            Start with <code>shell</code> on <code>local-host</code>, then open compare panes to watch
            multiple runtimes side by side.
          </p>
          <div className="empty-stage__commands">
            <span>pwsh</span>
            <span>ollama run</span>
            <span>docker exec</span>
          </div>
        </div>
      )}
    </div>
  )
}

interface ConfigBlockProps {
  title: string
  description: string
  actionLabel: string
  children: ReactNode
  onAdd: () => void
}

function ConfigBlock({ title, description, actionLabel, children, onAdd }: ConfigBlockProps) {
  return (
    <section className="config-block">
      <div className="config-block__header">
        <div>
          <h3>{title}</h3>
          <p>{description}</p>
        </div>
        <button className="button button--ghost" onClick={onAdd} type="button">
          {actionLabel}
        </button>
      </div>
      <div className="config-stack">{children}</div>
    </section>
  )
}

interface BackplaneEditorProps {
  backplane: BackplaneDefinition
  onChange: (value: BackplaneDefinition) => void
  onRemove: () => void
}

function BackplaneEditor({ backplane, onChange, onRemove }: BackplaneEditorProps) {
  return (
    <article className="config-card" data-testid={`backplane-card-${backplane.id}`}>
      <header className="config-card__header">
        <strong>{backplane.id}</strong>
        <button className="button button--ghost" onClick={onRemove} type="button">
          Remove
        </button>
      </header>
      <div className="config-card__grid">
        <label className="field">
          <span>ID</span>
          <input value={backplane.id} onChange={(event) => onChange({ ...backplane, id: event.target.value })} />
        </label>
        <label className="field">
          <span>Name</span>
          <input
            value={backplane.displayName}
            onChange={(event) => onChange({ ...backplane, displayName: event.target.value })}
          />
        </label>
        <label className="field">
          <span>Kind</span>
          <input value={backplane.kind} onChange={(event) => onChange({ ...backplane, kind: event.target.value })} />
        </label>
        <label className="toggle">
          <input
            type="checkbox"
            checked={backplane.enabled}
            onChange={(event) => onChange({ ...backplane, enabled: event.target.checked })}
          />
          <span>Enabled</span>
        </label>
      </div>
    </article>
  )
}

interface HostEditorProps {
  host: HostConfig
  backplanes: BackplaneDefinition[]
  onChange: (value: HostConfig) => void
  onRemove: () => void
}

function HostEditor({ host, backplanes, onChange, onRemove }: HostEditorProps) {
  const selectedBackplaneKind =
    backplanes.find((backplane) => backplane.id === host.backplaneId)?.kind.toLowerCase() ?? 'local'

  return (
    <article className="config-card" data-testid={`host-card-${host.id}`}>
      <header className="config-card__header">
        <strong>{host.id}</strong>
        <button className="button button--ghost" onClick={onRemove} type="button">
          Remove
        </button>
      </header>
      <div className="config-card__grid">
        <label className="field">
          <span>ID</span>
          <input value={host.id} onChange={(event) => onChange({ ...host, id: event.target.value })} />
        </label>
        <label className="field">
          <span>Name</span>
          <input
            value={host.displayName}
            onChange={(event) => onChange({ ...host, displayName: event.target.value })}
          />
        </label>
        <label className="field">
          <span>Backplane</span>
          <select
            value={host.backplaneId}
            onChange={(event) => onChange({ ...host, backplaneId: event.target.value })}
          >
            {backplanes.map((backplane) => (
              <option key={backplane.id} value={backplane.id}>
                {backplane.displayName}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>Shell executable</span>
          <input
            value={host.shellExecutable}
            onChange={(event) => onChange({ ...host, shellExecutable: event.target.value })}
          />
        </label>
        <label className="field">
          <span>Shell arguments</span>
          <input
            value={formatArgumentList(host.shellArguments)}
            onChange={(event) => onChange({ ...host, shellArguments: parseArgumentList(event.target.value) })}
          />
        </label>
        <label className="field">
          <span>Working directory</span>
          <input
            value={host.workingDirectory ?? ''}
            onChange={(event) =>
              onChange({ ...host, workingDirectory: event.target.value.trim() || null })
            }
          />
        </label>
        {selectedBackplaneKind === 'docker' ? (
          <>
            <label className="field">
              <span>Docker endpoint</span>
              <input
                value={host.dockerEndpoint ?? ''}
                onChange={(event) =>
                  onChange({ ...host, dockerEndpoint: event.target.value.trim() || null })
                }
              />
            </label>
            <label className="field">
              <span>Docker image</span>
              <input
                value={host.dockerImage ?? ''}
                onChange={(event) =>
                  onChange({ ...host, dockerImage: event.target.value.trim() || null })
                }
              />
            </label>
          </>
        ) : null}
        {selectedBackplaneKind === 'ssh' ? (
          <>
            <label className="field">
              <span>SSH address</span>
              <input
                data-testid="host-ssh-address"
                value={host.sshAddress ?? ''}
                onChange={(event) =>
                  onChange({ ...host, sshAddress: event.target.value.trim() || null })
                }
              />
            </label>
            <label className="field">
              <span>SSH port</span>
              <input
                data-testid="host-ssh-port"
                type="number"
                min={1}
                max={65535}
                value={host.sshPort ?? 22}
                onChange={(event) =>
                  onChange({ ...host, sshPort: Number.parseInt(event.target.value, 10) || 22 })
                }
              />
            </label>
            <label className="field">
              <span>SSH username</span>
              <input
                data-testid="host-ssh-username"
                value={host.sshUsername ?? ''}
                onChange={(event) =>
                  onChange({ ...host, sshUsername: event.target.value.trim() || null })
                }
              />
            </label>
            <label className="field">
              <span>SSH password</span>
              <input
                data-testid="host-ssh-password"
                type="password"
                value={host.sshPassword ?? ''}
                onChange={(event) =>
                  onChange({ ...host, sshPassword: event.target.value || null })
                }
              />
            </label>
            <label className="field">
              <span>SSH private key path</span>
              <input
                data-testid="host-ssh-private-key-path"
                value={host.sshPrivateKeyPath ?? ''}
                onChange={(event) =>
                  onChange({ ...host, sshPrivateKeyPath: event.target.value.trim() || null })
                }
              />
            </label>
            <label className="field">
              <span>SSH private key passphrase</span>
              <input
                data-testid="host-ssh-private-key-passphrase"
                type="password"
                value={host.sshPrivateKeyPassphrase ?? ''}
                onChange={(event) =>
                  onChange({ ...host, sshPrivateKeyPassphrase: event.target.value || null })
                }
              />
            </label>
            <label className="field">
              <span>SSH certificate path</span>
              <input
                data-testid="host-ssh-certificate-path"
                value={host.sshCertificatePath ?? ''}
                onChange={(event) =>
                  onChange({ ...host, sshCertificatePath: event.target.value.trim() || null })
                }
              />
            </label>
            <label className="field">
              <span>Host key fingerprint</span>
              <input
                data-testid="host-ssh-host-key-fingerprint"
                value={host.sshHostKeyFingerprint ?? ''}
                onChange={(event) =>
                  onChange({ ...host, sshHostKeyFingerprint: event.target.value.trim() || null })
                }
              />
            </label>
            <label className="field">
              <span>Trusted CA fingerprint</span>
              <input
                data-testid="host-ssh-trusted-ca-fingerprint"
                value={host.sshTrustedCaFingerprint ?? ''}
                onChange={(event) =>
                  onChange({ ...host, sshTrustedCaFingerprint: event.target.value.trim() || null })
                }
              />
            </label>
            <label className="toggle">
              <input
                data-testid="host-ssh-allow-any-host-key"
                type="checkbox"
                checked={Boolean(host.sshAllowAnyHostKey)}
                onChange={(event) =>
                  onChange({ ...host, sshAllowAnyHostKey: event.target.checked })
                }
              />
              <span>Allow any host key</span>
            </label>
            <label className="toggle">
              <input
                data-testid="host-ssh-use-keyboard-interactive"
                type="checkbox"
                checked={Boolean(host.sshUseKeyboardInteractive)}
                onChange={(event) =>
                  onChange({ ...host, sshUseKeyboardInteractive: event.target.checked })
                }
              />
              <span>Use keyboard-interactive auth</span>
            </label>
          </>
        ) : null}
        {selectedBackplaneKind === 'remote' ? (
          <>
            <label className="field">
              <span>Remote daemon URL</span>
              <input
                data-testid="host-remote-daemon-url"
                value={host.remoteDaemonUrl ?? ''}
                onChange={(event) =>
                  onChange({ ...host, remoteDaemonUrl: event.target.value.trim() || null })
                }
              />
            </label>
            <label className="field">
              <span>Remote access token</span>
              <input
                data-testid="host-remote-access-token"
                type="password"
                value={host.remoteAccessToken ?? ''}
                onChange={(event) =>
                  onChange({ ...host, remoteAccessToken: event.target.value || null })
                }
              />
            </label>
            <label className="field">
              <span>Remote executor</span>
              <select
                data-testid="host-remote-executor-kind"
                value={host.remoteExecutorKind ?? 'process'}
                onChange={(event) =>
                  onChange({ ...host, remoteExecutorKind: event.target.value })
                }
              >
                <option value="process">process</option>
                <option value="docker">docker</option>
              </select>
            </label>
            <label className="toggle">
              <input
                data-testid="host-remote-allow-insecure-tls"
                type="checkbox"
                checked={Boolean(host.remoteAllowInsecureTls)}
                onChange={(event) =>
                  onChange({ ...host, remoteAllowInsecureTls: event.target.checked })
                }
              />
              <span>Allow insecure TLS</span>
            </label>
            {host.remoteExecutorKind === 'docker' ? (
              <>
                <label className="field">
                  <span>Remote Docker endpoint</span>
                  <input
                    data-testid="host-remote-docker-endpoint"
                    value={host.remoteDockerEndpoint ?? ''}
                    onChange={(event) =>
                      onChange({ ...host, remoteDockerEndpoint: event.target.value.trim() || null })
                    }
                  />
                </label>
                <label className="field">
                  <span>Remote Docker image</span>
                  <input
                    data-testid="host-remote-docker-image"
                    value={host.remoteDockerImage ?? ''}
                    onChange={(event) =>
                      onChange({ ...host, remoteDockerImage: event.target.value.trim() || null })
                    }
                  />
                </label>
              </>
            ) : null}
          </>
        ) : null}
        <label className="toggle">
          <input
            type="checkbox"
            checked={host.enabled}
            onChange={(event) => onChange({ ...host, enabled: event.target.checked })}
          />
          <span>Enabled</span>
        </label>
      </div>
    </article>
  )
}

interface ConnectorEditorProps {
  connector: ConnectorDefinition
  onChange: (value: ConnectorDefinition) => void
  onRemove: () => void
}

function ConnectorEditor({ connector, onChange, onRemove }: ConnectorEditorProps) {
  const connectorKind = connector.kind.trim().toLowerCase()
  const isShellConnector = connectorKind === 'shell'

  return (
    <article className="config-card" data-testid={`connector-card-${connector.id}`}>
      <header className="config-card__header">
        <strong>{connector.id}</strong>
        <button className="button button--ghost" onClick={onRemove} type="button">
          Remove
        </button>
      </header>
      <div className="config-card__grid">
        <label className="field">
          <span>ID</span>
          <input
            value={connector.id}
            onChange={(event) => onChange({ ...connector, id: event.target.value })}
          />
        </label>
        <label className="field">
          <span>Name</span>
          <input
            value={connector.displayName}
            onChange={(event) => onChange({ ...connector, displayName: event.target.value })}
          />
        </label>
        <label className="field">
          <span>Kind</span>
          <input
            value={connector.kind}
            onChange={(event) => onChange({ ...connector, kind: event.target.value })}
          />
        </label>

        {isShellConnector ? (
          <div className="field field--hint">
            <span>Launch target</span>
            <p>Uses the selected host&apos;s shell executable directly.</p>
          </div>
        ) : (
          <>
            <label className="field">
              <span>Command</span>
              <input
                value={connector.launchCommand ?? ''}
                onChange={(event) =>
                  onChange({ ...connector, launchCommand: event.target.value.trim() || null })
                }
              />
            </label>
            <label className="field">
              <span>Base arguments</span>
              <input
                placeholder={connectorKind === 'ollama' ? '--verbose' : '--verbose, --json'}
                value={formatArgumentList(connector.launchArguments)}
                onChange={(event) =>
                  onChange({ ...connector, launchArguments: parseArgumentList(event.target.value) })
                }
              />
            </label>
            <label className="field">
              <span>Default model</span>
              <input
                value={connector.defaultModel ?? ''}
                onChange={(event) =>
                  onChange({ ...connector, defaultModel: event.target.value.trim() || null })
                }
              />
            </label>
            {connectorKind === 'claude' ? (
              <>
                <label className="field">
                  <span>Permission mode</span>
                  <input
                    list="permission-mode-options"
                    value={connector.defaultPermissionMode ?? ''}
                    onChange={(event) =>
                      onChange({ ...connector, defaultPermissionMode: event.target.value.trim() || null })
                    }
                  />
                </label>
                <label className="field">
                  <span>Allowed tools</span>
                  <input
                    placeholder="Read, Edit, Bash(ls *)"
                    value={formatArgumentList(connector.allowedTools)}
                    onChange={(event) =>
                      onChange({ ...connector, allowedTools: parseArgumentList(event.target.value) })
                    }
                  />
                </label>
                <label className="toggle">
                  <input
                    type="checkbox"
                    checked={connector.skipPermissions}
                    onChange={(event) =>
                      onChange({ ...connector, skipPermissions: event.target.checked })
                    }
                  />
                  <span>Dangerously skip permissions</span>
                </label>
              </>
            ) : null}
          </>
        )}
        <label className="toggle">
          <input
            type="checkbox"
            checked={connector.enabled}
            onChange={(event) => onChange({ ...connector, enabled: event.target.checked })}
          />
          <span>Enabled</span>
        </label>
      </div>
    </article>
  )
}

interface ExperimentEditorProps {
  configDirty: boolean
  experiment: ExperimentDefinition
  isRunningExperiment: boolean
  onChange: (value: ExperimentDefinition) => void
  onRemove: () => void
  onRun: () => void
}

function ExperimentEditor({ configDirty, experiment, isRunningExperiment, onChange, onRemove, onRun }: ExperimentEditorProps) {
  return (
    <article className="config-card" data-testid={`experiment-card-${experiment.id}`}>
      <header className="config-card__header">
        <div>
          <strong>{experiment.id}</strong>
          <p>{experiment.displayName}</p>
        </div>
        <div className="inline-actions inline-actions--compact">
          <button
            className="button button--ghost"
            data-testid={`run-experiment-${experiment.id}`}
            disabled={configDirty || isRunningExperiment || !experiment.enabled}
            onClick={onRun}
            type="button"
          >
            {isRunningExperiment ? 'Running…' : 'Run now'}
          </button>
          <button className="button button--ghost" onClick={onRemove} type="button">
            Remove
          </button>
        </div>
      </header>
      <div className="config-card__grid">
        <label className="field">
          <span>ID</span>
          <input value={experiment.id} onChange={(event) => onChange({ ...experiment, id: event.target.value })} />
        </label>
        <label className="field">
          <span>Name</span>
          <input
            value={experiment.displayName}
            onChange={(event) => onChange({ ...experiment, displayName: event.target.value })}
          />
        </label>
        <label className="field">
          <span>Description</span>
          <input
            value={experiment.description ?? ''}
            onChange={(event) =>
              onChange({ ...experiment, description: event.target.value.trim() || null })
            }
          />
        </label>
        <label className="field">
          <span>Host ids</span>
          <input
            placeholder="local-host, docker-local"
            value={formatArgumentList(experiment.hostIds)}
            onChange={(event) =>
              onChange({ ...experiment, hostIds: parseArgumentList(event.target.value) })
            }
          />
        </label>
        <label className="field">
          <span>Connector ids</span>
          <input
            placeholder="shell, claude"
            value={formatArgumentList(experiment.connectorIds)}
            onChange={(event) =>
              onChange({ ...experiment, connectorIds: parseArgumentList(event.target.value) })
            }
          />
        </label>
        <label className="field">
          <span>Models</span>
          <input
            placeholder="Leave blank to use connector defaults"
            value={formatArgumentList(experiment.models)}
            onChange={(event) =>
              onChange({ ...experiment, models: parseArgumentList(event.target.value) })
            }
          />
        </label>
        <label className="field">
          <span>Columns</span>
          <input
            type="number"
            min={24}
            max={240}
            value={experiment.cols}
            onChange={(event) =>
              onChange({ ...experiment, cols: Number(event.target.value) || 120 })
            }
          />
        </label>
        <label className="field">
          <span>Rows</span>
          <input
            type="number"
            min={12}
            max={120}
            value={experiment.rows}
            onChange={(event) =>
              onChange({ ...experiment, rows: Number(event.target.value) || 34 })
            }
          />
        </label>
        <label className="toggle">
          <input
            checked={experiment.enabled}
            onChange={(event) => onChange({ ...experiment, enabled: event.target.checked })}
            type="checkbox"
          />
          <span>Enabled</span>
        </label>
      </div>
    </article>
  )
}

function updateItem<T>(items: T[], index: number, nextValue: T): T[] {
  return items.map((item, itemIndex) => (itemIndex === index ? nextValue : item))
}

function asMessage(error: unknown): string {
  if (error instanceof ApiError && error.details?.errors) {
    const detail = Object.values(error.details.errors)
      .flat()
      .join(' ')
    return detail || error.message
  }

  if (error instanceof Error) {
    return error.message
  }

  return 'An unexpected error interrupted the control deck.'
}

function formatWorkspaceLayout(layout: WorkspaceLayout): string {
  switch (layout) {
    case 'split-horizontal':
      return 'Split rows'
    case 'split-vertical':
      return 'Split columns'
    default:
      return 'Single pane'
  }
}

function getConnectorLaunchDefaults(connector?: ConnectorDefinition | null): Pick<
  LaunchDraft,
  'agent' | 'allowedTools' | 'model' | 'permissionMode' | 'skipPermissions'
> {
  void connector
  return {
    model: null,
    permissionMode: null,
    skipPermissions: null,
    allowedTools: null,
    agent: null,
  }
}

interface ConnectorLaunchCapabilities {
  supportsAgent: boolean
  supportsAllowedTools: boolean
  supportsModel: boolean
  supportsPermissionMode: boolean
  supportsSkipPermissions: boolean
}

const claudeAllowedToolOptions = ['Read', 'Edit', 'Write', 'Glob', 'Grep', 'Bash', 'Task', 'WebFetch', 'TodoWrite']

function formatConnectorLaunchTarget(connector: ConnectorDefinition): string {
  if (connector.kind.toLowerCase() === 'shell') {
    return 'Selected host shell'
  }

  const command = connector.launchCommand ?? connector.kind
  const argumentsList =
    connector.kind.toLowerCase() === 'ollama'
      ? ['run', ...connector.launchArguments]
      : connector.launchArguments
  const argumentsText = argumentsList.join(' ')
  return argumentsText ? `${command} ${argumentsText}` : command
}

function formatWorkspaceLabel(value: string): string {
  if (value.length <= 22) {
    return value
  }

  return `${value.slice(0, 8)}…${value.slice(-8)}`
}

const permissionModes = ['default', 'acceptEdits', 'plan', 'auto', 'dontAsk', 'bypassPermissions']

function getConnectorLaunchCapabilities(connector?: ConnectorDefinition | null): ConnectorLaunchCapabilities {
  const kind = connector?.kind.trim().toLowerCase()
  return {
    supportsAgent: kind === 'claude',
    supportsAllowedTools: kind === 'claude',
    supportsModel: Boolean(connector && kind !== 'shell'),
    supportsPermissionMode: kind === 'claude',
    supportsSkipPermissions: kind === 'claude',
  }
}

function getKnownAllowedToolOptions(connector?: ConnectorDefinition | null): string[] {
  if (connector?.kind.trim().toLowerCase() !== 'claude') {
    return []
  }

  return dedupeLaunchValues([...claudeAllowedToolOptions, ...connector.allowedTools])
}

function dedupeLaunchValues(values: string[]): string[] {
  return values
    .map((value) => value.trim())
    .filter(Boolean)
    .filter((value, index, items) => items.findIndex((candidate) => sameLaunchValue(candidate, value)) === index)
}

function sameLaunchValue(left: string, right: string): boolean {
  return left.localeCompare(right, undefined, { sensitivity: 'accent' }) === 0
}

function formatPermissionMode(value: string): string {
  switch (value) {
    case 'acceptEdits':
      return 'acceptEdits'
    case 'bypassPermissions':
      return 'bypassPermissions'
    default:
      return value
  }
}

function formatAllowedToolSummary(values: string[]): string {
  if (values.length === 0) {
    return 'no pre-approved tools'
  }

  if (values.length <= 3) {
    return values.join(', ')
  }

  return `${values.slice(0, 3).join(', ')} +${values.length - 3} more`
}

function shouldLoadClaudeCatalogForSection(
  section: StudioSection,
  claudeHomeExists: boolean | undefined,
  connector?: ConnectorDefinition | null,
): boolean {
  if (!claudeHomeExists) {
    return false
  }

  return (
    section === 'agents' ||
    section === 'skills' ||
    (section === 'workspace' && connector?.kind.trim().toLowerCase() === 'claude')
  )
}

function useSystemTheme(): ThemeMode {
  const [themeMode, setThemeMode] = useState<ThemeMode>(() => readSystemTheme())

  useEffect(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return
    }

    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
    const syncTheme = () => {
      setThemeMode(mediaQuery.matches ? 'dark' : 'light')
    }

    syncTheme()

    if (typeof mediaQuery.addEventListener === 'function') {
      mediaQuery.addEventListener('change', syncTheme)
      return () => {
        mediaQuery.removeEventListener('change', syncTheme)
      }
    }

    mediaQuery.addListener(syncTheme)
    return () => {
      mediaQuery.removeListener(syncTheme)
    }
  }, [])

  useEffect(() => {
    document.documentElement.dataset.theme = themeMode
  }, [themeMode])

  return themeMode
}

function useCompactWorkspace(): boolean {
  const [isCompact, setIsCompact] = useState<boolean>(() => readCompactWorkspace())

  useEffect(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return
    }

    const mediaQuery = window.matchMedia(compactWorkspaceQuery)
    const syncCompactMode = () => {
      setIsCompact(mediaQuery.matches)
    }

    syncCompactMode()

    if (typeof mediaQuery.addEventListener === 'function') {
      mediaQuery.addEventListener('change', syncCompactMode)
      return () => {
        mediaQuery.removeEventListener('change', syncCompactMode)
      }
    }

    mediaQuery.addListener(syncCompactMode)
    return () => {
      mediaQuery.removeListener(syncCompactMode)
    }
  }, [])

  return isCompact
}

function readSystemTheme(): ThemeMode {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return 'light'
  }

  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

function readCompactWorkspace(): boolean {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return false
  }

  return window.matchMedia(compactWorkspaceQuery).matches
}

function useStudioSection(): StudioSection {
  const [section, setSection] = useState<StudioSection>(() => readStudioSection())

  useEffect(() => {
    if (typeof window === 'undefined') {
      return
    }

    if (!window.location.hash) {
      const initialSection = readStudioSection()
      window.history.replaceState(
        null,
        '',
        `${window.location.pathname}${window.location.search}${createStudioSectionHref(initialSection)}`,
      )
    }

    const syncSection = () => {
      setSection(readStudioSection())
    }

    window.addEventListener('hashchange', syncSection)
    return () => {
      window.removeEventListener('hashchange', syncSection)
    }
  }, [])

  return section
}

function shouldStartWorkspaceStudioNavOpen(): boolean {
  return readStudioSection() !== 'workspace'
}

function readStudioSection(): StudioSection {
  if (typeof window === 'undefined') {
    return defaultStudioSection
  }

  const hash = window.location.hash.replace(/^#\/?/, '').trim().toLowerCase()
  const section = (hash || readStudioSectionFromPath(window.location.pathname)) as StudioSection
  return Object.hasOwn(studioSectionMeta, section) ? section : defaultStudioSection
}

function readStudioSectionFromPath(pathname: string): string {
  return pathname.replace(/^\/+/, '').split('/')[0]?.trim().toLowerCase() ?? ''
}

function createStudioSectionHref(section: StudioSection): string {
  return `#/${section}`
}

function setStudioSectionHash(section: StudioSection) {
  if (typeof window === 'undefined') {
    return
  }

  window.location.hash = createStudioSectionHref(section)
}

function summarizeStatusMessage(message: string): string {
  if (/synchronized/i.test(message)) {
    return 'Synced'
  }

  if (/configuration saved/i.test(message)) {
    return 'Config saved'
  }

  if (/configuration edits discarded/i.test(message)) {
    return 'Draft reset'
  }

  if (/stop requested/i.test(message)) {
    return 'Stopping'
  }

  if (/orchestration board/i.test(message)) {
    return 'Board active'
  }

  if (/session .* is live/i.test(message)) {
    return 'Session live'
  }

  if (/focused /i.test(message)) {
    return 'Session focused'
  }

  if (/comparing /i.test(message)) {
    return 'Compare active'
  }

  return message.length > 18 ? `${message.slice(0, 18)}…` : message
}

export default App

import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'

import { TerminalPane } from './components/TerminalPane'
import { ApiError, createSession, loadAppState, loadSessions, saveConfig, stopSession } from './lib/api'
import {
  coerceLaunchDraft,
  createBackplaneDefinition,
  createConnectorDefinition,
  createHostConfig,
  formatArgumentList,
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
  ConnectorDefinition,
  HostConfig,
  SessionSummary,
  TerminalServerMessage,
} from './types'

type ThemeMode = 'light' | 'dark'

function App() {
  const themeMode = useSystemTheme()
  const [savedConfig, setSavedConfig] = useState<AppConfig | null>(null)
  const [configDraft, setConfigDraft] = useState<AppConfig | null>(null)
  const [sessions, setSessions] = useState<SessionSummary[]>([])
  const [launchDraft, setLaunchDraft] = useState<LaunchDraft>({
    backplaneId: '',
    hostId: '',
    connectorId: '',
    cols: 120,
    rows: 34,
  })
  const [workspace, setWorkspace] = useState(() => createInitialWorkspace([]))
  const [isBooting, setIsBooting] = useState(true)
  const [isCreatingSession, setIsCreatingSession] = useState(false)
  const [isSavingConfig, setIsSavingConfig] = useState(false)
  const [isRefreshing, setIsRefreshing] = useState(false)
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
      setSessions(state.sessions)
      setWorkspace(createInitialWorkspace(state.sessions))
      setLaunchDraft(coerceLaunchDraft(state.config))
      setConfigDirty(false)
      setStatusMessage('Control deck synchronized with the local runtime.')
    } catch (error) {
      setErrorMessage(asMessage(error))
    } finally {
      setIsBooting(false)
    }
  }, [])

  useEffect(() => {
    void loadWorkspace()
  }, [loadWorkspace])

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

  const updateLaunchDraft = <K extends keyof LaunchDraft>(key: K, value: LaunchDraft[K]) => {
    if (!savedConfig) {
      return
    }

    setLaunchDraft((current) =>
      coerceLaunchDraft(savedConfig, {
        ...current,
        [key]: value,
      }),
    )
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
      setStatusMessage(`Session ${createdSession.id} is live.`)
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
      setStatusMessage('Configuration persisted to local storage.')
    } catch (error) {
      setErrorMessage(asMessage(error))
    } finally {
      setIsSavingConfig(false)
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
    setWorkspace((current) => openSessionTab(current, session, { target }))
    setStatusMessage(
      target === 'secondary' ? `Comparing ${session.id} in a split pane.` : `Focused ${session.id} in the workspace.`,
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

  return (
    <>
      <a href="#main-content" className="skip-link">
        Skip to terminal
      </a>
      <div className="app-shell">
        <header className="masthead">
          <div className="masthead__brand">
            <p className="eyebrow">Browser terminal cockpit</p>
            <div className="masthead__title">
              <h1>ClankYankers</h1>
              <p className="masthead__lede">Local shell and agent runtime control deck.</p>
            </div>
          </div>
          <div className="masthead__meta">
            <div className="masthead__status">
              <div className="status-chip">
                <span className="status-chip__label">Deck status</span>
                <strong>{statusMessage}</strong>
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
              <button
                className="button button--ghost"
                data-testid="refresh-sessions"
                onClick={() => void refreshSessions()}
                type="button"
              >
                {isRefreshing ? 'Refreshing…' : 'Refresh sessions'}
              </button>
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
            </div>
          </div>
        </header>

        {errorMessage ? (
          <div className="alert" role="alert">
            <strong>Heads up:</strong> {errorMessage}
          </div>
        ) : null}

        <main className={`workspace${activeSession ? ' workspace--active' : ''}`}>
          <aside className="rail">
            <section className="panel panel--launch">
              <div className="panel__header">
                <div>
                  <p className="eyebrow">Session launch</p>
                  <h2>New session</h2>
                </div>
              </div>

              <form className="launch-form" data-testid="launch-form" onSubmit={handleLaunchSession}>
                <label className="field">
                  <span>Backplane</span>
                  <select
                    data-testid="launch-backplane"
                    value={launchDraft.backplaneId}
                    onChange={(event) => updateLaunchDraft('backplaneId', event.target.value)}
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
                    onChange={(event) => updateLaunchDraft('hostId', event.target.value)}
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
                    onChange={(event) => updateLaunchDraft('connectorId', event.target.value)}
                  >
                    {enabledConnectors.map((connector) => (
                      <option key={connector.id} value={connector.id}>
                        {connector.displayName}
                      </option>
                    ))}
                  </select>
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
                      onChange={(event) => updateLaunchDraft('cols', Number(event.target.value))}
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
                      onChange={(event) => updateLaunchDraft('rows', Number(event.target.value))}
                    />
                  </label>
                </div>

                <button className="button button--solid launch-button" data-testid="launch-session" disabled={isCreatingSession}>
                  {isCreatingSession ? 'Launching…' : 'Launch session'}
                </button>
              </form>
            </section>

            <details className="panel panel--settings" data-testid="config-panel">
              <summary data-testid="config-panel-toggle">
                <div>
                  <p className="eyebrow">Configuration manifest</p>
                  <h2>Runtime config</h2>
                </div>
              </summary>
              <div className="panel--settings__body">
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
              </div>
            </details>

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
                      className={`session-card${activeSession?.id === session.id ? ' is-active' : ''}`}
                      onClick={() => handleOpenSession(session)}
                      role="tab"
                      aria-selected={activeSession?.id === session.id}
                      type="button"
                    >
                      <span className={`session-state session-state--${session.state.toLowerCase()}`}>
                        {session.state}
                      </span>
                      <strong title={session.id}>{formatWorkspaceLabel(session.id)}</strong>
                      <span>{session.displayCommand}</span>
                    </button>
                  ))}
                </div>
              ) : (
                <div className="empty-callout">
                  <p className="eyebrow">No live sessions yet</p>
                  <p>
                    Launch a shell or Ollama run, then switch tabs freely. Sessions survive WebSocket
                    disconnects and reattach when you come back.
                  </p>
                </div>
              )}
            </section>
          </aside>

          <section className={`stage${activeSession ? ' stage--active' : ' stage--idle'}`} id="main-content">
            <div className="stage__header">
              <div>
                <p className="eyebrow">Command studio</p>
                <h2>Workspace</h2>
                <p className="stage__lede">
                  Tabs manage long-lived sessions, while split panes handle compare and monitor flows
                  without breaking terminal fidelity.
                </p>
                <p className="stage__context">
                  Active: {formatWorkspaceLabel(activeTab?.title ?? 'Orchestration')}
                  {secondaryTab ? ` · Compare: ${formatWorkspaceLabel(secondaryTab.title)}` : ''}
                </p>
              </div>

              <div className="stage__actions">
                <button className="button button--ghost" onClick={handleOpenOrchestration} type="button">
                  Orchestration
                </button>
                <button
                  className="button button--ghost"
                  data-testid="split-vertical"
                  onClick={() => handleSetWorkspaceLayout('split-vertical')}
                  disabled={sessions.length < 2 && workspace.secondaryTabId === null}
                  type="button"
                >
                  Split vertical
                </button>
                <button
                  className="button button--ghost"
                  data-testid="split-horizontal"
                  onClick={() => handleSetWorkspaceLayout('split-horizontal')}
                  disabled={sessions.length < 2 && workspace.secondaryTabId === null}
                  type="button"
                >
                  Split horizontal
                </button>
                <button
                  className="button button--ghost"
                  data-testid="single-pane"
                  onClick={() => handleSetWorkspaceLayout('single')}
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
                      onClick={() => setWorkspace((current) => focusWorkspaceTab(current, tab.id))}
                      role="tab"
                      aria-selected={tab.id === workspace.activeTabId}
                      type="button"
                    >
                      <span className="workspace-tab__title" title={tab.title}>
                        {formatWorkspaceLabel(tab.title)}
                      </span>
                      <span className="workspace-tab__kind">
                        {tab.kind === 'orchestration' ? 'board' : 'session'}
                      </span>
                    </button>
                    {tab.closable ? (
                      <button
                        className="workspace-tab__close"
                        data-testid={`workspace-tab-close-${tab.id}`}
                        onClick={() => handleCloseWorkspaceTab(tab.id)}
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
                  onOpenSession={handleOpenSession}
                  onSelectTab={handleSelectPaneTab}
                  onSessionMessage={handleSessionMessage}
                  onStopSession={handleStopSession}
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
                    onOpenSession={handleOpenSession}
                    onSelectTab={handleSelectPaneTab}
                    onSessionMessage={handleSessionMessage}
                    onStopSession={handleStopSession}
                  />
                ) : null}
              </div>
            </div>
          </section>
        </main>
      </div>
    </>
  )
}

interface WorkspacePaneViewProps {
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
          <dl className="session-meta">
            <div>
              <dt>Backplane</dt>
              <dd>{session.backplaneId}</dd>
            </div>
            <div>
              <dt>Host</dt>
              <dd>{session.hostId}</dd>
            </div>
            <div>
              <dt>Connector</dt>
              <dd>{session.connectorId}</dd>
            </div>
            <div>
              <dt>Command</dt>
              <dd>{session.displayCommand}</dd>
            </div>
          </dl>

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
        <OrchestrationBoard onOpenSession={onOpenSession} sessions={sessions} target={target} />
      )}
    </section>
  )
}

interface OrchestrationBoardProps {
  onOpenSession: (session: SessionSummary, target?: 'primary' | 'secondary') => void
  sessions: SessionSummary[]
  target: 'primary' | 'secondary'
}

function OrchestrationBoard({ onOpenSession, sessions, target }: OrchestrationBoardProps) {
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
        <label className="field">
          <span>Default model</span>
          <input
            value={connector.defaultModel ?? ''}
            onChange={(event) =>
              onChange({ ...connector, defaultModel: event.target.value.trim() || null })
            }
          />
        </label>
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

function formatWorkspaceLabel(value: string): string {
  if (value.length <= 22) {
    return value
  }

  return `${value.slice(0, 8)}…${value.slice(-8)}`
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

function readSystemTheme(): ThemeMode {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return 'light'
  }

  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

export default App

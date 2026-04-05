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
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null)
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
      setLaunchDraft(coerceLaunchDraft(state.config))
      setActiveSessionId(state.sessions[0]?.id ?? null)
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
    if (activeSessionId && sessions.some((session) => session.id === activeSessionId)) {
      return
    }

    setActiveSessionId(sessions[0]?.id ?? null)
  }, [activeSessionId, sessions])

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
  const activeSession = sessions.find((session) => session.id === activeSessionId) ?? null

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
      setActiveSessionId(createdSession.id)
      setStatusMessage(`Session ${createdSession.id} is live.`)
    } catch (error) {
      setErrorMessage(asMessage(error))
    } finally {
      setIsCreatingSession(false)
    }
  }

  const handleStopSession = async () => {
    if (!activeSession) {
      return
    }

    try {
      setIsStoppingSession(true)
      setErrorMessage(null)
      await stopSession(activeSession.id)
      await refreshSessions()
      setStatusMessage(`Stop requested for ${activeSession.id}.`)
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
    (message: TerminalServerMessage) => {
      if (!activeSessionId) {
        return
      }

      if (message.type === 'state' && message.state) {
        setSessions((current) =>
          current.map((session) =>
            session.id === activeSessionId
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
            session.id === activeSessionId
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
            session.id === activeSessionId
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
    [activeSessionId],
  )

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
                <span className="status-chip__label">System theme</span>
                <strong>{themeMode === 'dark' ? 'Dark mode' : 'Light mode'}</strong>
              </div>
            </div>
            <div className="header-actions">
              <button className="button button--ghost" onClick={() => void refreshSessions()} type="button">
                {isRefreshing ? 'Refreshing…' : 'Refresh sessions'}
              </button>
              <button
                className="button button--ghost"
                onClick={handleResetDraft}
                disabled={!configDirty}
                type="button"
              >
                Discard edits
              </button>
              <button
                className="button button--solid"
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

              <form className="launch-form" onSubmit={handleLaunchSession}>
                <label className="field">
                  <span>Backplane</span>
                  <select
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
                      type="number"
                      min={24}
                      max={240}
                      value={launchDraft.rows}
                      onChange={(event) => updateLaunchDraft('rows', Number(event.target.value))}
                    />
                  </label>
                </div>

                <button className="button button--solid launch-button" disabled={isCreatingSession}>
                  {isCreatingSession ? 'Launching…' : 'Launch session'}
                </button>
              </form>
            </section>

            <details className="panel panel--settings" open>
              <summary>
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
                      className={`session-card${session.id === activeSessionId ? ' is-active' : ''}`}
                      onClick={() => setActiveSessionId(session.id)}
                      role="tab"
                      aria-selected={session.id === activeSessionId}
                      type="button"
                    >
                      <span className={`session-state session-state--${session.state.toLowerCase()}`}>
                        {session.state}
                      </span>
                      <strong>{session.id}</strong>
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
                <p className="eyebrow">Terminal stage</p>
                <h2>{activeSession ? activeSession.id : 'Choose or launch a session'}</h2>
                <p className="stage__lede">
                  {activeSession
                    ? 'A contained live console with stable internal scrolling and session-aware controls.'
                    : 'Launch a session to attach a docked console without leaving the workspace.'}
                </p>
              </div>

              {activeSession ? (
                <div className="stage__actions">
                  <span className={`session-state session-state--${activeSession.state.toLowerCase()}`}>
                    {activeSession.state}
                  </span>
                  <button
                    className="button button--ghost"
                    onClick={handleStopSession}
                    disabled={isStoppingSession || activeSession.state !== 'Running'}
                    type="button"
                  >
                    {isStoppingSession ? 'Stopping…' : 'Stop session'}
                  </button>
                </div>
              ) : (
                null
              )}
            </div>

            {activeSession ? (
              <div className="stage__body">
                <dl className="session-meta">
                  <div>
                    <dt>Backplane</dt>
                    <dd>{activeSession.backplaneId}</dd>
                  </div>
                  <div>
                    <dt>Host</dt>
                    <dd>{activeSession.hostId}</dd>
                  </div>
                  <div>
                    <dt>Connector</dt>
                    <dd>{activeSession.connectorId}</dd>
                  </div>
                  <div>
                    <dt>Command</dt>
                    <dd>{activeSession.displayCommand}</dd>
                  </div>
                </dl>

                <div className="stage__dock">
                  <TerminalPane
                    key={activeSession.id}
                    sessionId={activeSession.id}
                    onSessionMessage={handleSessionMessage}
                    themeMode={themeMode}
                  />
                </div>
              </div>
            ) : (
              <div className="empty-stage">
                <p className="eyebrow">No live sessions yet</p>
                <h3>Launch a session and the dock snaps into place.</h3>
                <p>
                  Start with <code>shell</code> on <code>local-host</code>, then compare Docker and
                  Ollama without losing the feel of a native console.
                </p>
                <div className="empty-stage__commands">
                  <span>pwd</span>
                  <span>ls</span>
                  <span>echo hello</span>
                </div>
              </div>
            )}
          </section>
        </main>
      </div>
    </>
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
    <article className="config-card">
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
    <article className="config-card">
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
    <article className="config-card">
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

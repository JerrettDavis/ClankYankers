import type { SessionSummary } from '../types'

export type WorkspaceLayout = 'single' | 'split-vertical' | 'split-horizontal'
export type WorkspaceTabKind = 'orchestration' | 'session'

export interface WorkspaceTab {
  id: string
  kind: WorkspaceTabKind
  title: string
  sessionId: string | null
  closable: boolean
}

export interface WorkspaceState {
  activeTabId: string
  layout: WorkspaceLayout
  secondaryTabId: string | null
  tabs: WorkspaceTab[]
}

export const orchestrationTabId = 'workspace:orchestration'

export function createInitialWorkspace(sessions: SessionSummary[]): WorkspaceState {
  const tabs = [createOrchestrationTab()]

  if (sessions[0]) {
    tabs.push(createSessionTab(sessions[0]))
  }

  return {
    activeTabId: orchestrationTabId,
    layout: 'single',
    secondaryTabId: null,
    tabs,
  }
}

export function syncWorkspaceState(
  current: WorkspaceState | null | undefined,
  sessions: SessionSummary[],
): WorkspaceState {
  const sessionById = new Map(sessions.map((session) => [session.id, session]))
  const nextTabs = (current?.tabs ?? [createOrchestrationTab()])
    .filter((tab) => tab.kind !== 'session' || (tab.sessionId !== null && sessionById.has(tab.sessionId)))
    .map((tab) => {
      if (tab.kind !== 'session' || tab.sessionId === null) {
        return tab.id === orchestrationTabId ? createOrchestrationTab() : tab
      }

      return createSessionTab(sessionById.get(tab.sessionId)!)
    })

  if (!nextTabs.some((tab) => tab.id === orchestrationTabId)) {
    nextTabs.unshift(createOrchestrationTab())
  }

  const activeTabId = nextTabs.some((tab) => tab.id === current?.activeTabId)
    ? current!.activeTabId
    : getPreferredTabId(nextTabs)

  const secondaryTabId =
    current?.secondaryTabId && current.secondaryTabId !== activeTabId && nextTabs.some((tab) => tab.id === current.secondaryTabId)
      ? current.secondaryTabId
      : null

  return {
    activeTabId,
    layout: secondaryTabId ? current?.layout ?? 'split-vertical' : 'single',
    secondaryTabId,
    tabs: nextTabs,
  }
}

export function openSessionTab(
  current: WorkspaceState,
  session: SessionSummary,
  options?: { target?: 'primary' | 'secondary' },
): WorkspaceState {
  const nextTab = createSessionTab(session)
  const existingTab = current.tabs.find((tab) => tab.id === nextTab.id)
  const tabs = existingTab
    ? current.tabs.map((tab) => (tab.id === nextTab.id ? nextTab : tab))
    : [...current.tabs, nextTab]

  if (options?.target === 'secondary') {
    if (nextTab.id === current.activeTabId) {
      return current
    }

    return {
      ...current,
      layout: current.layout === 'single' ? 'split-vertical' : current.layout,
      secondaryTabId: nextTab.id,
      tabs,
    }
  }

  return {
    ...current,
    activeTabId: nextTab.id,
    secondaryTabId: current.secondaryTabId === nextTab.id ? null : current.secondaryTabId,
    layout: current.secondaryTabId === nextTab.id ? 'single' : current.layout,
    tabs,
  }
}

export function closeWorkspaceTab(current: WorkspaceState, tabId: string): WorkspaceState {
  if (tabId === orchestrationTabId) {
    return current
  }

  const tabs = current.tabs.filter((tab) => tab.id !== tabId)
  const fallbackTabId = getPreferredTabId(tabs)

  const activeTabId = current.activeTabId === tabId ? fallbackTabId : current.activeTabId
  const secondaryTabId =
    current.secondaryTabId === tabId || current.secondaryTabId === activeTabId ? null : current.secondaryTabId

  return {
    activeTabId,
    layout: secondaryTabId ? current.layout : 'single',
    secondaryTabId,
    tabs,
  }
}

export function setWorkspaceLayout(current: WorkspaceState, layout: WorkspaceLayout): WorkspaceState {
  if (layout === 'single') {
    return {
      ...current,
      layout,
      secondaryTabId: null,
    }
  }

  if (!current.secondaryTabId) {
    return current
  }

  return {
    ...current,
    layout,
  }
}

export function focusWorkspaceTab(current: WorkspaceState, tabId: string): WorkspaceState {
  if (!current.tabs.some((tab) => tab.id === tabId)) {
    return current
  }

  const secondaryTabId = current.secondaryTabId === tabId ? null : current.secondaryTabId

  return {
    ...current,
    activeTabId: tabId,
    layout: secondaryTabId ? current.layout : 'single',
    secondaryTabId,
  }
}

export function ensureSecondarySession(current: WorkspaceState, sessions: SessionSummary[]): WorkspaceState {
  if (current.secondaryTabId) {
    return current
  }

  const reservedTabIds = new Set<string>()
  const primaryTabId = getPrimarySessionTabId(current)

  if (primaryTabId) {
    reservedTabIds.add(primaryTabId)
  }

  if (current.secondaryTabId) {
    reservedTabIds.add(current.secondaryTabId)
  }

  const candidate = sessions.find((session) => !reservedTabIds.has(getSessionTabId(session.id)))
  if (!candidate) {
    return current
  }

  return openSessionTab(current, candidate, { target: 'secondary' })
}

export function getSessionTabId(sessionId: string): string {
  return `session:${sessionId}`
}

export function isSessionTab(tab: WorkspaceTab | undefined | null): tab is WorkspaceTab & { sessionId: string } {
  return Boolean(tab && tab.kind === 'session' && tab.sessionId)
}

function createOrchestrationTab(): WorkspaceTab {
  return {
    closable: false,
    id: orchestrationTabId,
    kind: 'orchestration',
    sessionId: null,
    title: 'Orchestration',
  }
}

function createSessionTab(session: SessionSummary): WorkspaceTab {
  return {
    closable: true,
    id: getSessionTabId(session.id),
    kind: 'session',
    sessionId: session.id,
    title: session.id,
  }
}

function getPreferredTabId(tabs: WorkspaceTab[]): string {
  return tabs.find((tab) => tab.kind === 'session')?.id ?? tabs[0]?.id ?? orchestrationTabId
}

function getPrimarySessionTabId(current: WorkspaceState): string | null {
  if (current.activeTabId !== orchestrationTabId) {
    return current.activeTabId
  }

  return current.tabs.find((tab) => tab.kind === 'session')?.id ?? null
}

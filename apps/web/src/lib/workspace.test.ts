import { describe, expect, it } from 'vitest'

import type { SessionSummary } from '../types'
import {
  closeWorkspaceTab,
  createInitialWorkspace,
  ensureSecondarySession,
  focusWorkspaceTab,
  getSessionTabId,
  openSessionTab,
  orchestrationTabId,
  setWorkspaceLayout,
  syncWorkspaceState,
} from './workspace'

const sessions: SessionSummary[] = [
  {
    id: 'alpha',
    backplaneId: 'local',
    hostId: 'local-host',
    connectorId: 'shell',
    createdAt: '2026-01-01T00:00:00Z',
    displayCommand: 'pwsh',
    endedAt: null,
    error: null,
    exitCode: null,
    startedAt: '2026-01-01T00:00:00Z',
    state: 'Running',
  },
  {
    id: 'beta',
    backplaneId: 'docker',
    hostId: 'docker-host',
    connectorId: 'shell',
    createdAt: '2026-01-01T00:00:00Z',
    displayCommand: 'bash',
    endedAt: null,
    error: null,
    exitCode: null,
    startedAt: '2026-01-01T00:00:00Z',
    state: 'Running',
  },
]

describe('workspace helpers', () => {
  it('creates an orchestration tab and first session by default', () => {
    const workspace = createInitialWorkspace(sessions)

    expect(workspace.tabs.map((tab) => tab.id)).toEqual([orchestrationTabId, getSessionTabId('alpha')])
    expect(workspace.activeTabId).toBe(orchestrationTabId)
    expect(workspace.layout).toBe('single')
  })

  it('opens a session into the secondary pane when requested', () => {
    const workspace = ensureSecondarySession(createInitialWorkspace(sessions), sessions)

    expect(workspace.secondaryTabId).toBe(getSessionTabId('beta'))
    expect(workspace.layout).toBe('split-vertical')
  })

  it('collapses back to single-pane when the secondary tab closes', () => {
    const splitWorkspace = ensureSecondarySession(createInitialWorkspace(sessions), sessions)
    const workspace = closeWorkspaceTab(splitWorkspace, getSessionTabId('beta'))

    expect(workspace.secondaryTabId).toBeNull()
    expect(workspace.layout).toBe('single')
  })

  it('clears the compare pane when the active tab closes onto the secondary tab', () => {
    const splitWorkspace = openSessionTab(
      openSessionTab(createInitialWorkspace(sessions), sessions[0]),
      sessions[1],
      { target: 'secondary' },
    )
    const workspace = closeWorkspaceTab(splitWorkspace, getSessionTabId('alpha'))

    expect(workspace.activeTabId).toBe(getSessionTabId('beta'))
    expect(workspace.secondaryTabId).toBeNull()
    expect(workspace.layout).toBe('single')
  })

  it('focuses a compare tab without duplicating it into both panes', () => {
    const splitWorkspace = openSessionTab(
      openSessionTab(createInitialWorkspace(sessions), sessions[0]),
      sessions[1],
      { target: 'secondary' },
    )
    const workspace = focusWorkspaceTab(splitWorkspace, getSessionTabId('beta'))

    expect(workspace.activeTabId).toBe(getSessionTabId('beta'))
    expect(workspace.secondaryTabId).toBeNull()
    expect(workspace.layout).toBe('single')
  })

  it('drops missing session tabs during sync', () => {
    const workspace = openSessionTab(createInitialWorkspace(sessions), sessions[1])
    const synced = syncWorkspaceState(workspace, [sessions[0]])

    expect(synced.tabs.map((tab) => tab.id)).toEqual([orchestrationTabId, getSessionTabId('alpha')])
    expect(synced.activeTabId).toBe(getSessionTabId('alpha'))
  })

  it('does not enable split layout without a secondary tab', () => {
    const workspace = setWorkspaceLayout(createInitialWorkspace(sessions), 'split-horizontal')

    expect(workspace.layout).toBe('single')
    expect(workspace.secondaryTabId).toBeNull()
  })
})

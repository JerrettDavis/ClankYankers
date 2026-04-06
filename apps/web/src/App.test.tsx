import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('./components/TerminalPane', () => ({
  TerminalPane: () => <div data-testid="terminal-pane" />,
}))

import App from './App'
import type { AppStateResponse } from './types'

const appState: AppStateResponse = {
  config: {
    version: 1,
    backplanes: [
      { id: 'local', displayName: 'Local', kind: 'local', enabled: true },
      { id: 'docker', displayName: 'Docker', kind: 'docker', enabled: true },
    ],
    hosts: [
      {
        id: 'local-host',
        backplaneId: 'local',
        displayName: 'This machine',
        shellExecutable: 'pwsh.exe',
        shellArguments: ['-NoLogo'],
        workingDirectory: null,
        dockerEndpoint: null,
        dockerImage: null,
        enabled: true,
      },
    ],
    connectors: [
      {
        id: 'shell',
        displayName: 'Shell',
        kind: 'shell',
        launchCommand: null,
        launchArguments: [],
        defaultModel: null,
        defaultPermissionMode: null,
        allowedTools: [],
        skipPermissions: false,
        enabled: true,
      },
    ],
  },
  sessions: [],
  claudeHome: {
    rootDisplayPath: '~/.claude',
    exists: true,
    agentCount: 0,
    skillCount: 0,
    commandCount: 0,
    mcpArtifactCount: 0,
    settings: null,
  },
}

describe('App', () => {
  beforeEach(() => {
    window.location.hash = '#/workspace'
    const defaultClaudeCatalogResponses = [
      {
        agents: [{ name: 'frontend-developer', commandCount: 0 }],
        skills: [{ name: 'brainstorming', commandCount: 1 }],
      },
    ]
    let catalogRequestCount = 0
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        if (String(input).endsWith('/api/app-state')) {
          return new Response(JSON.stringify(appState), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          })
        }

        if (String(input).endsWith('/api/claude-home/catalog')) {
          const response =
            defaultClaudeCatalogResponses[Math.min(catalogRequestCount, defaultClaudeCatalogResponses.length - 1)]
          catalogRequestCount += 1

          return new Response(JSON.stringify(response), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          })
        }

        return new Response(JSON.stringify([]), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }),
    )
  })

  afterEach(() => {
    window.location.hash = ''
    vi.unstubAllGlobals()
  })

  it('renders the studio shell and workspace launch controls', async () => {
    render(<App />)

    expect(await screen.findByRole('heading', { name: /terminal work stays first-class/i })).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: /new session/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /launch session/i })).toBeInTheDocument()
    expect(screen.getAllByText(/no live sessions yet/i).length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: /save config/i })).toBeInTheDocument()
    expect(screen.getByTestId('nav-section-overview')).toBeInTheDocument()
  })

  it('loads and refreshes the sanitized Claude agent catalog explicitly', async () => {
    window.location.hash = '#/agents'

    const fetchMock = vi.mocked(fetch)
    const claudeCatalogResponses = [
      {
        agents: [{ name: 'frontend-developer', commandCount: 0 }],
        skills: [{ name: 'brainstorming', commandCount: 1 }],
      },
      {
        agents: [{ name: 'security-reviewer', commandCount: 0 }],
        skills: [{ name: 'brainstorming', commandCount: 1 }],
      },
    ]
    const claudeAwareState: AppStateResponse = {
      ...appState,
      claudeHome: {
        rootDisplayPath: '~/.claude',
        exists: true,
        agentCount: 1,
        skillCount: 1,
        commandCount: 1,
        mcpArtifactCount: 0,
        settings: {
          hasLocalOverrides: true,
          statusLineType: 'command',
          hasStatusLineCommand: true,
          voiceEnabled: true,
          skipDangerousModePermissionPrompt: false,
          enabledPluginCount: 2,
        },
      },
    }

    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      if (String(input).endsWith('/api/app-state')) {
        return new Response(JSON.stringify(claudeAwareState), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }

      if (String(input).endsWith('/api/claude-home/catalog')) {
        const response = claudeCatalogResponses.shift() ?? claudeCatalogResponses.at(-1)
        return new Response(JSON.stringify(response), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }

      return new Response(JSON.stringify([]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    })

    render(<App />)

    expect(await screen.findByRole('heading', { name: /local claude agent catalog/i })).toBeInTheDocument()
    expect(await screen.findByText('frontend-developer')).toBeInTheDocument()

    fireEvent.click(screen.getAllByTestId('refresh-sessions')[0])

    await waitFor(() => {
      expect(screen.getByText('security-reviewer')).toBeInTheDocument()
    })
  })
})

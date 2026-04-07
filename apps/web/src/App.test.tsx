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
        workingDirectory: 'C:\\git\\ClankYankers',
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
      {
        id: 'claude-team',
        displayName: 'Claude Team',
        kind: 'claude',
        launchCommand: 'claude',
        launchArguments: ['--verbose'],
        defaultModel: 'sonnet-4.6',
        defaultPermissionMode: 'plan',
        allowedTools: ['Read', 'Bash(git status)'],
        skipPermissions: false,
        enabled: true,
      },
    ],
    experiments: [
      {
        id: 'local-shell-smoke',
        displayName: 'Local shell smoke',
        description: 'Smoke test for the local shell.',
        hostIds: ['local-host'],
        connectorIds: ['shell'],
        models: [],
        cols: 120,
        rows: 34,
        enabled: true,
      },
    ],
  },
  sessions: [],
  experimentRuns: [],
  claudeHome: {
    rootDisplayPath: '~/.claude',
    exists: true,
    agentCount: 1,
    skillCount: 1,
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
    expect(screen.getByRole('main')).toHaveClass('product-main--workspace')
    expect(screen.getByTestId('studio-sidebar')).toHaveAttribute('aria-hidden', 'true')
    expect(screen.getByTestId('workspace-studio-nav-toggle')).toHaveTextContent(/show studio nav/i)
    expect(screen.getByTestId('workspace-launch-blade')).toHaveAttribute('aria-hidden', 'true')
    fireEvent.click(screen.getByTestId('workspace-studio-nav-toggle'))
    expect(screen.getByTestId('studio-sidebar')).toHaveAttribute('aria-hidden', 'false')
    expect(screen.getByTestId('workspace-studio-nav-toggle')).toHaveTextContent(/hide studio nav/i)
    fireEvent.click(screen.getByRole('button', { name: /new session/i }))
    expect(screen.getByTestId('studio-sidebar')).toHaveAttribute('aria-hidden', 'true')
    expect(await screen.findByRole('heading', { name: /new session/i })).toBeInTheDocument()
    expect(screen.getByTestId('launch-working-directory')).toHaveValue('C:\\git\\ClankYankers')
    expect(screen.getByRole('button', { name: /launch session/i })).toBeInTheDocument()
    expect(screen.getAllByText(/no live sessions yet/i).length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: /save config/i })).toBeInTheDocument()
    expect(screen.getByTestId('nav-section-overview')).toBeInTheDocument()
  })

  it('keeps direct workspace deep links on the workspace section', async () => {
    window.location.hash = ''
    window.history.replaceState(null, '', '/workspace')

    render(<App />)

    expect(await screen.findByTestId('product-page-workspace')).toBeInTheDocument()
    expect(window.location.hash).toBe('#/workspace')
  })

  it('does not serialize inherited workspace folders as explicit overrides', async () => {
    let createPayload: Record<string, unknown> | null = null
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input).endsWith('/api/app-state')) {
        return new Response(JSON.stringify(appState), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }

      if (String(input).endsWith('/api/sessions') && init?.method === 'POST') {
        createPayload = JSON.parse(String(init.body ?? '{}')) as Record<string, unknown>
        return new Response(
          JSON.stringify({
            id: 'session-1',
            experimentId: null,
            backplaneId: 'local',
            hostId: 'local-host',
            connectorId: 'shell',
            displayCommand: 'pwsh.exe -NoLogo',
            state: 'Running',
            createdAt: new Date().toISOString(),
            startedAt: new Date().toISOString(),
            endedAt: null,
            exitCode: null,
            error: null,
          }),
          {
            status: 201,
            headers: { 'Content-Type': 'application/json' },
          },
        )
      }

      return new Response(JSON.stringify([]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    })

    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: /new session/i }))
    expect(await screen.findByTestId('launch-working-directory')).toHaveValue('C:\\git\\ClankYankers')
    fireEvent.click(screen.getByRole('button', { name: /launch session/i }))

    await waitFor(() => {
      expect(createPayload).toMatchObject({
        hostId: 'local-host',
        connectorId: 'shell',
        model: null,
        permissionMode: null,
        skipPermissions: null,
        allowedTools: null,
        agent: null,
        workingDirectory: null,
      })
    })
  })

  it('posts an edited workspace folder when launching a session', async () => {
    let createPayload: Record<string, unknown> | null = null
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input).endsWith('/api/app-state')) {
        return new Response(JSON.stringify(appState), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }

      if (String(input).endsWith('/api/sessions') && init?.method === 'POST') {
        createPayload = JSON.parse(String(init.body ?? '{}')) as Record<string, unknown>
        return new Response(
          JSON.stringify({
            id: 'session-1',
            experimentId: null,
            backplaneId: 'local',
            hostId: 'local-host',
            connectorId: 'shell',
            displayCommand: 'pwsh.exe -NoLogo',
            state: 'Running',
            createdAt: new Date().toISOString(),
            startedAt: new Date().toISOString(),
            endedAt: null,
            exitCode: null,
            error: null,
          }),
          {
            status: 201,
            headers: { 'Content-Type': 'application/json' },
          },
        )
      }

      return new Response(JSON.stringify([]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    })

    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: /new session/i }))
    const workspaceFolder = await screen.findByTestId('launch-working-directory')
    fireEvent.change(workspaceFolder, { target: { value: 'C:\\Users\\jd\\source' } })
    fireEvent.click(screen.getByRole('button', { name: /launch session/i }))

    await waitFor(() => {
      expect(createPayload).toMatchObject({
        hostId: 'local-host',
        connectorId: 'shell',
        model: null,
        permissionMode: null,
        skipPermissions: null,
        allowedTools: null,
        agent: null,
        workingDirectory: 'C:\\Users\\jd\\source',
      })
    })
  })

  it('shows Claude session settings and posts explicit connector overrides', async () => {
    let createPayload: Record<string, unknown> | null = null
    const fetchMock = vi.mocked(fetch)
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input).endsWith('/api/app-state')) {
        return new Response(JSON.stringify(appState), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }

      if (String(input).endsWith('/api/claude-home/catalog')) {
        return new Response(
          JSON.stringify({
            agents: [{ name: 'frontend-developer', commandCount: 0 }],
            skills: [{ name: 'brainstorming', commandCount: 1 }],
          }),
          {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          },
        )
      }

      if (String(input).endsWith('/api/sessions') && init?.method === 'POST') {
        createPayload = JSON.parse(String(init.body ?? '{}')) as Record<string, unknown>
        return new Response(
          JSON.stringify({
            id: 'session-claude',
            experimentId: null,
            backplaneId: 'local',
            hostId: 'local-host',
            connectorId: 'claude-team',
            displayCommand: 'claude --verbose',
            state: 'Running',
            createdAt: new Date().toISOString(),
            startedAt: new Date().toISOString(),
            endedAt: null,
            exitCode: null,
            error: null,
          }),
          {
            status: 201,
            headers: { 'Content-Type': 'application/json' },
          },
        )
      }

      return new Response(JSON.stringify([]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    })

    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: /new session/i }))
    fireEvent.change(await screen.findByTestId('launch-connector'), { target: { value: 'claude-team' } })

    expect(await screen.findByTestId('launch-permission-mode')).toBeInTheDocument()
    expect(screen.getByTestId('launch-agent')).toBeInTheDocument()
    expect(screen.getByTestId('launch-allowed-tools')).toBeInTheDocument()

    fireEvent.change(screen.getByTestId('launch-model'), { target: { value: 'opus-4.6' } })
    fireEvent.change(screen.getByTestId('launch-permission-mode'), { target: { value: 'acceptEdits' } })
    fireEvent.click(screen.getByTestId('launch-skip-permissions'))
    fireEvent.change(screen.getByTestId('launch-agent'), { target: { value: 'frontend-developer' } })
    fireEvent.click(screen.getByLabelText('Task'))
    fireEvent.change(screen.getByTestId('launch-custom-tools'), { target: { value: 'Bash(git diff *)' } })
    fireEvent.click(screen.getByRole('button', { name: /launch session/i }))

    await waitFor(() => {
      expect(createPayload).toMatchObject({
        connectorId: 'claude-team',
        model: 'opus-4.6',
        permissionMode: 'acceptEdits',
        skipPermissions: true,
        allowedTools: ['Read', 'Bash(git status)', 'Task', 'Bash(git diff *)'],
        agent: 'frontend-developer',
      })
    })
  })

  it('launches an experiment from the lab surface', async () => {
    window.location.hash = '#/lab'

    const fetchMock = vi.mocked(fetch)
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input).endsWith('/api/app-state')) {
        return new Response(JSON.stringify(appState), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }

      if (String(input).endsWith('/api/experiments/local-shell-smoke/runs') && init?.method === 'POST') {
        return new Response(
          JSON.stringify({
            id: 'run-1',
            experimentId: 'local-shell-smoke',
            experimentDisplayName: 'Local shell smoke',
            experimentDescription: 'Smoke test for the local shell.',
            createdAt: new Date().toISOString(),
            activeSessionCount: 1,
            variantCount: 1,
            variants: [
              {
                sessionId: 'session-1',
                backplaneId: 'local',
                hostId: 'local-host',
                connectorId: 'shell',
                model: null,
              },
            ],
          }),
          {
            status: 201,
            headers: { 'Content-Type': 'application/json' },
          },
        )
      }

      if (String(input).endsWith('/api/sessions')) {
        return new Response(JSON.stringify([]), {
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

    expect(await screen.findByRole('heading', { name: /experiment matrix and recent runs/i })).toBeInTheDocument()

    fireEvent.click(screen.getAllByTestId('run-experiment-local-shell-smoke')[0])

    await waitFor(() => {
      expect(screen.getByText('1/1 active')).toBeInTheDocument()
    })
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

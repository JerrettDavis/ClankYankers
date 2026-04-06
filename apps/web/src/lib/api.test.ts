import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { AppConfig, ClaudeHomeCatalogResponse } from '../types'
import { loadClaudeHomeCatalog, runExperiment, saveConfig, stopSession } from './api'

describe('api client', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('accepts empty successful stop responses', async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(null, {
        status: 202,
      }),
    )

    await expect(stopSession('session-123')).resolves.toBeUndefined()
  })

  it('parses application/problem+json validation errors', async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(
        JSON.stringify({
          title: 'One or more validation errors occurred.',
          errors: {
            'hosts.backplaneId': ['Unknown backplane references: bad-id'],
          },
        }),
        {
          status: 400,
          headers: { 'Content-Type': 'application/problem+json' },
        },
      ),
    )

    const invalidConfig: AppConfig = {
      version: 1,
      backplanes: [{ id: 'local', displayName: 'Local', kind: 'local', enabled: true }],
      hosts: [
        {
          id: 'broken-host',
          backplaneId: 'bad-id',
          displayName: 'Broken',
          shellExecutable: 'pwsh.exe',
          shellArguments: [],
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
      experiments: [],
    }

    await expect(saveConfig(invalidConfig)).rejects.toMatchObject({
      status: 400,
      details: {
        errors: {
          'hosts.backplaneId': ['Unknown backplane references: bad-id'],
        },
      },
    })
  })

  it('loads the explicit Claude home catalog endpoint', async () => {
    const catalog: ClaudeHomeCatalogResponse = {
      agents: [{ name: 'frontend-developer', commandCount: 0 }],
      skills: [{ name: 'brainstorming', commandCount: 1 }],
    }

    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify(catalog), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )

    await expect(loadClaudeHomeCatalog()).resolves.toEqual(catalog)
  })

  it('starts an experiment run from the lab endpoint', async () => {
    const run = {
      id: 'run-1',
      experimentId: 'local-shell-smoke',
      experimentDisplayName: 'Local shell smoke',
      experimentDescription: 'Smoke test for the local shell.',
      createdAt: '2026-04-06T00:00:00Z',
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
    }

    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify(run), {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      }),
    )

    await expect(runExperiment('local-shell-smoke')).resolves.toEqual(run)
  })
})

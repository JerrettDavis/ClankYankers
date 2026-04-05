import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { AppConfig } from '../types'
import { saveConfig, stopSession } from './api'

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
})

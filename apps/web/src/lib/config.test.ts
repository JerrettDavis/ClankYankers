import { describe, expect, it } from 'vitest'

import type { AppConfig } from '../types'
import { coerceLaunchDraft } from './config'

const config: AppConfig = {
  version: 1,
  backplanes: [{ id: 'local', displayName: 'Local', kind: 'local', enabled: true }],
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
      id: 'claude',
      displayName: 'Claude',
      kind: 'claude',
      launchCommand: 'claude',
      launchArguments: [],
      defaultModel: null,
      defaultPermissionMode: 'default',
      allowedTools: [],
      skipPermissions: false,
      enabled: true,
    },
  ],
}

describe('config helpers', () => {
  it('canonicalizes case-insensitive draft ids to saved option values', () => {
    const draft = coerceLaunchDraft(config, {
      backplaneId: 'LOCAL',
      hostId: 'LOCAL-HOST',
      connectorId: 'CLAUDE',
    })

    expect(draft.backplaneId).toBe('local')
    expect(draft.hostId).toBe('local-host')
    expect(draft.connectorId).toBe('claude')
  })
})

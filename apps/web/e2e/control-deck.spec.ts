import { execFileSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import fs from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'

const here = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(here, '..', '..', '..')
const configPath = path.join(repoRoot, 'data', 'config.json')
const logPath = path.join(repoRoot, 'data', 'logs', 'sessions.ndjson')

const originalConfigText = readFileSync(configPath, 'utf8')
const originalConfig = JSON.parse(stripBom(originalConfigText)) as Record<string, unknown>
const hadOriginalLog = existsSync(logPath)
const originalLogText = hadOriginalLog ? readFileSync(logPath, 'utf8') : ''

const dockerAvailable = commandAvailable('docker', ['version', '--format', '{{.Server.Version}}'])
const ollamaAvailable = commandAvailable('ollama', ['show', 'qwen3.5:9b'])

test.describe.configure({ mode: 'serial' })

test.beforeEach(async ({ request }) => {
  await restoreBaselineConfig(request)
  await stopAllSessions(request)
  await clearAuditLog()
})

test.afterEach(async ({ request }) => {
  await stopAllSessions(request)
  await restoreBaselineConfig(request)
  await clearAuditLog()
})

test.afterAll(async () => {
  await fs.writeFile(configPath, originalConfigText, 'utf8')

  if (hadOriginalLog) {
    await fs.writeFile(logPath, originalLogText, 'utf8')
    return
  }

  await fs.rm(logPath, { force: true })
})

test('adapts to system theme and stays locked to the viewport', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'light' })
  await openDeck(page)
  const lightMasthead = await page.locator('.masthead').evaluate((element) => getComputedStyle(element).backgroundColor)

  await page.emulateMedia({ colorScheme: 'dark' })
  await page.reload()
  await waitForDeck(page)
  const darkMasthead = await page.locator('.masthead').evaluate((element) => getComputedStyle(element).backgroundColor)

  expect(darkMasthead).not.toBe(lightMasthead)

  for (const viewport of [
    { width: 1440, height: 1000 },
    { width: 900, height: 700 },
    { width: 480, height: 820 },
  ]) {
    await page.setViewportSize(viewport)
    await page.reload()
    await waitForDeck(page)

    const pageMetrics = await page.evaluate(() => ({
      innerHeight: window.innerHeight,
      scrollHeight: document.documentElement.scrollHeight,
      bodyScrollHeight: document.body.scrollHeight,
      scrollY: window.scrollY,
    }))
    const mastheadHeight = await page.locator('.masthead').evaluate((element) => element.getBoundingClientRect().height)

    expect(pageMetrics.scrollHeight).toBe(pageMetrics.innerHeight)
    expect(pageMetrics.bodyScrollHeight).toBe(pageMetrics.innerHeight)
    expect(pageMetrics.scrollY).toBe(0)
    expect(mastheadHeight).toBeLessThanOrEqual(Math.round(viewport.height * 0.24))
  }
})

test('scaffolds dashboard sections across the studio shell', async ({ page }) => {
  await openDeck(page)

  await expect(page.getByRole('heading', { name: 'One shell, multiple operating surfaces' })).toBeVisible()

  await openSection(page, 'workspace', 'Terminal work stays first-class')
  await openSection(page, 'sessions', 'Browse the active runtime manifest')
  await openSection(page, 'backplanes', 'Backplane registry')
  await openSection(page, 'hosts', 'Launch hosts')
  await openSection(page, 'connectors', 'Connector definitions')
  await openSection(page, 'lab', 'Experiment matrix and recent runs')
  await openSection(page, 'agents', 'Local Claude agent catalog')
  await openSection(page, 'skills', 'Local Claude skill catalog')
  await openSection(page, 'mcp', 'Claude MCP and plugin surfaces')
  await openSection(page, 'settings', 'Studio settings and operating posture')
})

test('surfaces Claude home catalog and status across studio pages', async ({ page }) => {
  await openDeck(page)

  await openSection(page, 'agents', 'Local Claude agent catalog')
  const agentCountText = page.getByText(/(?:Loaded from ~/.claude via a sanitized catalog endpoint|Suggested starter roles)/);
  await expect(agentCountText.first()).toBeVisible();
  await page.getByTestId('refresh-sessions').click()
  await expect(page.getByRole('heading', { name: 'Local Claude agent catalog' })).toBeVisible()

  await openSection(page, 'skills', 'Local Claude skill catalog')
  const skillDetail = page.getByText(/(?:without exposing file paths or frontmatter copy|Starter bundles for recovery)/);
  await expect(skillDetail.first()).toBeVisible();

  await openSection(page, 'settings', 'Studio settings and operating posture')
  await expect(page.getByRole('heading', { name: 'Local integration status' })).toBeVisible()
  await expect(page.getByText('~/.claude', { exact: true })).toBeVisible()
})

test('launches a structured experiment run from the lab', async ({ page, request }) => {
  await openDeck(page)

  await openSection(page, 'lab', 'Experiment matrix and recent runs')
  await page.getByTestId('run-experiment-local-shell-smoke').first().click()
  await expect(page.getByText('1/1 active')).toBeVisible()

  let sessions = await listSessions(request)
  await expect
    .poll(async () => {
      sessions = await listSessions(request)
      return sessions.length
    }, { timeout: 30_000 })
    .toBe(1)
  const [session] = sessions

  await expect(page.getByRole('button', { name: /Open [a-z0-9…-]+/i }).first()).toBeVisible()
  await page.getByRole('button', { name: /Open [a-z0-9…-]+/i }).first().click()
  await expect(page.getByRole('heading', { name: 'Terminal work stays first-class' })).toBeVisible()
  await expect(page.getByTestId(`workspace-tab-session:${session.id}`)).toBeVisible()
})

test('persists config changes and discards unsaved edits', async ({ page }) => {
  await openDeck(page)
  await openWorkspace(page)
  await openConfigPanel(page)

  const localBackplaneCard = page.getByTestId('backplane-card-local')
  await localBackplaneCard.getByLabel('Name').fill('Local E2E')

  await expect(page.getByTestId('save-config')).toBeEnabled()
  await page.getByTestId('save-config').click()
  await expect(page.getByText(/config saved/i)).toBeVisible()

  await page.reload()
  await waitForDeck(page)
  await expect(page.getByTestId('launch-backplane')).toContainText('Local E2E')

  await openConfigPanel(page)
  const reloadedLocalBackplaneCard = page.getByTestId('backplane-card-local')
  await reloadedLocalBackplaneCard.getByLabel('Name').fill('Local Temp')

  await expect(page.getByTestId('discard-config')).toBeEnabled()
  await page.getByTestId('discard-config').click()
  await expect(reloadedLocalBackplaneCard.getByLabel('Name')).toHaveValue('Local E2E')

  const claudeConnectorCard = page.getByTestId('connector-card-claude')
  await claudeConnectorCard.getByLabel('Base arguments').fill('--verbose')
  await claudeConnectorCard.getByLabel('Permission mode').fill('plan')
  await claudeConnectorCard.getByLabel('Allowed tools').fill('Read, Edit')

  await page.getByTestId('save-config').click()
  await expect(page.getByText(/config saved/i)).toBeVisible()

  await page.reload()
  await waitForDeck(page)
  await openLaunchBlade(page)
  await page.getByTestId('launch-connector').selectOption('claude')
  await expect(page.getByTestId('launch-connector-command')).toContainText('claude --verbose')

  await openConfigPanel(page)
  const reloadedClaudeConnectorCard = page.getByTestId('connector-card-claude')
  await expect(reloadedClaudeConnectorCard.getByLabel('Permission mode')).toHaveValue('plan')
  await expect(reloadedClaudeConnectorCard.getByLabel('Allowed tools')).toHaveValue('Read, Edit')
})

test('persists SSH and remote host settings through the config editor', async ({ page, request }) => {
  await openDeck(page)
  await openWorkspace(page)
  await openConfigPanel(page)

  const sshHostCard = page.getByTestId('host-card-ssh-example')
  await expect(sshHostCard.getByTestId('host-ssh-address')).toBeVisible()
  await expect(sshHostCard.getByTestId('host-ssh-username')).toBeVisible()

  await sshHostCard.getByTestId('host-ssh-address').fill('ssh.example.test')
  await sshHostCard.getByTestId('host-ssh-port').fill('2222')
  await sshHostCard.getByTestId('host-ssh-username').fill('clanky')
  await sshHostCard.getByTestId('host-ssh-private-key-path').fill('/home/clanky/.ssh/id_ed25519')
  await sshHostCard.getByTestId('host-ssh-certificate-path').fill('/home/clanky/.ssh/id_ed25519-cert.pub')
  await sshHostCard.getByTestId('host-ssh-allow-any-host-key').check()
  await sshHostCard.getByTestId('host-ssh-use-keyboard-interactive').check()

  const remoteHostCard = page.getByTestId('host-card-remote-example')
  await expect(remoteHostCard.getByTestId('host-remote-daemon-url')).toBeVisible()
  await expect(remoteHostCard.getByTestId('host-remote-executor-kind')).toBeVisible()

  await remoteHostCard.getByTestId('host-remote-daemon-url').fill('https://remote.example.test')
  await remoteHostCard.getByTestId('host-remote-access-token').fill('top-secret-token')
  await remoteHostCard.getByTestId('host-remote-allow-insecure-tls').check()
  await remoteHostCard.getByTestId('host-remote-executor-kind').selectOption('docker')
  await expect(remoteHostCard.getByTestId('host-remote-docker-endpoint')).toBeVisible()
  await expect(remoteHostCard.getByTestId('host-remote-docker-image')).toBeVisible()
  await remoteHostCard.getByTestId('host-remote-docker-endpoint').fill('unix:///var/run/docker.sock')
  await remoteHostCard.getByTestId('host-remote-docker-image').fill('ghcr.io/clanky/remote-agent:latest')

  await page.getByTestId('save-config').click()
  await expect(page.getByText(/config saved/i)).toBeVisible()

  const response = await request.get('http://127.0.0.1:5023/api/config')
  expect(response.ok()).toBeTruthy()
  const savedConfig = (await response.json()) as {
    hosts: Array<{
      id: string
      sshAddress?: string | null
      sshPort?: number | null
      sshUsername?: string | null
      sshPrivateKeyPath?: string | null
      sshCertificatePath?: string | null
      sshAllowAnyHostKey?: boolean
      sshUseKeyboardInteractive?: boolean
      remoteDaemonUrl?: string | null
      remoteAccessToken?: string | null
      remoteAllowInsecureTls?: boolean
      remoteExecutorKind?: string | null
      remoteDockerEndpoint?: string | null
      remoteDockerImage?: string | null
    }>
  }

  const savedSshHost = savedConfig.hosts.find((host) => host.id === 'ssh-example')
  expect(savedSshHost).toBeTruthy()
  expect(savedSshHost?.sshAddress).toBe('ssh.example.test')
  expect(savedSshHost?.sshPort).toBe(2222)
  expect(savedSshHost?.sshUsername).toBe('clanky')
  expect(savedSshHost?.sshPrivateKeyPath).toBe('/home/clanky/.ssh/id_ed25519')
  expect(savedSshHost?.sshCertificatePath).toBe('/home/clanky/.ssh/id_ed25519-cert.pub')
  expect(savedSshHost?.sshAllowAnyHostKey).toBe(true)
  expect(savedSshHost?.sshUseKeyboardInteractive).toBe(true)

  const savedRemoteHost = savedConfig.hosts.find((host) => host.id === 'remote-example')
  expect(savedRemoteHost).toBeTruthy()
  expect(savedRemoteHost?.remoteDaemonUrl).toBe('https://remote.example.test')
  expect(savedRemoteHost?.remoteAccessToken).toBe('top-secret-token')
  expect(savedRemoteHost?.remoteAllowInsecureTls).toBe(true)
  expect(savedRemoteHost?.remoteExecutorKind).toBe('docker')
  expect(savedRemoteHost?.remoteDockerEndpoint).toBe('unix:///var/run/docker.sock')
  expect(savedRemoteHost?.remoteDockerImage).toBe('ghcr.io/clanky/remote-agent:latest')
})

test('shows connector-specific launch overrides for client CLIs', async ({ page }) => {
  await openDeck(page)
  await openWorkspace(page)
  await openLaunchBlade(page)

  await page.getByTestId('launch-connector').selectOption('claude')
  await expect(page.getByTestId('launch-overrides')).toBeVisible()
  await expect(page.getByTestId('launch-model')).toBeVisible()

  await page.getByTestId('launch-connector').selectOption('ollama')
  await expect(page.getByTestId('launch-overrides')).toBeVisible()
  await expect(page.getByTestId('launch-model')).toBeVisible()

  await page.getByTestId('launch-connector').selectOption('shell')
  await expect(page.getByTestId('launch-overrides')).toHaveCount(0)
  await expect(page.getByText(/uses the selected host shell/i)).toBeVisible()
})

test('runs local shell flows end to end and records audit events', async ({ page, request }) => {
  await openDeck(page)
  await openWorkspace(page)

  const sessionId = await launchSession(page, request)
  const primaryPane = page.getByTestId('workspace-pane-primary')
  const socket = await attachSessionSocket(sessionId)

  try {
    await expectTerminalReady(primaryPane)

    await page.setViewportSize({ width: 1180, height: 820 })
    socket.sendInput("Write-Output 'e2e-local'\r\n")
    await expect.poll(() => flattenTerminalTranscript(socket.transcript())).toContain('e2e-local')
    await waitForPromptAfter(socket, 'e2e-local')

    await page.setViewportSize({ width: 960, height: 780 })
    socket.sendInput("$name = 'Alice'\r\n")
    socket.sendInput("Write-Output \"done:$name\"\r\n")
    await expect.poll(() => flattenTerminalTranscript(socket.transcript())).toContain('done:Alice')

    socket.sendInput('exit\r\n')
    await expect.poll(async () => (await listSessions(request)).length, { timeout: 30_000 }).toBe(0)

    await page.getByTestId('refresh-sessions').click()
    await expect(page.getByText(/no live sessions yet/i).first()).toBeVisible()

    await expect.poll(async () => await fs.readFile(logPath, 'utf8')).toContain(sessionId)
  } finally {
    socket.close()
  }
})

test('covers workspace orchestration, compare panes, tab close and stop flows', async ({ page, request }) => {
  await openDeck(page)
  await openWorkspace(page)

  const sessionA = await launchSession(page, request)
  const sessionB = await launchSession(page, request)
  const socketA = await attachSessionSocket(sessionA)
  const socketB = await attachSessionSocket(sessionB)
  let replaySocketA: Awaited<ReturnType<typeof attachSessionSocket>> | null = null

  try {
    await page.getByTestId('split-vertical').click()
    await expect(page.getByTestId('workspace-pane-secondary')).toBeVisible()

    const primaryPane = page.getByTestId('workspace-pane-primary')
    const secondaryPane = page.getByTestId('workspace-pane-secondary')

    await expectTerminalReady(primaryPane)
    await expectTerminalReady(secondaryPane)

    socketA.sendInput("Write-Output 'session-a-marker'\r\n")
    socketB.sendInput("Write-Output 'session-b-marker'\r\n")

    await expect.poll(() => socketA.transcript()).toContain('session-a-marker')
    await expect.poll(() => socketB.transcript()).toContain('session-b-marker')
    expect(socketA.transcript()).not.toContain('session-b-marker')
    expect(socketB.transcript()).not.toContain('session-a-marker')

    await page.setViewportSize({ width: 680, height: 820 })
    await expect(page.getByTestId('workspace-pane-secondary')).toHaveCount(0)
    await expect(page.getByTestId('split-vertical')).toBeDisabled()
    await page.getByRole('button', { name: 'Orchestration' }).click()
    await expect(page.getByTestId(`compare-session-${sessionA}`)).toBeDisabled()
    await openSection(page, 'sessions', 'Browse the active runtime manifest')
    await expect(page.getByRole('button', { name: 'Compare' }).first()).toBeDisabled()
    await openSection(page, 'overview', 'One shell, multiple operating surfaces')
    await expect(page.getByRole('button', { name: 'Compare' }).first()).toBeDisabled()
    await openWorkspace(page)

    await page.setViewportSize({ width: 1180, height: 700 })
    await expect(page.getByTestId('workspace-pane-secondary')).toHaveCount(0)
    await expect(page.getByTestId('split-vertical')).toBeDisabled()
    await expect(page.getByTestId(`compare-session-${sessionA}`)).toBeDisabled()

    await page.setViewportSize({ width: 1180, height: 820 })
    await expect(page.getByTestId('split-vertical')).toBeEnabled()
    await expect(page.getByTestId(`compare-session-${sessionA}`)).toBeEnabled()
    await page.getByTestId(`workspace-tab-session:${sessionB}`).click()
    await expect(page.getByTestId(`stop-session-${sessionB}`)).toBeVisible()
    await page.getByTestId('split-horizontal').click()
    await expect(page.getByRole('banner').getByText('Split rows')).toBeVisible()

    await secondaryPane.getByTestId('workspace-pane-picker-secondary').selectOption('workspace:orchestration')
    await expect(secondaryPane.getByText('Live sessions')).toBeVisible()

    await secondaryPane.getByTestId('workspace-pane-picker-secondary').selectOption(`session:${sessionA}`)
    await expect(secondaryPane.getByTestId(`stop-session-${sessionA}`)).toBeVisible()

    await page.getByTestId(`workspace-tab-session:${sessionB}`).click()
    await expect(page.getByTestId(`stop-session-${sessionB}`)).toBeVisible()
    await page.getByTestId(`workspace-tab-session:${sessionA}`).click()
    await expect(page.getByTestId(`stop-session-${sessionA}`)).toBeVisible()
    await expect(page.getByTestId('workspace-pane-secondary')).toHaveCount(0)

    await page.getByTestId('split-vertical').click()
    await expect(page.getByTestId('workspace-pane-secondary')).toBeVisible()
    await page.getByTestId('workspace-pane-picker-secondary').selectOption(`session:${sessionB}`)
    await expect(page.getByTestId('workspace-pane-secondary').getByTestId(`stop-session-${sessionB}`)).toBeVisible()

    await page.getByTestId('single-pane').click()
    await expect(page.getByTestId('workspace-pane-secondary')).toHaveCount(0)

    await page.getByTestId(`workspace-tab-close-session:${sessionA}`).click()
    await expect(page.getByTestId(`workspace-tab-session:${sessionA}`)).toHaveCount(0)
    await openLaunchBlade(page)
    await expect(page.getByTestId(`session-card-${sessionA}`)).toBeVisible()

    await reopenSessionFromManifest(page, sessionA)
    await closeLaunchBlade(page)
    await expect(page.getByTestId(`workspace-tab-session:${sessionA}`)).toBeVisible()
    await expect(page.getByTestId(`stop-session-${sessionA}`)).toBeVisible()

    replaySocketA = await attachSessionSocket(sessionA)
    await expect.poll(() => replaySocketA?.transcript() ?? '').toContain('session-a-marker')
    replaySocketA.sendInput("Write-Output 'session-a-history'\r\n")
    await expect.poll(() => replaySocketA?.transcript() ?? '').toContain('session-a-history')

    await page.getByTestId(`stop-session-${sessionA}`).click()
    await expect.poll(async () => (await listSessions(request)).some((session) => session.id === sessionA), { timeout: 30_000 }).toBe(false)

    await page.getByTestId(`stop-session-${sessionB}`).click()
    await expect.poll(async () => (await listSessions(request)).length, { timeout: 30_000 }).toBe(0)
  } finally {
    replaySocketA?.close()
    socketA.close()
    socketB.close()
  }
})

test('runs docker shell sessions when docker is available', async ({ page, request }) => {
  test.skip(!dockerAvailable, 'Docker is not available on this machine.')

  await openDeck(page)
  await openWorkspace(page)
  await openLaunchBlade(page)

  await page.getByTestId('launch-backplane').selectOption('docker')
  await expect(page.getByTestId('launch-host')).toHaveValue('docker-local')
  await page.getByTestId('launch-connector').selectOption('shell')

  const sessionId = await launchSession(page, request)
  const primaryPane = page.getByTestId('workspace-pane-primary')
  const socket = await attachSessionSocket(sessionId)

  try {
    await expectTerminalReady(primaryPane)
    socket.sendInput('echo docker-e2e\n')
    await expect.poll(() => socket.transcript(), { timeout: 30_000 }).toContain('docker-e2e')

    await page.getByTestId(`stop-session-${sessionId}`).click()
    await expect.poll(async () => (await listSessions(request)).length, { timeout: 30_000 }).toBe(0)
  } finally {
    socket.close()
  }
})

test('runs ollama connector sessions when the model is available', async ({ page, request }) => {
  test.skip(!ollamaAvailable, 'Ollama qwen3.5:9b is not available on this machine.')
  test.setTimeout(300_000)

  await openDeck(page)
  await openWorkspace(page)
  await openLaunchBlade(page)

  await page.getByTestId('launch-connector').selectOption('ollama')
  const sessionId = await launchSession(page, request)
  const primaryPane = page.getByTestId('workspace-pane-primary')
  const token = 'OLLAMA_E2E_TOKEN'
  const socket = await attachSessionSocket(sessionId)

  try {
    await expectTerminalReady(primaryPane)
    socket.sendInput(`Respond with exactly ${token} and nothing else.\n`)
    await expect.poll(() => socket.transcript(), { timeout: 180_000 }).toContain(token)

    await page.getByTestId(`stop-session-${sessionId}`).click()
    await expect.poll(async () => (await listSessions(request)).length, { timeout: 30_000 }).toBe(0)
  } finally {
    socket.close()
  }
})

async function openDeck(page: Page) {
  await page.goto('/')
  await waitForDeck(page)
}

async function waitForDeck(page: Page) {
  await expect(page.locator('.studio-shell')).toBeVisible()
  if (await page.getByTestId('workspace-studio-nav-toggle').count()) {
    await expect(page.getByTestId('workspace-studio-nav-toggle')).toBeVisible()
    return
  }

  await expect(page.getByTestId('nav-section-workspace')).toBeVisible()
}

async function openWorkspace(page: Page) {
  await openSection(page, 'workspace', 'Terminal work stays first-class')
}

async function openSection(page: Page, section: string, heading: string) {
  await ensureStudioNavVisible(page)
  await page.getByTestId(`nav-section-${section}`).click()
  await expect(page.getByRole('heading', { name: heading })).toBeVisible()
}

async function ensureStudioNavVisible(page: Page) {
  if ((await page.getByTestId('studio-sidebar').getAttribute('aria-hidden')) === 'true') {
    await page.getByTestId('workspace-studio-nav-toggle').click()
    await expect(page.getByTestId('studio-sidebar')).toHaveAttribute('aria-hidden', 'false')
  }
}

async function openConfigPanel(page: Page) {
  await openLaunchBlade(page)
  const configPanel = page.getByTestId('config-panel')
  const isOpen = await configPanel.evaluate((element) => (element as HTMLDetailsElement).open)
  if (!isOpen) {
    await page.getByTestId('config-panel-toggle').click()
  }
}

async function launchSession(
  page: Page,
  request: APIRequestContext,
): Promise<string> {
  const beforeIds = new Set((await listSessions(request)).map((session) => session.id))

  await openLaunchBlade(page)
  await page.getByTestId('launch-session').click()

  const resolvedSessionId = await waitForNewSessionId(request, beforeIds)
  await expect(page.getByTestId(`workspace-tab-session:${resolvedSessionId}`)).toBeVisible()
  await expect(page.getByTestId(`terminal-shell-${resolvedSessionId}`)).toBeVisible()
  return resolvedSessionId
}

async function listSessions(request: APIRequestContext): Promise<Array<{ id: string }>> {
  const response = await request.get('/api/sessions')
  expect(response.ok()).toBeTruthy()
  return (await response.json()) as Array<{ id: string }>
}

async function stopAllSessions(request: APIRequestContext) {
  const sessions = await listSessions(request)

  for (const session of sessions) {
    const response = await request.post(`/api/sessions/${session.id}/stop`)
    expect(response.ok()).toBeTruthy()
  }

  await expect.poll(async () => (await listSessions(request)).length).toBe(0)
}

async function restoreBaselineConfig(request: APIRequestContext) {
  const response = await request.put('/api/config', {
    data: originalConfig,
  })

  expect(response.ok()).toBeTruthy()
}

async function clearAuditLog() {
  await fs.mkdir(path.dirname(logPath), { recursive: true })
  await fs.writeFile(logPath, '', 'utf8')
}

function commandAvailable(command: string, args: string[]): boolean {
  try {
    execFileSync(command, args, {
      stdio: 'ignore',
      timeout: 15_000,
      windowsHide: true,
    })
    return true
  } catch {
    return false
  }
}

function stripBom(value: string): string {
  return value.replace(/^\uFEFF/, '')
}

async function waitForNewSessionId(request: APIRequestContext, beforeIds: Set<string>): Promise<string> {
  let sessionId: string | null = null

  await expect.poll(async () => {
    const sessions = await listSessions(request)
    sessionId = sessions.find((session) => !beforeIds.has(session.id))?.id ?? null
    return sessionId
  }).toBeTruthy()

  return sessionId!
}

async function reopenSessionFromManifest(page: Page, sessionId: string) {
  await openLaunchBlade(page)
  const sessionCard = page.getByTestId(`session-card-${sessionId}`)
  await sessionCard.evaluate((element) => {
    element.scrollIntoView({ block: 'center', inline: 'nearest' })
  })
  await sessionCard.focus()
  await page.keyboard.press('Enter')
}

async function openLaunchBlade(page: Page) {
  if ((await page.getByTestId('workspace-launch-blade').getAttribute('aria-hidden')) === 'true') {
    await page.getByTestId('workspace-launch-blade-toggle').click()
    await expect(page.getByTestId('workspace-launch-blade')).toHaveAttribute('aria-hidden', 'false')
  }
}

async function closeLaunchBlade(page: Page) {
  if ((await page.getByTestId('workspace-launch-blade').getAttribute('aria-hidden')) === 'false') {
    await page.getByLabel('Close launch blade').click()
    await expect(page.getByTestId('workspace-launch-blade')).toHaveAttribute('aria-hidden', 'true')
  }
}

async function waitForPromptAfter(socket: SessionSocket, marker: string) {
  await expect
    .poll(() => {
      const transcript = socket.transcript()
      const markerIndex = transcript.lastIndexOf(marker)
      if (markerIndex === -1) {
        return false
      }

      return /[\r\n]>\s/.test(transcript.slice(markerIndex))
    }, { timeout: 30_000 })
    .toBe(true)
}

function flattenTerminalTranscript(transcript: string) {
  return transcript
    .replace(terminalOscSequencePattern, '')
    .replace(terminalCsiSequencePattern, '')
    .replace(terminalBellPattern, '')
    .replace(/\r?\n/g, '')
}


async function expectTerminalReady(pane: Locator) {
  await expect(pane.getByText('Stream live')).toBeVisible()
  await expect(pane.getByRole('textbox', { name: 'Terminal input' })).toBeAttached()
}

interface SessionSocket {
  close: () => void
  sendInput: (data: string) => void
  transcript: () => string
}

const terminalOscSequencePattern = new RegExp(String.raw`\u001B\][^\u0007]*(?:\u0007|\u001B\\)`, 'g')
const terminalCsiSequencePattern = new RegExp(String.raw`\u001B\[[0-?]*[ -/]*[@-~]`, 'g')
const terminalBellPattern = new RegExp(String.raw`\u0007`, 'g')

async function attachSessionSocket(sessionId: string): Promise<SessionSocket> {
  const socket = new WebSocket(`ws://127.0.0.1:5023/ws/session/${sessionId}`)
  let transcript = ''

  socket.addEventListener('message', (event) => {
    const message = JSON.parse(String(event.data)) as {
      data?: string | null
      message?: string | null
      type?: string
    }

    if (message.type === 'output' && typeof message.data === 'string') {
      transcript += message.data
    }

    if (message.type === 'error' && typeof message.message === 'string') {
      transcript += `\n${message.message}`
    }
  })

  await new Promise<void>((resolve, reject) => {
    const timeoutId = setTimeout(() => reject(new Error(`Timed out connecting websocket for ${sessionId}.`)), 15_000)

    const handleOpen = () => {
      clearTimeout(timeoutId)
      resolve()
    }

    const handleError = () => {
      clearTimeout(timeoutId)
      reject(new Error(`WebSocket connection failed for ${sessionId}.`))
    }

    socket.addEventListener('open', handleOpen, { once: true })
    socket.addEventListener('error', handleError, { once: true })
  })

  return {
    close: () => {
      if (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING) {
        socket.close()
      }
    },
    sendInput: (data: string) => {
      socket.send(JSON.stringify({ type: 'input', data }))
    },
    transcript: () => transcript,
  }
}

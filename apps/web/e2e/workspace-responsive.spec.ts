import { expect, test, type Locator, type Page } from '@playwright/test'

const coreViewports = [
  { name: 'mobile-360', width: 360, height: 800 },
  { name: 'mobile-390', width: 390, height: 844 },
  { name: 'tablet-768', width: 768, height: 1024 },
  { name: 'laptop-1280', width: 1280, height: 800 },
  { name: 'desktop-1440', width: 1440, height: 900 },
  { name: 'desktop-1920', width: 1920, height: 1080 },
] as const

const stageControlIds = ['workspace-studio-nav-toggle', 'refresh-sessions', 'workspace-launch-blade-toggle', 'single-pane'] as const
const bladeControlIds = ['launch-backplane', 'launch-host', 'launch-connector', 'launch-working-directory', 'launch-cols', 'launch-rows', 'launch-session'] as const
const claudeBladeControlIds = ['launch-model', 'launch-permission-mode', 'launch-agent', 'launch-skip-permissions', 'launch-allowed-tools'] as const

test.describe('workspace responsiveness and accessibility', () => {
  test('keeps the focused workspace fully visible across the core viewport matrix', async ({ page }) => {
    for (const viewport of coreViewports) {
      await test.step(`${viewport.name} stage chrome remains visible`, async () => {
        await page.setViewportSize({ width: viewport.width, height: viewport.height })
        await openWorkspace(page)
        await assertViewportLocked(page)

        await expect(page.getByTestId('studio-sidebar')).toHaveAttribute('aria-hidden', 'true')

        for (const testId of stageControlIds) {
          await assertVisibleAndInteractable(page.getByTestId(testId))
        }

        await page.getByTestId('workspace-studio-nav-toggle').click()
        await expect(page.getByTestId('studio-sidebar')).toHaveAttribute('aria-hidden', 'false')
        await assertVisibleAndInteractable(page.getByTestId('nav-section-sessions'))
      })
    }
  })

  test('keeps launch blade controls reachable without clipping across the core viewport matrix', async ({ page }) => {
    for (const viewport of coreViewports) {
      await test.step(`${viewport.name} launch blade remains reachable`, async () => {
        await page.setViewportSize({ width: viewport.width, height: viewport.height })
        await openWorkspace(page)

        await page.getByTestId('workspace-launch-blade-toggle').click()
        await expect(page.getByTestId('workspace-launch-blade')).toHaveAttribute('aria-hidden', 'false')

        for (const testId of bladeControlIds) {
          await assertVisibleAndInteractable(page.getByTestId(testId))
        }

        await page.getByTestId('launch-connector').selectOption('claude')

        for (const testId of claudeBladeControlIds) {
          await assertVisibleAndInteractable(page.getByTestId(testId))
        }
      })
    }
  })
})

async function openWorkspace(page: Page) {
  await page.goto('/')
  await expect(page.getByTestId('nav-section-workspace')).toBeVisible()
  await page.getByTestId('nav-section-workspace').click()
  await expect(page.getByTestId('product-page-workspace')).toBeVisible()
  await page.addStyleTag({
    content: `
      *,
      *::before,
      *::after {
        animation: none !important;
        transition: none !important;
      }
    `,
  })
  await expect(page.getByRole('heading', { name: 'Terminal work stays first-class' })).toBeVisible()
}

async function assertViewportLocked(page: Page) {
  const metrics = await page.evaluate(() => ({
    innerHeight: window.innerHeight,
    innerWidth: window.innerWidth,
    scrollHeight: document.documentElement.scrollHeight,
    scrollWidth: document.documentElement.scrollWidth,
    bodyScrollHeight: document.body.scrollHeight,
    bodyScrollWidth: document.body.scrollWidth,
  }))

  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.innerWidth + 1)
  expect(metrics.bodyScrollWidth).toBeLessThanOrEqual(metrics.innerWidth + 1)
  expect(metrics.scrollHeight).toBeLessThanOrEqual(metrics.innerHeight + 1)
  expect(metrics.bodyScrollHeight).toBeLessThanOrEqual(metrics.innerHeight + 1)
}

async function assertVisibleAndInteractable(locator: Locator) {
  await locator.evaluate((element) => {
    element.scrollIntoView({ block: 'center', inline: 'nearest' })
  })
  await expect(locator).toBeVisible()

  const box = await locator.boundingBox()
  expect(box).not.toBeNull()

  const viewport = locator.page().viewportSize()
  expect(viewport).not.toBeNull()

  expect(box!.x).toBeGreaterThanOrEqual(0)
  expect(box!.y).toBeGreaterThanOrEqual(0)
  expect(box!.x + box!.width).toBeLessThanOrEqual(viewport!.width + 1)
  expect(box!.y + box!.height).toBeLessThanOrEqual(viewport!.height + 1)

  const targetVisible = await locator.evaluate((element) => {
    const rect = element.getBoundingClientRect()
    const centerX = rect.left + rect.width / 2
    const centerY = rect.top + rect.height / 2
    const target = document.elementFromPoint(centerX, centerY)
    return target === element || element.contains(target)
  })

  expect(targetVisible).toBe(true)
}

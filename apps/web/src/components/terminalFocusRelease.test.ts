import { afterEach, describe, expect, it, vi } from 'vitest'

import { createTerminalPageInteractionRelease } from './terminalFocusRelease'

describe('createTerminalPageInteractionRelease', () => {
  afterEach(() => {
    document.body.innerHTML = ''
    vi.restoreAllMocks()
  })

  it.each(['wheel', 'touchmove', 'pointerdown'] as const)(
    'releases terminal focus when %s starts outside the terminal host',
    (eventType) => {
      const host = document.createElement('div')
      const helper = document.createElement('textarea')
      const outside = document.createElement('div')

      helper.className = 'xterm-helper-textarea'
      host.append(helper)
      document.body.append(host, outside)

      const blurTerminal = vi.fn(() => helper.blur())
      const releasePageInteraction = createTerminalPageInteractionRelease(host, blurTerminal)
      helper.focus()

      outside.dispatchEvent(new Event(eventType, { bubbles: true }))

      expect(blurTerminal).toHaveBeenCalledTimes(1)

      releasePageInteraction()
    },
  )

  it('keeps focus when the interaction stays inside the terminal host', () => {
    const host = document.createElement('div')
    const helper = document.createElement('textarea')
    const inside = document.createElement('div')

    helper.className = 'xterm-helper-textarea'
    host.append(helper, inside)
    document.body.append(host)

    const blurTerminal = vi.fn(() => helper.blur())
    const releasePageInteraction = createTerminalPageInteractionRelease(host, blurTerminal)
    helper.focus()

    inside.dispatchEvent(new Event('wheel', { bubbles: true }))

    expect(blurTerminal).not.toHaveBeenCalled()

    releasePageInteraction()
  })

  it('stops releasing focus after dispose', () => {
    const host = document.createElement('div')
    const helper = document.createElement('textarea')
    const outside = document.createElement('div')

    helper.className = 'xterm-helper-textarea'
    host.append(helper)
    document.body.append(host, outside)

    const blurTerminal = vi.fn(() => helper.blur())
    const releasePageInteraction = createTerminalPageInteractionRelease(host, blurTerminal)
    helper.focus()
    releasePageInteraction()

    outside.dispatchEvent(new Event('pointerdown', { bubbles: true }))

    expect(blurTerminal).not.toHaveBeenCalled()
  })
})

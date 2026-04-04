import { afterEach, describe, expect, it, vi } from 'vitest'

import { createTerminalPageScrollLock } from './terminalPageScrollLock'

describe('createTerminalPageScrollLock', () => {
  afterEach(() => {
    document.body.innerHTML = ''
    vi.restoreAllMocks()
    Object.defineProperty(window, 'scrollX', { configurable: true, writable: true, value: 0 })
    Object.defineProperty(window, 'scrollY', { configurable: true, writable: true, value: 0 })
  })

  it('restores the page scroll position while the xterm helper textarea is focused', () => {
    const host = document.createElement('div')
    const helper = document.createElement('textarea')
    helper.className = 'xterm-helper-textarea'
    host.append(helper)
    document.body.append(host)

    Object.defineProperty(window, 'scrollX', { configurable: true, writable: true, value: 18 })
    Object.defineProperty(window, 'scrollY', { configurable: true, writable: true, value: 42 })

    const scrollTo = vi
      .spyOn(window, 'scrollTo')
      .mockImplementation((xOrOptions?: number | ScrollToOptions, y?: number) => {
        if (typeof xOrOptions === 'object' && xOrOptions !== null) {
          Object.defineProperty(window, 'scrollX', {
            configurable: true,
            writable: true,
            value: xOrOptions.left ?? window.scrollX,
          })
          Object.defineProperty(window, 'scrollY', {
            configurable: true,
            writable: true,
            value: xOrOptions.top ?? window.scrollY,
          })
          return
        }

        Object.defineProperty(window, 'scrollX', {
          configurable: true,
          writable: true,
          value: xOrOptions ?? window.scrollX,
        })
        Object.defineProperty(window, 'scrollY', { configurable: true, writable: true, value: y ?? window.scrollY })
      })

    const releasePageScrollLock = createTerminalPageScrollLock(host)
    helper.focus()

    Object.defineProperty(window, 'scrollX', { configurable: true, writable: true, value: 96 })
    Object.defineProperty(window, 'scrollY', { configurable: true, writable: true, value: 144 })
    window.dispatchEvent(new Event('scroll'))

    expect(scrollTo).toHaveBeenCalledWith(18, 42)

    releasePageScrollLock()
  })
})

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createTerminalOutputScrollGuard } from './terminalOutputScrollGuard'

describe('createTerminalOutputScrollGuard', () => {
  let animationFrameQueue: FrameRequestCallback[]

  beforeEach(() => {
    animationFrameQueue = []

    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((callback: FrameRequestCallback) => {
      animationFrameQueue.push(callback)
      return animationFrameQueue.length
    })

    vi.spyOn(window, 'cancelAnimationFrame').mockImplementation((frameId: number) => {
      animationFrameQueue[frameId - 1] = () => 0
    })
  })

  afterEach(() => {
    document.body.innerHTML = ''
    vi.restoreAllMocks()
    Object.defineProperty(window, 'scrollX', { configurable: true, writable: true, value: 0 })
    Object.defineProperty(window, 'scrollY', { configurable: true, writable: true, value: 0 })
  })

  it('restores the page scroll position after terminal output repositions the helper textarea', () => {
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

    const outputScrollGuard = createTerminalOutputScrollGuard(host)
    helper.focus()

    outputScrollGuard.preservePageScroll((restorePageScroll) => {
      Object.defineProperty(window, 'scrollX', { configurable: true, writable: true, value: 96 })
      Object.defineProperty(window, 'scrollY', { configurable: true, writable: true, value: 144 })
      restorePageScroll()
    })

    flushAnimationFrames(animationFrameQueue)

    expect(scrollTo).toHaveBeenCalledWith(18, 42)

    outputScrollGuard.dispose()
  })

  it('keeps the original scroll baseline across overlapping output writes', () => {
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

    const outputScrollGuard = createTerminalOutputScrollGuard(host)
    helper.focus()

    outputScrollGuard.preservePageScroll((restorePageScroll) => {
      Object.defineProperty(window, 'scrollX', { configurable: true, writable: true, value: 96 })
      Object.defineProperty(window, 'scrollY', { configurable: true, writable: true, value: 144 })
      restorePageScroll()
    })

    Object.defineProperty(window, 'scrollX', { configurable: true, writable: true, value: 96 })
    Object.defineProperty(window, 'scrollY', { configurable: true, writable: true, value: 144 })

    outputScrollGuard.preservePageScroll((restorePageScroll) => {
      Object.defineProperty(window, 'scrollX', { configurable: true, writable: true, value: 120 })
      Object.defineProperty(window, 'scrollY', { configurable: true, writable: true, value: 180 })
      restorePageScroll()
    })

    flushAnimationFrames(animationFrameQueue)

    expect(scrollTo).toHaveBeenLastCalledWith(18, 42)

    outputScrollGuard.dispose()
  })

  it('does not block ordinary page scrolling while the helper textarea is focused', () => {
    const host = document.createElement('div')
    const helper = document.createElement('textarea')
    helper.className = 'xterm-helper-textarea'
    host.append(helper)
    document.body.append(host)

    const scrollTo = vi.spyOn(window, 'scrollTo')
    const outputScrollGuard = createTerminalOutputScrollGuard(host)

    helper.focus()
    Object.defineProperty(window, 'scrollY', { configurable: true, writable: true, value: 300 })
    flushAnimationFrames(animationFrameQueue)

    expect(scrollTo).not.toHaveBeenCalled()

    outputScrollGuard.dispose()
  })

  it('cancels stale restore frames after the terminal loses focus', () => {
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

    const outputScrollGuard = createTerminalOutputScrollGuard(host)
    helper.focus()

    outputScrollGuard.preservePageScroll((restorePageScroll) => {
      Object.defineProperty(window, 'scrollX', { configurable: true, writable: true, value: 96 })
      Object.defineProperty(window, 'scrollY', { configurable: true, writable: true, value: 144 })
      restorePageScroll()
    })

    helper.blur()
    helper.focus()
    flushAnimationFrames(animationFrameQueue)

    expect(scrollTo).toHaveBeenCalledTimes(1)
    expect(scrollTo).toHaveBeenCalledWith(18, 42)

    outputScrollGuard.dispose()
  })
})

function flushAnimationFrames(queue: FrameRequestCallback[]) {
  while (queue.length > 0) {
    const callback = queue.shift()
    callback?.(performance.now())
  }
}

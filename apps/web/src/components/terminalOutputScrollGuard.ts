interface TerminalOutputScrollGuard {
  preservePageScroll: (operation: (restorePageScroll: () => void) => void) => void
  dispose: () => void
}

export function createTerminalOutputScrollGuard(
  host: HTMLElement,
  win: Window = window,
  doc: Document = document,
): TerminalOutputScrollGuard {
  let lockedScroll: { x: number; y: number } | null = null
  let restoreToken = 0
  let settleFrameId: number | null = null
  let trailingFrameId: number | null = null

  const isHelperFocused = () => {
    const activeElement = doc.activeElement

    return (
      activeElement instanceof HTMLTextAreaElement &&
      activeElement.classList.contains('xterm-helper-textarea') &&
      host.contains(activeElement)
    )
  }

  const clearPendingRestore = () => {
    if (settleFrameId !== null) {
      win.cancelAnimationFrame(settleFrameId)
      settleFrameId = null
    }

    if (trailingFrameId !== null) {
      win.cancelAnimationFrame(trailingFrameId)
      trailingFrameId = null
    }
  }

  const restorePageScroll = (scrollX: number, scrollY: number) => {
    if (!isHelperFocused()) {
      return
    }

    if (win.scrollX === scrollX && win.scrollY === scrollY) {
      return
    }

    win.scrollTo(scrollX, scrollY)
  }

  const cancelRestore = () => {
    restoreToken += 1
    clearPendingRestore()
    lockedScroll = null
  }

  host.addEventListener('focusout', cancelRestore, true)

  return {
    preservePageScroll(operation) {
      if (!isHelperFocused()) {
        cancelRestore()
        operation(() => {})
        return
      }

      lockedScroll ??= {
        x: win.scrollX,
        y: win.scrollY,
      }

      operation(() => {
        const preservedScroll = lockedScroll
        if (!preservedScroll) {
          return
        }

        const token = restoreToken
        clearPendingRestore()
        restorePageScroll(preservedScroll.x, preservedScroll.y)
        settleFrameId = win.requestAnimationFrame(() => {
          if (token !== restoreToken) {
            return
          }

          settleFrameId = null
          restorePageScroll(preservedScroll.x, preservedScroll.y)
          trailingFrameId = win.requestAnimationFrame(() => {
            if (token !== restoreToken) {
              return
            }

            trailingFrameId = null
            restorePageScroll(preservedScroll.x, preservedScroll.y)
            lockedScroll = null
          })
        })
      })
    },
    dispose() {
      host.removeEventListener('focusout', cancelRestore, true)
      cancelRestore()
    },
  }
}

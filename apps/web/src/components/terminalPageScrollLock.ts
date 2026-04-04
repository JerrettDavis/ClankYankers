export function createTerminalPageScrollLock(host: HTMLElement, win: Window = window, doc: Document = document) {
  let lockedScrollX = 0
  let lockedScrollY = 0

  const isHelperFocused = () => {
    const activeElement = doc.activeElement

    return (
      activeElement instanceof HTMLTextAreaElement &&
      activeElement.classList.contains('xterm-helper-textarea') &&
      host.contains(activeElement)
    )
  }

  const syncLockedScroll = () => {
    if (!isHelperFocused()) {
      return
    }

    lockedScrollX = win.scrollX
    lockedScrollY = win.scrollY
  }

  const restorePageScroll = () => {
    if (!isHelperFocused()) {
      return
    }

    if (win.scrollX === lockedScrollX && win.scrollY === lockedScrollY) {
      return
    }

    win.scrollTo(lockedScrollX, lockedScrollY)
  }

  host.addEventListener('focusin', syncLockedScroll)
  win.addEventListener('scroll', restorePageScroll, { passive: true })

  return () => {
    host.removeEventListener('focusin', syncLockedScroll)
    win.removeEventListener('scroll', restorePageScroll)
  }
}

export function createTerminalPageInteractionRelease(
  host: HTMLElement,
  blurTerminal: () => void,
  doc: Document = document,
) {
  const shouldReleaseFocus = (target: EventTarget | null) => {
    const activeElement = doc.activeElement

    if (
      !(activeElement instanceof HTMLTextAreaElement) ||
      !activeElement.classList.contains('xterm-helper-textarea') ||
      !host.contains(activeElement)
    ) {
      return false
    }

    return !(target instanceof Node && host.contains(target))
  }

  const handleInteraction = (event: Event) => {
    if (!shouldReleaseFocus(event.target)) {
      return
    }

    blurTerminal()
  }

  doc.addEventListener('wheel', handleInteraction, { capture: true, passive: true })
  doc.addEventListener('touchmove', handleInteraction, { capture: true, passive: true })
  doc.addEventListener('pointerdown', handleInteraction, { capture: true })

  return () => {
    doc.removeEventListener('wheel', handleInteraction, true)
    doc.removeEventListener('touchmove', handleInteraction, true)
    doc.removeEventListener('pointerdown', handleInteraction, true)
  }
}

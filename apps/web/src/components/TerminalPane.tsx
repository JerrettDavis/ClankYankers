import '@xterm/xterm/css/xterm.css'

import { FitAddon } from '@xterm/addon-fit'
import { Terminal } from '@xterm/xterm'
import { useEffect, useLayoutEffect, useRef, useState } from 'react'

import type { TerminalClientMessage, TerminalServerMessage } from '../types'
import { createTerminalPageInteractionRelease } from './terminalFocusRelease'
import { createTerminalOutputScrollGuard } from './terminalOutputScrollGuard'

type ConnectionState = 'connecting' | 'live' | 'closed' | 'error'
type ThemeMode = 'light' | 'dark'

interface TerminalPaneProps {
  label?: string
  sessionId: string
  onSessionMessage?: (sessionId: string, message: TerminalServerMessage) => void
  themeMode: ThemeMode
}

export function TerminalPane({ label, sessionId, onSessionMessage, themeMode }: TerminalPaneProps) {
  const frameRef = useRef<HTMLDivElement | null>(null)
  const hostRef = useRef<HTMLDivElement | null>(null)
  const viewportRef = useRef<HTMLDivElement | null>(null)
  const terminalRef = useRef<Terminal | null>(null)
  const onSessionMessageRef = useRef(onSessionMessage)
  const themeModeRef = useRef(themeMode)
  const [connectionState, setConnectionState] = useState<ConnectionState>('connecting')

  useEffect(() => {
    onSessionMessageRef.current = onSessionMessage
  }, [onSessionMessage])

  useEffect(() => {
    themeModeRef.current = themeMode

    const terminal = terminalRef.current
    if (!terminal) {
      return
    }

    terminal.options.theme = getTerminalTheme(themeMode)
    if (terminal.rows > 0) {
      terminal.refresh(0, terminal.rows - 1)
    }
  }, [themeMode])

  useLayoutEffect(() => {
    const frame = frameRef.current
    const host = hostRef.current
    const viewport = viewportRef.current
    if (!frame || !host || !viewport) {
      return undefined
    }

    host.replaceChildren()

    const terminal = new Terminal({
      allowTransparency: true,
      cursorBlink: true,
      cursorStyle: 'bar',
      disableStdin: true,
      fontFamily: '"Cascadia Code", "Iosevka Term", "SFMono-Regular", Consolas, monospace',
      fontSize: 14,
      lineHeight: 1.25,
      scrollback: 5000,
      theme: getTerminalTheme(themeModeRef.current),
    })

    terminalRef.current = terminal

    const fitAddon = new FitAddon()
    terminal.loadAddon(fitAddon)
    terminal.open(host)
    const releaseTerminalPageInteraction = createTerminalPageInteractionRelease(frame, () => terminal.blur())
    const outputScrollGuard = createTerminalOutputScrollGuard(host)

    const protocol = window.location.protocol === 'https:' ? 'wss' : 'ws'
    const socket = new WebSocket(`${protocol}://${window.location.host}/ws/session/${sessionId}`)
    let fitFrameId: number | null = null
    let isInteractive = false

    const sendMessage = (message: TerminalClientMessage) => {
      if (socket.readyState === WebSocket.OPEN) {
        socket.send(JSON.stringify(message))
      }
    }

    const setInteractive = (interactive: boolean) => {
      isInteractive = interactive
      terminal.options.disableStdin = !interactive

      if (!interactive) {
        terminal.blur()
      }
    }

    const fitTerminal = () => {
      try {
        fitAddon.fit()
      } catch {
        return
      }

      const dimensions = fitAddon.proposeDimensions()
      if (dimensions) {
        sendMessage({
          type: 'resize',
          cols: dimensions.cols,
          rows: dimensions.rows,
        })
      }
    }

    const scheduleFit = () => {
      if (fitFrameId !== null) {
        window.cancelAnimationFrame(fitFrameId)
      }

      fitFrameId = window.requestAnimationFrame(() => {
        fitFrameId = null
        fitTerminal()
      })
    }

    const resizeObserver = new ResizeObserver(() => {
      scheduleFit()
    })

    resizeObserver.observe(viewport)
    resizeObserver.observe(frame)
    scheduleFit()

    const dataDisposable = terminal.onData((data) => {
      sendMessage({
        type: 'input',
        data,
      })
    })

    const resizeDisposable = terminal.onResize(({ cols, rows }) => {
      sendMessage({
        type: 'resize',
        cols,
        rows,
      })
    })

    let isDisposed = false

    const handleSocketOpen = () => {
      if (isDisposed) {
        return
      }

      setConnectionState('live')
      setInteractive(true)
      focusTerminal(host, terminal)
      scheduleFit()
    }

    const handleSocketMessage = (event: MessageEvent<string>) => {
      if (isDisposed) {
        return
      }

      const message = JSON.parse(event.data) as TerminalServerMessage
      onSessionMessageRef.current?.(sessionId, message)

      if (message.type === 'output' && typeof message.data === 'string') {
        const output = message.data
        outputScrollGuard.preservePageScroll((restorePageScroll) => {
          terminal.write(output, restorePageScroll)
        })
        return
      }

      if (message.type === 'error' && typeof message.message === 'string') {
        const errorMessage = message.message
        outputScrollGuard.preservePageScroll((restorePageScroll) => {
          terminal.writeln(`\r\n[session error] ${errorMessage}`, restorePageScroll)
        })
      }
    }

    const handleSocketClose = () => {
      if (isDisposed) {
        return
      }

      setConnectionState('closed')
      setInteractive(false)
    }

    const handleSocketError = () => {
      if (isDisposed) {
        return
      }

      setConnectionState('error')
      setInteractive(false)
    }

    socket.addEventListener('open', handleSocketOpen)
    socket.addEventListener('message', handleSocketMessage)
    socket.addEventListener('close', handleSocketClose)
    socket.addEventListener('error', handleSocketError)

    const handleFramePointerDown = (event: PointerEvent) => {
      if (isInteractive && event.target instanceof Node && !host.contains(event.target)) {
        focusTerminal(host, terminal)
      }
    }

    frame.addEventListener('pointerdown', handleFramePointerDown)

    return () => {
      isDisposed = true
      frame.removeEventListener('pointerdown', handleFramePointerDown)
      releaseTerminalPageInteraction()
      outputScrollGuard.dispose()
      resizeDisposable.dispose()
      dataDisposable.dispose()
      resizeObserver.disconnect()
      if (fitFrameId !== null) {
        window.cancelAnimationFrame(fitFrameId)
      }

      socket.removeEventListener('open', handleSocketOpen)
      socket.removeEventListener('message', handleSocketMessage)
      socket.removeEventListener('close', handleSocketClose)
      socket.removeEventListener('error', handleSocketError)

      if (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING) {
        socket.close()
      }

      setInteractive(false)
      terminal.reset()
      terminal.dispose()
      terminalRef.current = null
      host.replaceChildren()
    }
  }, [sessionId])

  return (
    <div
      className={`terminal-shell terminal-shell--${connectionState}`}
      data-session-id={sessionId}
      data-testid={`terminal-shell-${sessionId}`}
    >
      <div className="terminal-shell__bar">
        <strong className="terminal-shell__title" title={sessionId}>
          {label ?? sessionId}
        </strong>
        <div className={`terminal-connection terminal-connection--${connectionState}`}>
          {connectionLabel[connectionState]}
        </div>
      </div>
      <div ref={viewportRef} className="terminal-scrollport" data-testid={`terminal-scrollport-${sessionId}`}>
        <div ref={frameRef} className="terminal-frame" data-testid={`terminal-frame-${sessionId}`}>
          <div ref={hostRef} className="terminal-canvas" data-testid={`terminal-canvas-${sessionId}`} />
        </div>
      </div>
    </div>
  )
}

const connectionLabel: Record<ConnectionState, string> = {
  connecting: 'Attaching stream',
  live: 'Stream live',
  closed: 'Stream detached',
  error: 'Stream interrupted',
}

function focusTerminal(host: HTMLDivElement, terminal: Terminal) {
  const helperTextarea = host.querySelector<HTMLTextAreaElement>('.xterm-helper-textarea')
  if (helperTextarea) {
    helperTextarea.focus({ preventScroll: true })
    return
  }

  terminal.focus()
}

function getTerminalTheme(themeMode: ThemeMode) {
  if (themeMode === 'dark') {
    return {
      background: '#12131a',
      foreground: '#edf1f8',
      cursor: '#8aa0ff',
      selectionBackground: 'rgba(138, 160, 255, 0.24)',
      black: '#151821',
      red: '#ff8f91',
      green: '#a7d4a2',
      yellow: '#e7cc90',
      blue: '#95aeff',
      magenta: '#d3b3ff',
      cyan: '#89d4df',
      white: '#dfe6f2',
      brightBlack: '#4f5668',
      brightRed: '#ffb2b2',
      brightGreen: '#c5e8b4',
      brightYellow: '#f1deab',
      brightBlue: '#c1d1ff',
      brightMagenta: '#e5ceff',
      brightCyan: '#a5e2ea',
      brightWhite: '#f7f9fc',
    }
  }

  return {
    background: '#171b25',
    foreground: '#edf1f8',
    cursor: '#7f90ff',
    selectionBackground: 'rgba(127, 144, 255, 0.22)',
    black: '#171b25',
    red: '#f48f93',
    green: '#9fcea4',
    yellow: '#e8cc86',
    blue: '#8ea6ff',
    magenta: '#caa8ff',
    cyan: '#84d4e0',
    white: '#e4ebf7',
    brightBlack: '#596177',
    brightRed: '#ffb1b0',
    brightGreen: '#bee4b8',
    brightYellow: '#f1dfab',
    brightBlue: '#b8c7ff',
    brightMagenta: '#dfc8ff',
    brightCyan: '#a4e3eb',
    brightWhite: '#f7f9fc',
  }
}
